using Errandesk.Client.Helpers;

namespace Errandesk.Client.Views;

/// <summary>
/// Shows Markdown text as formatted, selectable text. The content is rebuilt whenever <see cref="Markdown"/> changes.
/// </summary>
public sealed partial class MarkdownView : UserControl
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownView), new PropertyMetadata(string.Empty, OnMarkdownChanged));

    private readonly StackPanel _blocksPanel = new() { Spacing = MarkdownElementBuilder.BlockSpacing };

    public MarkdownView()
    {
        IsTabStop = false;
        Content = _blocksPanel;
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) => ((MarkdownView)dependencyObject).Render();

    /// <summary>
    /// Rebuilds the content. If the elements cannot be built, the Markdown is shown as plain text.
    /// </summary>
    private void Render()
    {
        var markdown = Markdown ?? string.Empty;
        _blocksPanel.Children.Clear();
        try { MarkdownElementBuilder.AppendBlocks(_blocksPanel.Children, markdown); }
        catch (ArgumentException)
        {
            _blocksPanel.Children.Clear();
            _blocksPanel.Children.Add(new TextBlock { Text = markdown, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        }
    }
}
