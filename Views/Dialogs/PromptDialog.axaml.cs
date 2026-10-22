using Avalonia.Controls;

namespace HoI4ModBuilder.Views.Dialogs;

/// <summary>Asks for a single line of text, validating it as the user types.</summary>
public partial class PromptDialog : Window
{
    private readonly Func<string, string?> _validate = _ => null;

    public PromptDialog() => InitializeComponent();

    /// <param name="validate">Returns an error message, or null when the value is acceptable.</param>
    public PromptDialog(string title, string label, string confirmText, string? placeholder, Func<string, string?> validate,
        string? initialValue = null)
        : this()
    {
        _validate = validate;
        Title = title;
        LabelText.Text = label;
        ConfirmButton.Content = confirmText;
        Input.PlaceholderText = placeholder;
        Input.Text = initialValue;

        Input.TextChanged += (_, _) => Validate(showEmptyError: false);
        ConfirmButton.Click += (_, _) =>
        {
            if (Validate(showEmptyError: true))
                Close(Input.Text!.Trim());
        };
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
        Validate(showEmptyError: false);
    }

    private bool Validate(bool showEmptyError)
    {
        var value = Input.Text?.Trim() ?? "";
        var error = _validate(value);
        // Don't scold an empty field before the user has tried to confirm it.
        ErrorText.Text = error;
        ErrorText.IsVisible = error is not null && (value.Length > 0 || showEmptyError);
        ConfirmButton.IsEnabled = error is null;
        return error is null;
    }
}
