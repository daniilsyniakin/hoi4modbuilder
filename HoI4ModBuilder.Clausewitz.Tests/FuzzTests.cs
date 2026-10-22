namespace HoI4ModBuilder.Clausewitz.Tests;

/// <summary>Random and mutated inputs: the parser must never throw and must always round-trip.</summary>
public class FuzzTests
{
    // Heavy on characters that matter to the lexer.
    private const string Alphabet = "ab_1.-  \t\t\r\n\n{{}}==<>!?\"\"##\\@[]é\uFEFF";

    private const string Sample = """
        # Sample focus tree
        focus_tree = {
        	id = ukraine_focus
        	country = { factor = 0 modifier = { add = 10 tag = UKR } }
        	focus = {
        		id = UKR_industry
        		icon = GFX_goal_generic_production
        		x = 3 y = 0
        		cost = 10
        		available = { has_war = no NOT = { has_country_flag = "flag # 1" } }
        		completion_reward = { add_ideas = UKR_idea color = rgb { 1 2 3 } }
        		ai_will_do = { factor = @[ base * 2 ] }
        	}
        }
        """;

    [Fact]
    public void RandomText()
    {
        var random = new Random(1936);
        for (var i = 0; i < 20_000; i++)
        {
            var length = random.Next(0, 120);
            var chars = new char[length];
            for (var j = 0; j < length; j++)
                chars[j] = Alphabet[random.Next(Alphabet.Length)];
            AssertLossless(new string(chars));
        }
    }

    [Fact]
    public void MutatedScript()
    {
        var random = new Random(1939);
        for (var i = 0; i < 5_000; i++)
        {
            var text = Sample;
            var edits = random.Next(1, 6);
            for (var e = 0; e < edits; e++)
            {
                var position = random.Next(text.Length + 1);
                text = random.Next(3) switch
                {
                    0 when text.Length > 0 && position < text.Length => text.Remove(position, 1),
                    1 => text.Insert(position, Alphabet[random.Next(Alphabet.Length)].ToString()),
                    _ => text.Insert(position, Sample.Substring(random.Next(Sample.Length - 10), 10)),
                };
            }
            AssertLossless(text);
        }
    }

    [Fact]
    public void DeepNesting()
    {
        var text = string.Concat(Enumerable.Repeat("a = { ", 500)) + string.Concat(Enumerable.Repeat("} ", 500));
        AssertLossless(text);
        AssertLossless(string.Concat(Enumerable.Repeat("{", 500)));
    }

    private static void AssertLossless(string text)
    {
        var tree = SyntaxTree.Parse(text);
        if (TreeAssert.FindViolation(tree) is { } violation)
            Assert.Fail($"{violation}\nInput: {Escape(text)}");
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}
