using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ResourceManager.Core;

namespace ResourceManager.App;

public sealed class HistoryChartControl : Control
{
    public static readonly StyledProperty<IEnumerable<ResourceSample>?> ItemsProperty =
        AvaloniaProperty.Register<HistoryChartControl, IEnumerable<ResourceSample>?>(nameof(Items));

    public IEnumerable<ResourceSample>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    static HistoryChartControl()
    {
        AffectsRender<HistoryChartControl>(ItemsProperty, BoundsProperty);
        ItemsProperty.Changed.AddClassHandler<HistoryChartControl>((control, args) =>
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
        var samples = Items?.TakeLast(120).ToArray() ?? [];
        var width = Math.Max(0, Bounds.Width - 24);
        var height = Math.Max(0, Bounds.Height - 24);
        if (width < 16 || height < 16) return;

        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#303038")), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = 12 + height * i / 4;
            context.DrawLine(gridPen, new Point(12, y), new Point(12 + width, y));
        }

        if (samples.Length < 2) return;
        DrawSeries(context, samples, sample => sample.CpuPercent, "#88B9AD", width, height);
        DrawSeries(context, samples, sample => sample.MemoryPercent, "#7FA9BD", width, height);
    }

    private static void DrawSeries(DrawingContext context, ResourceSample[] samples,
        Func<ResourceSample, double> value, string color, double width, double height)
    {
        var pen = new Pen(new SolidColorBrush(Color.Parse(color)), 2);
        for (var i = 1; i < samples.Length; i++)
        {
            var a = new Point(12 + width * (i - 1) / (samples.Length - 1), 12 + height * (1 - Math.Clamp(value(samples[i - 1]), 0, 100) / 100));
            var b = new Point(12 + width * i / (samples.Length - 1), 12 + height * (1 - Math.Clamp(value(samples[i]), 0, 100) / 100));
            context.DrawLine(pen, a, b);
        }
    }
}
