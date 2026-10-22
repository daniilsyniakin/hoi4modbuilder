using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <summary>
/// Starting points for new entities, inserted into a file where the game expects them: inside
/// <c>ideas = { slot = { … } }</c>, a decision category, <c>characters = { … }</c>… Missing
/// wrappers are created. Applied to an empty text they produce a whole new file.
/// </summary>
public static class EntityTemplates
{
    /// <summary>The kinds that can be created (states need provinces from the map, so they can't).</summary>
    public static IReadOnlyList<ContentKind> Creatable { get; } =
    [
        ContentKind.FocusTree, ContentKind.Event, ContentKind.Idea, ContentKind.Decision, ContentKind.Technology,
        ContentKind.Character, ContentKind.Country, ContentKind.ScriptedEffect, ContentKind.ScriptedTrigger,
    ];

    /// <summary>The folder (relative to the mod) the kind lives in.</summary>
    public static string Folder(ContentKind kind) => kind switch
    {
        ContentKind.FocusTree or ContentKind.Focus => "common/national_focus",
        ContentKind.Event => "events",
        ContentKind.Idea => "common/ideas",
        ContentKind.Decision => "common/decisions",
        ContentKind.Technology => "common/technologies",
        ContentKind.Character => "common/characters",
        ContentKind.Country => "common/country_tags",
        ContentKind.State => "history/states",
        ContentKind.ScriptedEffect => "common/scripted_effects",
        ContentKind.ScriptedTrigger => "common/scripted_triggers",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The edits that add a new <paramref name="kind"/> called <paramref name="id"/> to a file.
    /// </summary>
    /// <param name="group">Where inside the file: the idea slot, the decision category, the event namespace.</param>
    public static IReadOnlyList<TextEdit> Add(ContentKind kind, string text, string id, string? group = null)
    {
        var tree = SyntaxTree.Parse(text);
        return kind switch
        {
            ContentKind.FocusTree => [ScriptEdits.InsertMultiLine(text, tree.Root, FocusTree(id))],
            ContentKind.Event => AddEvent(text, tree, id),
            ContentKind.Idea => Nested(text, tree.Root, ["ideas", group ?? "country"], Idea(id)),
            ContentKind.Decision => Nested(text, tree.Root, [group ?? "decisions"], Decision(id)),
            ContentKind.Technology => Nested(text, tree.Root, ["technologies"], Technology(id)),
            ContentKind.Character => Nested(text, tree.Root, ["characters"], Character(id)),
            ContentKind.Country => [ScriptEdits.InsertStatement(text, tree.Root, $"{id} = \"countries/{id}.txt\"")],
            ContentKind.ScriptedEffect or ContentKind.ScriptedTrigger => [ScriptEdits.InsertMultiLine(text, tree.Root, $"{id} = {{\n}}")],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>A whole new file with one entity.</summary>
    public static string NewFile(ContentKind kind, string id, string? group = null) =>
        ScriptEdits.Apply("", Add(kind, "", id, group));

    /// <summary>A decision category for <c>common/decisions/categories</c>.</summary>
    public static string NewDecisionCategory(string id) => $"{id} = {{\n\ticon = generic_decision\n}}\n";

    /// <summary><c>common/countries/TAG.txt</c>: how the country looks.</summary>
    public static string NewCountryLook() =>
        "graphical_culture = western_european_gfx\ngraphical_culture_2d = western_european_2d\n\ncolor = { 120 120 120 }\n";

    /// <summary><c>history/countries/TAG - Name.txt</c>: where it starts.</summary>
    public static string NewCountryHistory() => "capital = 1\n\nset_politics = {\n\truling_party = neutrality\n}\n";

    /// <summary>The namespace of an event id: <c>ukr.4</c> → <c>ukr</c>.</summary>
    public static string? Namespace(string eventId) => eventId.LastIndexOf('.') is > 0 and var dot ? eventId[..dot] : null;

    private static IReadOnlyList<TextEdit> AddEvent(string text, SyntaxTree tree, string id)
    {
        var edits = new List<TextEdit>();
        var ns = Namespace(id);
        if (text.Trim().Length == 0)
        {
            // A new file: one edit, so the namespace surely comes first.
            var header = ns is null ? "" : $"add_namespace = {ns}\n\n";
            return [new TextEdit(new TextSpan(0, text.Length), header + Event(id) + "\n")];
        }
        var hasNamespace = ns is null || tree.Root.Properties("add_namespace")
            .Any(p => p.Value is ScalarSyntax scalar && scalar.ValueText.Equals(ns, StringComparison.OrdinalIgnoreCase));
        if (!hasNamespace)
        {
            // The game wants the namespace declared before the events that use it.
            var newLine = ScriptEdits.NewLine(text);
            edits.Add(new TextEdit(new TextSpan(0, 0), $"add_namespace = {ns}{newLine}{(text.Length > 0 ? newLine : "")}"));
        }
        var statement = Event(id);
        edits.Add(ScriptEdits.InsertMultiLine(text, tree.Root, text.Trim().Length > 0 || !hasNamespace ? "\n" + statement : statement));
        return edits;
    }

    /// <summary>
    /// Puts <paramref name="statement"/> inside the chain of blocks <paramref name="path"/>
    /// (e.g. <c>ideas</c> → <c>country</c>), creating the blocks that are missing.
    /// </summary>
    private static IReadOnlyList<TextEdit> Nested(string text, IStatementContainer container, IReadOnlyList<string> path, string statement)
    {
        IStatementContainer current = container;
        for (var i = 0; i < path.Count; i++)
        {
            var next = ScriptRead.Blocks(current, path[i]).FirstOrDefault();
            if (next is null)
            {
                // Wrap the statement in the missing blocks, innermost first.
                var wrapped = statement;
                for (var j = path.Count - 1; j >= i; j--)
                    wrapped = $"{path[j]} = {{\n\t{wrapped.Replace("\n", "\n\t")}\n}}";
                return [ScriptEdits.InsertMultiLine(text, current, wrapped)];
            }
            current = next;
        }
        return [ScriptEdits.InsertMultiLine(text, current, statement)];
    }

    private static string FocusTree(string id) => $$"""
        focus_tree = {
        	id = {{id}}
        	country = {
        		factor = 0
        	}
        	default = no

        	focus = {
        		id = {{id}}_start
        		icon = GFX_goal_unknown
        		x = 0
        		y = 0
        		cost = 10
        		completion_reward = {
        		}
        	}
        }
        """.Replace("\r\n", "\n");

    private static string Event(string id) => $$"""
        country_event = {
        	id = {{id}}
        	title = {{id}}.t
        	desc = {{id}}.d
        	picture = GFX_report_event_generic_read_write
        	is_triggered_only = yes

        	option = {
        		name = {{id}}.a
        	}
        }
        """.Replace("\r\n", "\n");

    private static string Idea(string id) => $$"""
        {{id}} = {
        	allowed = {
        		always = no
        	}
        	removal_cost = -1
        	modifier = {
        	}
        }
        """.Replace("\r\n", "\n");

    private static string Decision(string id) => $$"""
        {{id}} = {
        	icon = generic_decision
        	cost = 50
        	available = {
        	}
        	complete_effect = {
        	}
        }
        """.Replace("\r\n", "\n");

    private static string Technology(string id) => $$"""
        {{id}} = {
        	research_cost = 1
        	start_year = 1936
        	folder = {
        		name = infantry_folder
        		position = { x = 0 y = 0 }
        	}
        	categories = {
        	}
        }
        """.Replace("\r\n", "\n");

    private static string Character(string id) => $$"""
        {{id}} = {
        	name = {{id}}
        	portraits = {
        		civilian = {
        			large = GFX_portrait_unknown
        		}
        	}
        }
        """.Replace("\r\n", "\n");
}
