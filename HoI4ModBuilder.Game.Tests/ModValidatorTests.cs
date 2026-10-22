using System.Diagnostics;
using System.Text;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Completion;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Schema;
using HoI4ModBuilder.Game.Validation;
using Xunit.Abstractions;

namespace HoI4ModBuilder.Game.Tests;

public sealed class ModValidatorTests(ITestOutputHelper output)
{
    private const string Docs = """
        ## add_political_power

        * Supported Scopes: COUNTRY
        * Supported Targets: none

        ```
        add political power
        ex: add_political_power = 100
        ```

        ## add_manpower

        * Supported Scopes: STATE, COUNTRY
        * Supported Targets: none

        ```
        adds manpower
        ```

        ## add_building_construction

        * Supported Scopes: STATE
        * Supported Targets: none

        ```
        builds
        ```

        ## country_event

        * Supported Scopes: COUNTRY
        * Supported Targets: none

        ```
        fires
        ```

        ## every_owned_state

        * Supported Scopes: COUNTRY
        * Supported Targets: none

        ```
        each state
        ```
        """;

    private const string TriggerDocs = """
        ## has_war

        * Supported Scopes: COUNTRY
        * Supported Targets: none

        ```
        at war
        ex: has_war = yes
        ```
        """;

    private const string ModifierDocs = """
        ## stability_factor

        * Number with 2 decimal places
        * **Categories**: country
        """;

    private static ValidationContext Context(Func<string, bool>? hasText = null) => new(
        new ScriptSchema(ScriptDocumentation.ParseEffects(Docs)
            .Concat(ScriptDocumentation.ParseTriggers(TriggerDocs))
            .Concat(ScriptDocumentation.ParseModifiers(ModifierDocs))),
        sprite => sprite.StartsWith("GFX_goal_", StringComparison.Ordinal),
        hasText,
        (_, _) => false,
        (_, _) => false);

    private static List<Problem> Validate(params (string Relative, string Text)[] files) =>
        ModValidator.Validate(files.Select(f => (f.Relative, f.Relative, SyntaxTree.Parse(f.Text))).ToList(), Context());

    [Fact]
    public void CleanModHasNoProblems()
    {
        var problems = Validate(
            ("common/national_focus/a.txt", """
                focus_tree = {
                    id = t
                    focus = { id = A icon = GFX_goal_a x = 1 completion_reward = { add_political_power = 100 country_event = ukr.1 } }
                    focus = { id = B icon = GFX_goal_b x = 2 prerequisite = { focus = A } available = { has_war = yes } }
                }
                """),
            ("events/a.txt", "add_namespace = ukr\ncountry_event = { id = ukr.1 option = { name = ukr.1.a every_owned_state = { add_building_construction = yes } } }"));

        Assert.Empty(problems);
    }

    [Fact]
    public void FindsBrokenFocusReferencesWithTheirPaths()
    {
        var problems = Validate(("common/national_focus/a.txt", """
            focus_tree = {
                id = t
                focus = { id = A x = 1 mutually_exclusive = { focus = B } }
                focus = { id = B x = 2 prerequisite = { focus = A focus = GHOST } relative_position_id = NOPE }
            }
            """));

        Assert.Contains(problems, p => p is { Severity: ProblemSeverity.Error, EntityId: "B", Path: "prerequisite#0/focus#1" } && p.Message.Contains("GHOST"));
        Assert.Contains(problems, p => p is { Severity: ProblemSeverity.Error, EntityId: "B", Path: "relative_position_id#0" });
        Assert.Contains(problems, p => p is { Severity: ProblemSeverity.Warning, EntityId: "A", Path: "mutually_exclusive#0/focus#0" });
    }

    [Fact]
    public void ChecksCommandsAgainstTheDocumentation()
    {
        var problems = Validate(("common/national_focus/a.txt", """
            focus_tree = {
                id = t
                focus = {
                    id = A
                    completion_reward = {
                        has_war = yes
                        add_political_power = yes
                        made_up_effect = 5
                        add_building_construction = yes
                        every_owned_state = { add_building_construction = yes add_manpower = 10 }
                        random_list = { 50 = { add_political_power = 1 } }
                    }
                    available = { add_political_power = 5 }
                }
            }
            """));

        Assert.Contains(problems, p => p.Path == "completion_reward#0/has_war#0" && p.Message.Contains("умова"));
        Assert.Contains(problems, p => p.Path == "completion_reward#0/add_political_power#0" && p.Message.Contains("число"));
        Assert.Contains(problems, p => p is { Severity: ProblemSeverity.Warning, Path: "completion_reward#0/made_up_effect#0" });
        Assert.Contains(problems, p => p.Path == "completion_reward#0/add_building_construction#0" && p.Message.Contains("STATE"));
        Assert.Contains(problems, p => p.Path == "available#0/add_political_power#0" && p.Message.Contains("ефект"));
        // Inside the state iterator and the random_list weight everything is in the right scope.
        Assert.DoesNotContain(problems, p => p.Path.Contains("every_owned_state") || p.Path.Contains("random_list"));
    }

    [Fact]
    public void ChecksModifiersAreKnownNumbers()
    {
        var problems = Validate(("common/ideas/a.txt", "ideas = { country = { I = { modifier = { stability_factor = high fake_modifier = 1 } } } }"));

        Assert.Contains(problems, p => p is { Severity: ProblemSeverity.Error, Path: "modifier#0/stability_factor#0" });
        Assert.Contains(problems, p => p is { Severity: ProblemSeverity.Warning, Path: "modifier#0/fake_modifier#0" });
    }

    [Fact]
    public void FindsMissingEventsOfTheModsOwnNamespace()
    {
        var problems = Validate(("events/a.txt", """
            add_namespace = ukr
            country_event = { id = ukr.1 option = { name = a country_event = ukr.99 country_event = { id = germany.5 } } }
            """));

        Assert.Single(problems, p => p.Message.Contains("ukr.99"));
        Assert.DoesNotContain(problems, p => p.Message.Contains("germany.5"));
    }

    [Fact]
    public void FindsDuplicatesSyntaxErrorsAndOverlaps()
    {
        var problems = Validate(("common/national_focus/a.txt", """
            focus_tree = {
                id = t
                focus = { id = A x = 1 y = 0 }
                focus = { id = A x = 3 y = 0 }
                focus = { id = C x = 1 y = 0 completion_reward = { add_political_power = 1 }
            }
            """));

        Assert.Equal(2, problems.Count(p => p.Message.Contains("визначено 2 рази")));
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Error && p.Kind is not null && p.Message.Length > 0 && p.Path == "" && !p.Message.Contains("визначено"));
        Assert.Contains(problems, p => p.Message.Contains("тій самій клітинці"));
    }

    [Fact]
    public void ReportsMissingTexts()
    {
        var problems = ModValidator.Validate(
            [("common/ideas/a.txt", "common/ideas/a.txt", SyntaxTree.Parse("ideas = { country = { I = { } J = { } } }"))],
            Context(hasText: key => key == "I"));

        Assert.Single(problems, p => p is { EntityId: "J" } && p.Message.Contains("назви"));
    }

    /// <summary>
    /// Vanilla works in the game, so every error the validator finds there is a false alarm.
    /// The vocabulary of the game itself is used, as in the app.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    public void VanillaContentIsMostlyClean()
    {
        if (GameGraphicsCorpusTests.FindGameDirectory() is not { } game)
        {
            output.WriteLine("Hearts of Iron IV not found.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var files = new List<(string, string, SyntaxTree)>();
        var vocabulary = new ScriptVocabulary();
        foreach (var folder in ContentParser.Folders)
        {
            var directory = Path.Combine(game, folder);
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.txt"))
            {
                var tree = SyntaxTree.Parse(Encoding.UTF8.GetString(File.ReadAllBytes(file)));
                files.Add((file, Path.GetRelativePath(game, file), tree));
                vocabulary.AddFile(tree, folder);
            }
        }
        output.WriteLine($"parsed {files.Count} files in {stopwatch.ElapsedMilliseconds} ms");

        stopwatch.Restart();
        var effects = vocabulary.GetKeys(ScriptVocabulary.RootContext("common/scripted_effects"));
        var triggers = vocabulary.GetKeys(ScriptVocabulary.RootContext("common/scripted_triggers"));
        var context = new ValidationContext(ScriptSchema.Load(game), null, null,
            (blockKey, key) => vocabulary.GetKeys(ScriptVocabulary.BlockContext(blockKey))?.ContainsKey(key) == true,
            (key, kind) => (kind == ScriptContext.Effect ? effects : triggers)?.ContainsKey(key) == true);
        var problems = ModValidator.Validate(files, context);
        output.WriteLine($"validated in {stopwatch.ElapsedMilliseconds} ms: {problems.Count(p => p.Severity == ProblemSeverity.Error)} errors, {problems.Count(p => p.Severity == ProblemSeverity.Warning)} warnings");

        foreach (var group in problems.GroupBy(p => (p.Severity, Shape(p.Message))).OrderByDescending(g => g.Count()).Take(25))
            output.WriteLine($"  {group.Count(),5} {group.Key.Severity} {group.Key.Item2}  e.g. {group.First().Message} [{Path.GetFileName(group.First().File)} {group.First().Path}]");

        var errors = problems.Count(p => p.Severity == ProblemSeverity.Error);
        Assert.True(errors < 200, $"{errors} errors on vanilla");
    }

    /// <summary>The message with quoted names blanked, to group similar problems.</summary>
    private static string Shape(string message) => System.Text.RegularExpressions.Regex.Replace(message, "«[^»]*»", "«…»");
}
