namespace HoI4ModBuilder.Game.Content;

public enum SchemaValue
{
    Text,
    Number,
    Bool,
    /// <summary><c>key = { … }</c> with properties inside (effects, triggers, modifiers).</summary>
    Block,
    /// <summary><c>key = { A B C }</c>: bare items.</summary>
    List,
}

/// <param name="References">The kind of entity the value names, for picking it from a list.</param>
public sealed record SchemaProperty(string Key, SchemaValue Type, ContentKind? References = null);

/// <summary>
/// The properties each kind of entity can have, in the order the game's own files usually use.
/// The inspector shows the missing ones too, so modders can see (and add) what's possible.
/// </summary>
public static class EntitySchemas
{
    private static readonly SchemaProperty[] Focus =
    [
        new("id", SchemaValue.Text),
        new("icon", SchemaValue.Text),
        new("x", SchemaValue.Number),
        new("y", SchemaValue.Number),
        new("relative_position_id", SchemaValue.Text, ContentKind.Focus),
        new("cost", SchemaValue.Number),
        new("prerequisite", SchemaValue.Block),
        new("mutually_exclusive", SchemaValue.Block),
        new("allow_branch", SchemaValue.Block),
        new("available", SchemaValue.Block),
        new("bypass", SchemaValue.Block),
        new("cancel_if_invalid", SchemaValue.Bool),
        new("continue_if_invalid", SchemaValue.Bool),
        new("available_if_capitulated", SchemaValue.Bool),
        new("will_lead_to_war_with", SchemaValue.Text, ContentKind.Country),
        new("search_filters", SchemaValue.List),
        new("select_effect", SchemaValue.Block),
        new("completion_reward", SchemaValue.Block),
        new("ai_will_do", SchemaValue.Block),
    ];

    private static readonly SchemaProperty[] Event =
    [
        new("id", SchemaValue.Text),
        new("title", SchemaValue.Text),
        new("desc", SchemaValue.Text),
        new("picture", SchemaValue.Text),
        new("is_triggered_only", SchemaValue.Bool),
        new("fire_only_once", SchemaValue.Bool),
        new("hidden", SchemaValue.Bool),
        new("major", SchemaValue.Bool),
        new("trigger", SchemaValue.Block),
        new("mean_time_to_happen", SchemaValue.Block),
        new("immediate", SchemaValue.Block),
    ];

    private static readonly SchemaProperty[] EventOption =
    [
        new("name", SchemaValue.Text),
        new("trigger", SchemaValue.Block),
        new("ai_chance", SchemaValue.Block),
    ];

    private static readonly SchemaProperty[] Idea =
    [
        new("picture", SchemaValue.Text),
        new("cost", SchemaValue.Number),
        new("removal_cost", SchemaValue.Number),
        new("allowed", SchemaValue.Block),
        new("allowed_civil_war", SchemaValue.Block),
        new("available", SchemaValue.Block),
        new("visible", SchemaValue.Block),
        new("cancel", SchemaValue.Block),
        new("modifier", SchemaValue.Block),
        new("research_bonus", SchemaValue.Block),
        new("equipment_bonus", SchemaValue.Block),
        new("targeted_modifier", SchemaValue.Block),
        new("on_add", SchemaValue.Block),
        new("on_remove", SchemaValue.Block),
        new("traits", SchemaValue.List),
        new("ai_will_do", SchemaValue.Block),
    ];

    private static readonly SchemaProperty[] Decision =
    [
        new("icon", SchemaValue.Text),
        new("cost", SchemaValue.Number),
        new("days_remove", SchemaValue.Number),
        new("days_re_enable", SchemaValue.Number),
        new("fire_only_once", SchemaValue.Bool),
        new("allowed", SchemaValue.Block),
        new("visible", SchemaValue.Block),
        new("available", SchemaValue.Block),
        new("target_trigger", SchemaValue.Block),
        new("complete_effect", SchemaValue.Block),
        new("remove_effect", SchemaValue.Block),
        new("timeout_effect", SchemaValue.Block),
        new("modifier", SchemaValue.Block),
        new("ai_will_do", SchemaValue.Block),
    ];

    private static readonly SchemaProperty[] Technology =
    [
        new("research_cost", SchemaValue.Number),
        new("start_year", SchemaValue.Number),
        new("folder", SchemaValue.Block),
        new("path", SchemaValue.Block),
        new("categories", SchemaValue.List),
        new("enable_equipments", SchemaValue.List),
        new("enable_subunits", SchemaValue.List),
        new("allow", SchemaValue.Block),
        new("ai_will_do", SchemaValue.Block),
    ];

    private static readonly SchemaProperty[] Character =
    [
        new("name", SchemaValue.Text),
        new("gender", SchemaValue.Text),
        new("portraits", SchemaValue.Block),
        new("country_leader", SchemaValue.Block),
        new("advisor", SchemaValue.Block),
        new("corps_commander", SchemaValue.Block),
        new("field_marshal", SchemaValue.Block),
        new("navy_leader", SchemaValue.Block),
    ];

    private static readonly SchemaProperty[] State =
    [
        new("id", SchemaValue.Number),
        new("name", SchemaValue.Text),
        new("manpower", SchemaValue.Number),
        new("state_category", SchemaValue.Text),
        new("resources", SchemaValue.Block),
        new("history", SchemaValue.Block),
        new("provinces", SchemaValue.List),
    ];

    // Value keys that name another entity, wherever they appear.
    private static readonly Dictionary<string, ContentKind> ReferenceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["focus"] = ContentKind.Focus,
        ["relative_position_id"] = ContentKind.Focus,
        ["has_completed_focus"] = ContentKind.Focus,
        ["complete_national_focus"] = ContentKind.Focus,
        ["unlock_national_focus"] = ContentKind.Focus,
        ["focus_progress"] = ContentKind.Focus,
        ["add_ideas"] = ContentKind.Idea,
        ["remove_ideas"] = ContentKind.Idea,
        ["has_idea"] = ContentKind.Idea,
        ["country_event"] = ContentKind.Event,
        ["news_event"] = ContentKind.Event,
        ["state_event"] = ContentKind.Event,
        ["has_country_event"] = ContentKind.Event,
        ["activate_decision"] = ContentKind.Decision,
        ["has_decision"] = ContentKind.Decision,
        ["has_tech"] = ContentKind.Technology,
        ["set_technology"] = ContentKind.Technology,
        ["recruit_character"] = ContentKind.Character,
        ["has_character"] = ContentKind.Character,
        ["promote_character"] = ContentKind.Character,
        ["tag"] = ContentKind.Country,
        ["original_tag"] = ContentKind.Country,
        ["exists"] = ContentKind.Country,
        ["will_lead_to_war_with"] = ContentKind.Country,
    };

    public static IReadOnlyList<SchemaProperty> For(ContentKind kind) => kind switch
    {
        ContentKind.Focus => Focus,
        ContentKind.Event => Event,
        ContentKind.Idea => Idea,
        ContentKind.Decision => Decision,
        ContentKind.Technology => Technology,
        ContentKind.Character => Character,
        ContentKind.State => State,
        _ => [],
    };

    public static IReadOnlyList<SchemaProperty> ForEventOption => EventOption;

    /// <summary>The kind of entity a value of <paramref name="key"/> names, if any.</summary>
    public static ContentKind? ReferenceOf(string key) => ReferenceKeys.TryGetValue(key, out var kind) ? kind : null;
}
