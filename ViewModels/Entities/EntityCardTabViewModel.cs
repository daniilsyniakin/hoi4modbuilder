using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.ViewModels.Entities.Inspector;

namespace HoI4ModBuilder.ViewModels.Entities;

/// <summary>
/// A card for entities that have no natural graph — ideas, decisions, characters, technologies,
/// countries, states, scripted effects: picture, name and description as the game shows them,
/// and every property as an editable field.
/// </summary>
public sealed class EntityCardTabViewModel : EntityTabViewModel
{
    public EntityCardTabViewModel(EntityContext context, ContentKind kind, string id, string filePath, Action<DocumentTabViewModel> close)
        : base(context, kind, id, filePath, close)
    {
    }

    protected override string KindTitle => Title(EntityContentKind);

    public static string Title(ContentKind kind) => kind switch
    {
        ContentKind.FocusTree => "Дерево фокусів",
        ContentKind.Focus => "Фокус",
        ContentKind.Event => "Подія",
        ContentKind.Idea => "Ідея",
        ContentKind.Decision => "Рішення",
        ContentKind.Technology => "Технологія",
        ContentKind.Character => "Персонаж",
        ContentKind.Country => "Країна",
        ContentKind.State => "Область",
        ContentKind.ScriptedEffect => "Скриптовий ефект",
        ContentKind.ScriptedTrigger => "Скриптовий тригер",
        _ => kind.ToString(),
    };

    protected override void Refresh()
    {
        var locate = EntityLocator(FilePath, EntityContentKind, EntityId);
        var read = Read(FilePath);
        var property = read is null
            ? null
            : ContentParser.Locate(read.Value.Tree, Context.Workspace.Relative(FilePath), EntityContentKind, EntityId);
        if (read is not { } source || property is null)
        {
            Error = $"{KindTitle} «{EntityId}» не знайдено у {Context.Workspace.Relative(FilePath)}.";
            ShowPanel(null);
            return;
        }
        Error = null;

        var subtitle = $"{KindTitle} · {EntityId}";
        if (property.Value is not BlockSyntax block)
        {
            ShowPanel(CountryTagPanel(property, subtitle));
            return;
        }

        var (title, description, picture) = Describe(block);
        var (pictureKey, prefix) = EntityContentKind switch
        {
            ContentKind.Idea => ("picture", "GFX_idea_"),
            ContentKind.Decision => ("icon", "GFX_decision_"),
            ContentKind.Focus => ("icon", "GFX_goal_"),
            _ => (null, null),
        };
        PictureEditor? pictureEditor = null;
        if (pictureKey is not null)
        {
            // Ideas and decisions name their picture without the prefix; focuses use the full sprite name.
            var stripPrefix = EntityContentKind != ContentKind.Focus;
            pictureEditor = SpritePicker([prefix!], sprite =>
            {
                using var action = Action("Зміна зображення");
                SetProperty(FilePath, locate, pictureKey, stripPrefix ? sprite[prefix!.Length..] : sprite);
            });
        }

        ShowPanel(BuildPanel(FilePath, source.Text, block, locate, Key, title, subtitle, property.Span.Start,
            EntitySchemas.For(EntityContentKind), ContextKey(),
            description, picture, pictureEditor,
            skip: pictureKey is null ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pictureKey },
            titleKey: TitleKey(block),
            descriptionKey: EntityContentKind is ContentKind.Idea or ContentKind.Decision or ContentKind.Technology or ContentKind.Focus
                ? EntityId + "_desc"
                : null,
            entity: (EntityContentKind, EntityId),
            rootContext: EventGraphTabViewModel.RootContext(EntityContentKind)));
    }

    /// <summary>Where the name is localised: under the id, or under the key that <c>name</c> points at.</summary>
    private string? TitleKey(BlockSyntax block) => EntityContentKind switch
    {
        ContentKind.Character or ContentKind.State => ScriptRead.Scalar(block, "name"),
        ContentKind.ScriptedEffect or ContentKind.ScriptedTrigger => null,
        _ => EntityId,
    };

    /// <summary>The definition key, where it is a fixed one, for key suggestions.</summary>
    private string? ContextKey() => EntityContentKind switch
    {
        ContentKind.Focus => "focus",
        ContentKind.State => "state",
        _ => null,
    };

    /// <summary>Country tags are "TAG = countries/File.txt": one value, edited in place, with the flag as picture.</summary>
    private PropertyPanelViewModel CountryTagPanel(PropertySyntax property, string subtitle)
    {
        var panel = new PropertyPanelViewModel(Key, Context.Localisation.Text(EntityId) ?? EntityId, subtitle,
            () => Context.OpenInCode(FilePath, property.Span.Start))
        {
            PictureName = $"gfx/flags/{EntityId}.tga",
        };
        var editor = new TagFileEditor(this);
        var root = new BlockNode("", InspectorPath.Root, -1, false, editor, true, "", (_, _) => { });
        if (property.Value is ScalarSyntax scalar)
        {
            root.Children.Add(new ScalarNode("file", InspectorPath.Root.Append("file", 0), 0, false, editor,
                ScalarKind.Text, scalar.ValueText, []));
        }
        panel.Root = root;
        LoadPicture(panel.PictureName, image => panel.Picture = image);
        return panel;
    }

    private sealed class TagFileEditor(EntityCardTabViewModel tab) : IInspectorEditor
    {
        public void SetValue(InspectorPath property, string value)
        {
            using var action = tab.Action("Зміна файлу країни");
            tab.Edit(tab.FilePath, (_, tree) =>
                ContentParser.Locate(tree, tab.Context.Workspace.Relative(tab.FilePath), tab.EntityContentKind, tab.EntityId) is { } current
                    ? [ScriptEdits.ReplaceValue(current, value)]
                    : []);
        }

        public void AddProperty(InspectorPath block, string key, string valueText)
        {
        }

        public void Remove(InspectorPath property)
        {
        }

        public void AddItem(InspectorPath block, string value)
        {
        }

        public void RemoveItem(InspectorPath block, int index)
        {
        }
    }

    /// <summary>The name, description and picture the game would show, by the kind's conventions.</summary>
    private (string Title, string? Description, string? Picture) Describe(BlockSyntax block)
    {
        var loc = Context.Localisation;
        var id = EntityId;
        switch (EntityContentKind)
        {
            case ContentKind.Idea:
            {
                var picture = ScriptRead.Scalar(block, "picture");
                var sprite = picture is null ? $"GFX_idea_{id}" : picture.StartsWith("GFX_", StringComparison.OrdinalIgnoreCase) ? picture : $"GFX_idea_{picture}";
                return (loc.Text(id) ?? id, loc.Text(id + "_desc"), sprite);
            }
            case ContentKind.Decision:
            {
                var icon = ScriptRead.ScalarOrNested(block, "icon", "value");
                var sprite = icon is null ? null : icon.StartsWith("GFX_", StringComparison.OrdinalIgnoreCase) ? icon : $"GFX_decision_{icon}";
                return (loc.Text(id) ?? id, loc.Text(id + "_desc"), sprite);
            }
            case ContentKind.Technology:
                return (loc.Text(id) ?? id, loc.Text(id + "_desc"), $"GFX_{id}_medium");
            case ContentKind.Character:
            {
                var name = ScriptRead.Scalar(block, "name");
                // portraits = { civilian = { large = … } army = { large = … } }
                var portrait = ScriptRead.Blocks(block, "portraits")
                    .SelectMany(p => p.Properties().Select(role => role.Value).OfType<BlockSyntax>())
                    .Select(role => ScriptRead.Scalar(role, "large") ?? ScriptRead.Scalar(role, "small"))
                    .FirstOrDefault(p => p is not null);
                return (loc.Text(name) ?? name ?? id, null, portrait);
            }
            case ContentKind.State:
            {
                var name = ScriptRead.Scalar(block, "name");
                return (loc.Text(name) ?? name ?? id, null, null);
            }
            case ContentKind.Focus:
                return (loc.Text(id) ?? id, loc.Text(id + "_desc"), ScriptRead.ScalarOrNested(block, "icon", "value"));
            default:
                return (loc.Text(id) ?? id, loc.Text(id + "_desc"), null);
        }
    }
}
