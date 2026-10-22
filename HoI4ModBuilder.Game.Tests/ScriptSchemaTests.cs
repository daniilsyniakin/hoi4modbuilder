using HoI4ModBuilder.Game.Schema;
using Xunit.Abstractions;

namespace HoI4ModBuilder.Game.Tests;

public sealed class ScriptSchemaTests(ITestOutputHelper output)
{
    private const string Effects = """
        # Effects

        ## Effects for scope COUNTRY

        * [add_political_power](#add_political_power)

        ## add_political_power

        * Supported Scopes: COUNTRY
        * Supported Targets: none

        ```
        add political power to country
        ```

        ## add_popularity

        * Supported Scopes: COUNTRY
        * Supported Targets: none

        ```
        add popularity to an ideology in a country

        Example:
        add_popularity = {
        	ideology = neutrality
        	popularity = 0.05
        }
        ```

        ## country_event

        * Supported Scopes: COUNTRY
        * Supported Targets: none

        ```
        Fires a country event.
        Example:
        country_event = {
        	id = germany.75 # The event to fire.
        	# Optional Fields:
        	days = 5 # The number of days.
        }
        ```

        ## injure_scientist_for_days

        * Supported Scopes: CHARACTER
        * Supported Targets: none

        ```
        Injure a scientist for x amount of days.
        	ex: my_character = {
        	  injure_scientist_for_days = 12
        	}
        ```
        """;

    private const string Modifiers = """
        ## stability_factor

        * Number with 2 decimal places
        * **Categories**: country

        ## army_attack_factor

        * Number with 2 decimal places
        * **Categories**: army
        """;

    private static ScriptSchema Schema() =>
        new(ScriptDocumentation.ParseEffects(Effects).Concat(ScriptDocumentation.ParseModifiers(Modifiers)));

    [Fact]
    public void ParsesCommandsWithScopesAndDescriptions()
    {
        var power = Schema().Find(ScriptContext.Effect, "add_political_power")!;

        Assert.Equal(["COUNTRY"], power.Scopes);
        Assert.Equal("add political power to country", power.Description);
        Assert.Equal(ValueShape.Unknown, power.Shape);
    }

    [Fact]
    public void ReadsBlockFieldsFromExamplesWithOptionalOnes()
    {
        var schema = Schema();
        var popularity = schema.Find(ScriptContext.Effect, "add_popularity")!;
        var countryEvent = schema.Find(ScriptContext.Effect, "country_event")!;

        Assert.Equal(ValueShape.Block, popularity.Shape);
        Assert.Equal("{ ideology = neutrality popularity = 0.05 }", schema.Template(popularity));
        Assert.Equal("Fires a country event.", countryEvent.Description);
        Assert.Equal([("id", false), ("days", true)], countryEvent.Fields.Select(f => (f.Key, f.Optional)));
        Assert.Equal("{ id = germany.75 }", schema.Template(countryEvent));
    }

    [Fact]
    public void FindsNestedExamples()
    {
        var injure = Schema().Find(ScriptContext.Effect, "injure_scientist_for_days")!;

        Assert.Equal((ValueShape.Number, "12"), (injure.Shape, injure.Sample));
        Assert.Equal("Injure a scientist for x amount of days.", injure.Description);
    }

    [Fact]
    public void ParsesModifiersWithCategories()
    {
        var schema = Schema();

        Assert.Equal("Країна", ScriptSchema.Category(schema.Find(ScriptContext.Modifier, "stability_factor")!));
        Assert.Equal("Армія", ScriptSchema.Category(schema.Find(ScriptContext.Modifier, "army_attack_factor")!));
    }

    [Fact]
    public void ContextsFollowTheScript()
    {
        var schema = Schema();

        Assert.Equal(ScriptContext.Effect, ScriptSchema.EntityBlock("completion_reward"));
        Assert.Equal(ScriptContext.Trigger, schema.ChildContext(ScriptContext.Effect, "limit"));
        Assert.Equal(ScriptContext.Effect, schema.ChildContext(ScriptContext.Effect, "if"));
        Assert.Equal(ScriptContext.Effect, schema.ChildContext(ScriptContext.Effect, "every_owned_state"));
        Assert.Equal(ScriptContext.Effect, schema.ChildContext(ScriptContext.Effect, "GER"));
        Assert.Equal(ScriptContext.Plain, schema.ChildContext(ScriptContext.Effect, "add_popularity"));
        Assert.Equal(ScriptContext.Trigger, schema.ChildContext(ScriptContext.Trigger, "OR"));
        Assert.Equal(ScriptContext.Trigger, schema.ChildContext(ScriptContext.Weight, "modifier"));
        Assert.Equal("STATE", ScriptSchema.ScopeOf("every_owned_state"));
        Assert.Equal("COUNTRY", ScriptSchema.ScopeOf("GER"));
    }

    [Fact]
    public void TheGamesDocumentation()
    {
        if (GameGraphicsCorpusTests.FindGameDirectory() is not { } game || !Directory.Exists(Path.Combine(game, "documentation")))
        {
            output.WriteLine("Hearts of Iron IV documentation not found.");
            return;
        }

        var schema = ScriptSchema.Load(game);
        var effects = schema.All(ScriptContext.Effect).ToList();
        var triggers = schema.All(ScriptContext.Trigger).ToList();
        var modifiers = schema.All(ScriptContext.Modifier).ToList();
        output.WriteLine($"effects {effects.Count}, triggers {triggers.Count}, modifiers {modifiers.Count}");
        foreach (var group in effects.GroupBy(c => c.Shape))
            output.WriteLine($"  effect shape {group.Key}: {group.Count()}");
        foreach (var group in effects.GroupBy(ScriptSchema.Category).OrderByDescending(g => g.Count()))
            output.WriteLine($"  effect category {group.Key}: {group.Count()}");
        output.WriteLine("add_popularity template: " + schema.Template(schema.Find(ScriptContext.Effect, "add_popularity")!));

        Assert.True(effects.Count > 400);
        Assert.True(triggers.Count > 400);
        Assert.True(modifiers.Count > 600);
        Assert.NotNull(schema.Find(ScriptContext.Trigger, "has_completed_focus"));
        Assert.Equal(ValueShape.Block, schema.Find(ScriptContext.Effect, "add_popularity")!.Shape);
        Assert.Equal(ValueShape.Number, schema.Find(ScriptContext.Modifier, "stability_factor")!.Shape);
    }
}
