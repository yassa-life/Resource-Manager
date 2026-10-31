using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Collections.Specialized;
using ResourceManager.Core;

namespace ResourceManager.App;

public sealed class TreemapControl : Control
{
    public static readonly StyledProperty<IEnumerable<DiskEntry>?> ItemsProperty =
        AvaloniaProperty.Register<TreemapControl, IEnumerable<DiskEntry>?>(nameof(Items));

    private readonly List<(Rect Rect, DiskEntry Entry)> _hitAreas = [];
    public IEnumerable<DiskEntry>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public event EventHandler<DiskEntry>? EntrySelected;

    static TreemapControl()
    {
        AffectsRender<TreemapControl>(ItemsProperty, BoundsProperty);
        ItemsProperty.Changed.AddClassHandler<TreemapControl>((control, args) =>
        {
            if (args.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= control.OnItemsChanged;
            if (args.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += control.OnItemsChanged;
            control.InvalidateVisual();
        });
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _hitAreas.Clear();
        var files = Items?.Where(x => !x.IsDirectory && x.LogicalBytes > 0)
            .OrderByDescending(x => x.LogicalBytes).Take(160).ToArray() ?? [];
        if (files.Length == 0)
        {
            var text = new FormattedText("Choose a folder to build its treemap", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Gray);
            context.DrawText(text, new Point(20, 20)); return;
        }
        DrawSlice(context, files, new Rect(0, 0, Bounds.Width, Bounds.Height), true);
    }

    private void DrawSlice(DrawingContext context, IReadOnlyList<DiskEntry> files, Rect area, bool vertical)
    {
        if (files.Count == 0 || area.Width < 1 || area.Height < 1) return;
        if (files.Count == 1)
        {
            var entry = files[0]; var rect = area.Deflate(1);
            context.DrawRectangle(Brush(entry.Extension), new Pen(Brushes.White, 1), rect, 4, 4);
            _hitAreas.Add((rect, entry));
            if (rect.Width > 92 && rect.Height > 34)
            {
                var label = new FormattedText(entry.Name, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White) { MaxTextWidth = rect.Width - 12, MaxTextHeight = rect.Height - 8 };
                context.DrawText(label, rect.TopLeft + new Vector(6, 5));
            }
            return;
        }
        var total = files.Sum(x => Math.Max(1, x.LogicalBytes));
        long firstTotal = 0; var split = 0;
        while (split < files.Count - 1 && firstTotal < total / 2) firstTotal += Math.Max(1, files[split++].LogicalBytes);
        var ratio = Math.Clamp(firstTotal / (double)total, .08, .92);
        var a = vertical ? new Rect(area.X, area.Y, area.Width * ratio, area.Height) : new Rect(area.X, area.Y, area.Width, area.Height * ratio);
        var b = vertical ? new Rect(a.Right, area.Y, area.Width - a.Width, area.Height) : new Rect(area.X, a.Bottom, area.Width, area.Height - a.Height);
        DrawSlice(context, files.Take(split).ToArray(), a, !vertical);
        DrawSlice(context, files.Skip(split).ToArray(), b, !vertical);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        var selected = _hitAreas.LastOrDefault(x => x.Rect.Contains(point)).Entry;
        if (selected is not null) EntrySelected?.Invoke(this, selected);
        base.OnPointerPressed(e);
    }

    private static IBrush Brush(string extension)
    {
        var colors = new[] { "#2563EB", "#0891B2", "#7C3AED", "#059669", "#D97706", "#DB2777", "#475569", "#DC2626" };
        var hash = string.IsNullOrEmpty(extension) ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(extension);
        return new SolidColorBrush(Color.Parse(colors[Math.Abs(hash % colors.Length)]));
    }
}
