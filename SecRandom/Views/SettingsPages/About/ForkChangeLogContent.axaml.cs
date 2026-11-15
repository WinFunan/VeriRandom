using Avalonia.Controls;

namespace SecRandom.Views.SettingsPages.About;

/// <summary>
///     Renders the external <c>DownStream_Change.md</c> that ships beside the executable. It takes the text in
///     the constructor so the Markdown binding is resolved during the XAML load instead of needing a change
///     notification after the control is already attached.
/// </summary>
public partial class ForkChangeLogContent : UserControl
{
    public ForkChangeLogContent(string markdown)
    {
        Markdown = markdown;
        DataContext = this;
        InitializeComponent();
    }

    public string Markdown { get; }
}
