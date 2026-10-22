namespace HoI4ModBuilder.Clausewitz;

public enum DiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>A problem found while parsing. Messages are user-facing, hence Ukrainian.</summary>
public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, TextSpan Span, string Message)
{
    public override string ToString() => $"{Code} {Span}: {Message}";
}

/// <summary>Catalogue of parser diagnostics, so codes stay stable and in one place.</summary>
public static class DiagnosticCodes
{
    public const string UnterminatedString = "CW1001";
    public const string UnexpectedCloseBrace = "CW1002";
    public const string OperatorWithoutKey = "CW1003";
    public const string MissingValue = "CW1004";
    public const string UnclosedBlock = "CW1005";
    public const string UnterminatedInlineMath = "CW1006";
}

internal sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Items => _diagnostics;

    public void UnterminatedString(TextSpan span) =>
        Error(DiagnosticCodes.UnterminatedString, span, "Рядок не закрито лапками.");

    public void UnexpectedCloseBrace(TextSpan span) =>
        Error(DiagnosticCodes.UnexpectedCloseBrace, span, "Зайва закривна дужка «}» — для неї немає відкривної.");

    public void OperatorWithoutKey(TextSpan span, string text) =>
        Error(DiagnosticCodes.OperatorWithoutKey, span, $"Оператор «{text}» без ключа ліворуч.");

    public void MissingValue(TextSpan span, string operatorText) =>
        Error(DiagnosticCodes.MissingValue, span, $"Після «{operatorText}» очікувалося значення.");

    public void UnclosedBlock(TextSpan span) =>
        Error(DiagnosticCodes.UnclosedBlock, span, "Блок «{» не закрито — бракує «}».");

    public void UnterminatedInlineMath(TextSpan span) =>
        Error(DiagnosticCodes.UnterminatedInlineMath, span, "Вираз «@[» не закрито — бракує «]».");

    private void Error(string code, TextSpan span, string message) =>
        _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, span, message));
}
