using Windows.Foundation;

namespace Errandesk.Client.Views;

/// <summary>
/// Arranges its visible children from left to right and continues on a new line when the next child does not fit the available width.
/// Children of one line share the height of the tallest child on that line.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnSpacingChanged));

    /// <summary>
    /// The space between two children on the same line.
    /// </summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>
    /// The space between two lines.
    /// </summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var childConstraint = new Size(availableSize.Width, double.PositiveInfinity);
        foreach (var child in Children) child.Measure(childConstraint);
        return ArrangeLines(availableSize.Width, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ArrangeLines(finalSize.Width, true);
        return finalSize;
    }

    private static void OnSpacingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) => ((WrapPanel)dependencyObject).InvalidateMeasure();

    /// <summary>
    /// Places the visible children into lines no wider than <paramref name="availableWidth"/> and returns the size they need.
    /// When <paramref name="isArranging"/> is <see langword="true"/>, the children are also arranged at their places.
    /// </summary>
    private Size ArrangeLines(double availableWidth, bool isArranging)
    {
        List<UIElement> lineChildren = [];
        var lineWidth = 0d;
        var lineHeight = 0d;
        var lineTop = 0d;
        var totalWidth = 0d;

        void CompleteLine()
        {
            if (lineChildren.Count == 0) return;

            if (isArranging)
            {
                var left = 0d;
                foreach (var lineChild in lineChildren)
                {
                    lineChild.Arrange(new Rect(left, lineTop, lineChild.DesiredSize.Width, lineHeight));
                    left += lineChild.DesiredSize.Width + HorizontalSpacing;
                }
            }

            totalWidth = Math.Max(totalWidth, lineWidth);
            lineTop += lineHeight + VerticalSpacing;
            lineChildren.Clear();
            lineWidth = 0;
            lineHeight = 0;
        }

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                if (isArranging) child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var childSize = child.DesiredSize;
            var requiredWidth = lineChildren.Count == 0 ? childSize.Width : lineWidth + HorizontalSpacing + childSize.Width;
            if (lineChildren.Count > 0 && requiredWidth > availableWidth)
            {
                CompleteLine();
                requiredWidth = childSize.Width;
            }

            lineChildren.Add(child);
            lineWidth = requiredWidth;
            lineHeight = Math.Max(lineHeight, childSize.Height);
        }

        var hasLines = lineTop > 0 || lineChildren.Count > 0;
        CompleteLine();
        var totalHeight = hasLines ? lineTop - VerticalSpacing : 0;
        return new Size(totalWidth, Math.Max(0, totalHeight));
    }
}
