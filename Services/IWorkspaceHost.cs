using HoI4ModBuilder.Models;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Services;

/// <summary>Switches the application between the welcome screen and a mod workspace.</summary>
public interface IWorkspaceHost
{
    /// <summary>Opens the mod in the editor window, replacing whatever is currently shown.</summary>
    void OpenMod(ModProject mod);

    void ShowWelcome(WelcomePage page = WelcomePage.Mods);

    void Exit();
}
