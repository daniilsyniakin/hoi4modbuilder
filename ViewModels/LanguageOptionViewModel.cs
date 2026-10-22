using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

/// <summary>One entry of the localisation language menu in the status bar.</summary>
public sealed partial class LanguageOptionViewModel : ViewModelBase
{
    public LanguageOptionViewModel(LocalisationLanguage language, bool isSelected, Action<LanguageOptionViewModel> select)
    {
        Language = language;
        IsSelected = isSelected;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public LocalisationLanguage Language { get; }

    public string Title => Language.Code is null ? Language.Title : $"{Language.Short} — {Language.Title}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IRelayCommand SelectCommand { get; }
}
