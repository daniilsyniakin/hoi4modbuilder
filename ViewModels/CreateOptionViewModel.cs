using System.Windows.Input;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.ViewModels;

/// <summary>An entry of the "create" menu.</summary>
public sealed class CreateOptionViewModel(ContentKind kind, string title, ICommand create)
{
    public ContentKind Kind { get; } = kind;

    public string Title { get; } = title;

    public ICommand Command { get; } = create;
}
