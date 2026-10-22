using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using HoI4ModBuilder.Game.Validation;

namespace HoI4ModBuilder.ViewModels.Entities;

public enum GraphEdgeStyle
{
    /// <summary>A required prerequisite.</summary>
    Solid,
    /// <summary>One of several alternative prerequisites.</summary>
    Dashed,
    /// <summary>Mutually exclusive focuses.</summary>
    Exclusive,
    /// <summary>Cause and effect in the event graph: a smooth curve with an arrow.</summary>
    Flow,
    /// <summary>A focus placed relative to another (<c>relative_position_id</c>): faint, behind everything.</summary>
    Relative,
    /// <summary>The link being drawn to the pointer.</summary>
    Preview,
}

/// <summary>A connection drawn behind the nodes, in world coordinates.</summary>
public sealed record GraphEdge(IReadOnlyList<Point> Points, GraphEdgeStyle Style);

/// <summary>A box on a graph canvas. Positions are world coordinates of its top-left corner.</summary>
public abstract partial class GraphNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    [ObservableProperty]
    public partial double Width { get; set; }

    [ObservableProperty]
    public partial double Height { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The worst problem the mod check found in it, for a badge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(HasWarning), nameof(HasProblems))]
    public partial ProblemSeverity? Severity { get; set; }

    public bool HasError => Severity == ProblemSeverity.Error;

    public bool HasWarning => Severity == ProblemSeverity.Warning;

    public bool HasProblems => Severity is not null;

    /// <summary>The problems' messages, one per line.</summary>
    [ObservableProperty]
    public partial string? ProblemText { get; set; }

    /// <summary>Takes over the problems of an entity (from the mod check).</summary>
    public void ShowProblems(IReadOnlyList<Problem> problems)
    {
        Severity = problems.Count == 0 ? null : problems.Max(p => p.Severity);
        ProblemText = problems.Count == 0 ? null : string.Join("\n", problems.Select(p => p.Message).Distinct());
    }
}
