using Avalonia.Controls;

namespace HoI4ModBuilder.Views.Dialogs;

/// <summary>Yes/no question. For destructive actions the safe choice (Cancel) is the default.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => InitializeComponent();

    public ConfirmDialog(string title, string message, string confirmText, bool destructive)
        : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        WarningIcon.IsVisible = destructive;
        if (destructive)
            ConfirmButton.Classes.Add("danger");

        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
        Opened += (_, _) => CancelButton.Focus();
    }
}
