using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Validation;

namespace HoI4ModBuilder.ViewModels;

/// <summary>A line of the problems panel.</summary>
public sealed class ProblemRowViewModel(Problem problem, string relativeFile)
{
    public Problem Problem { get; } = problem;

    public string Message => Problem.Message;

    public bool IsError => Problem.Severity == ProblemSeverity.Error;

    public ContentKind? EntityKind => Problem.Kind;

    public bool HasEntity => Problem.EntityId is not null;

    public string Entity => Problem.EntityId ?? "";

    public string File { get; } = relativeFile;
}
