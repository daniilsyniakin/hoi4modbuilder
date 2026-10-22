using HoI4ModBuilder.Clausewitz.Classification;

namespace HoI4ModBuilder.Clausewitz.Tests;

public class ClassifierTests
{
    private static List<(string Text, ScriptClassification Kind)> Classify(string line, LineState state = LineState.Default)
    {
        var spans = new List<ClassifiedSpan>();
        ScriptClassifier.ClassifyLine(line, state, spans);
        return spans.Select(s => (line.Substring(s.Start, s.Length), s.Classification)).ToList();
    }

    [Fact]
    public void KeysValuesOperatorsAndComments()
    {
        Assert.Equal(
        [
            ("cost", ScriptClassification.Key),
            ("=", ScriptClassification.Operator),
            ("10", ScriptClassification.Number),
            ("# cheap", ScriptClassification.Comment),
        ], Classify("\tcost = 10 # cheap"));
    }

    [Fact]
    public void BlockKeyWithoutOperatorIsKey()
    {
        Assert.Equal(("focus", ScriptClassification.Key), Classify("focus {")[0]);
    }

    [Theory]
    [InlineData("has_war = yes", "yes", ScriptClassification.Keyword)]
    [InlineData("AND = { }", "AND", ScriptClassification.Keyword)]
    [InlineData("limit = { }", "limit", ScriptClassification.Keyword)]
    [InlineData("FROM.FROM = { }", "FROM.FROM", ScriptClassification.Scope)]
    [InlineData("target = ROOT", "ROOT", ScriptClassification.Scope)]
    [InlineData("tag = GER", "GER", ScriptClassification.Tag)]
    [InlineData("D01 = { }", "D01", ScriptClassification.Tag)]
    [InlineData("1936.1.1 = { }", "1936.1.1", ScriptClassification.Number)]
    [InlineData("add = -0.25", "-0.25", ScriptClassification.Number)]
    [InlineData("add = 25%", "25%", ScriptClassification.Number)]
    [InlineData("@cost = 10", "@cost", ScriptClassification.Variable)]
    [InlineData("x = var:my_var", "var:my_var", ScriptClassification.Variable)]
    [InlineData("x = @[ a * 2 ]", "@[ a * 2 ]", ScriptClassification.Variable)]
    [InlineData("icon = GFX_goal", "GFX_goal", ScriptClassification.Value)]
    [InlineData("name = \"Ukraine\"", "\"Ukraine\"", ScriptClassification.String)]
    [InlineData("color = rgb { 1 2 3 }", "rgb", ScriptClassification.Keyword)]
    public void WordClassification(string line, string word, ScriptClassification expected)
    {
        Assert.Contains((word, expected), Classify(line));
    }

    [Fact]
    public void HashInsideStringIsNotAComment()
    {
        Assert.DoesNotContain(Classify("a = \"# not\" # yes"), s => s.Text == "# not\"");
        Assert.Contains(("# yes", ScriptClassification.Comment), Classify("a = \"# not\" # yes"));
    }

    [Fact]
    public void MultiLineStringCarriesState()
    {
        var spans = new List<ClassifiedSpan>();
        Assert.Equal(LineState.InString, ScriptClassifier.ClassifyLine("desc = \"first line", LineState.Default, spans));

        Assert.Equal(LineState.InString, ScriptClassifier.ClassifyLine("middle # not a comment", LineState.InString, spans = []));
        Assert.Equal([new ClassifiedSpan(0, 22, ScriptClassification.String)], spans);

        Assert.Equal(LineState.Default, ScriptClassifier.ClassifyLine("end\" x = 1", LineState.InString, spans = []));
        Assert.Equal(ScriptClassification.String, spans[0].Classification);
        Assert.Equal(4, spans[0].Length);
        Assert.Equal(ScriptClassification.Key, spans[1].Classification);
    }

    [Fact]
    public void MultiLineInlineMathCarriesState()
    {
        var spans = new List<ClassifiedSpan>();
        Assert.Equal(LineState.InInlineMath, ScriptClassifier.ClassifyLine("x = @[ a +", LineState.Default, spans));
        Assert.Equal(LineState.Default, ScriptClassifier.ClassifyLine("b ] y = 1", LineState.InInlineMath, spans = []));
        Assert.Equal(("b ]", ScriptClassification.Value), ("b ] y = 1"[..spans[0].Length], spans[0].Classification));
    }

    [Fact]
    public void LineByLineAgreesWithWholeFileParse()
    {
        const string text = "a = \"multi\r\nline # still string\r\n\r\nend\" b = { c = @[ x +\ny ] } # done\n\"q\" = rgb { 1 2 }\r\n";
        var tree = SyntaxTree.Parse(text);
        var expected = tree.Root.DescendantTokens()
            .Where(t => t.Text.Length > 0)
            .Select(t => (t.Position, t.Text.Length))
            .ToHashSet();

        var actual = new HashSet<(int, int)>();
        var state = LineState.Default;
        var lineStart = 0;
        foreach (var line in text.Split('\n'))
        {
            var content = line.TrimEnd('\r');
            var spans = new List<ClassifiedSpan>();
            state = ScriptClassifier.ClassifyLine(content, state, spans);
            foreach (var span in spans.Where(s => s.Classification != ScriptClassification.Comment))
                actual.Add((lineStart + span.Start, span.Length));
            lineStart += line.Length + 1;
        }

        // Tokens that span lines are split per line; every single-line token must match exactly.
        foreach (var (position, length) in expected)
        {
            if (text.AsSpan(position, length).IndexOfAny('\r', '\n') < 0)
                Assert.Contains((position, length), actual);
        }
    }

    [Fact]
    public void FastStateScanAgreesWithClassifier()
    {
        const string alphabet = "ab @@[[]]\"\"\\##{}=!?<> \t﻿";
        var random = new Random(1941);
        var states = Enum.GetValues<LineState>();
        for (var n = 0; n < 50_000; n++)
        {
            var chars = new char[random.Next(0, 24)];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[random.Next(alphabet.Length)];
            var line = new string(chars);
            var start = states[random.Next(states.Length)];

            var expected = ScriptClassifier.ClassifyLine(line, start, []);
            var actual = ScriptClassifier.ScanLineState(line, start);
            Assert.True(expected == actual, $"'{line}' from {start}: classifier {expected}, scanner {actual}");
        }
    }

    [Fact]
    public void EmptyLineInsideStringStaysInString()
    {
        var spans = new List<ClassifiedSpan>();
        Assert.Equal(LineState.InString, ScriptClassifier.ClassifyLine("", LineState.InString, spans));
        Assert.Empty(spans);
    }
}
