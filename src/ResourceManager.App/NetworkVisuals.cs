using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ResourceManager.Core;

namespace ResourceManager.App;

// Bounded, event-driven rendering: no animation timer or continuously running layout simulation.
public sealed class NetworkMapControl : Control
{
    public static readonly StyledProperty<IEnumerable<NetworkFlow>?> ItemsProperty =
        AvaloniaProperty.Register<NetworkMapControl, IEnumerable<NetworkFlow>?>(nameof(Items));
    public IEnumerable<NetworkFlow>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public event EventHandler<NetworkFlow>? FlowSelected;
    private readonly List<(Rect Bounds, NetworkFlow Flow)> _targets = [];
    static NetworkMapControl()
    {
        AffectsRender<NetworkMapControl>(ItemsProperty, BoundsProperty);
        ItemsProperty.Changed.AddClassHandler<NetworkMapControl>((control, args) =>
        {
            if (args.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= control.Changed;
            if (args.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += control.Changed;
            control.InvalidateVisual();
        });
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _targets.Clear();
        var rows = Items?.Take(Math.Clamp((int)(Bounds.Height / 42), 1, 6)).ToArray() ?? [];
        if (rows.Length == 0) { Label(context, "no endpoints in this selection", 8, 16, "#A8ABB4"); return; }
        var w = Bounds.Width;
        if (w < 200) return;
        var nodeWidth = Math.Max(80, (w - 72) / 2);
        for (var i = 0; i < rows.Length; i++)
        {
            var flow = rows[i];
            var y = 6 + i * 42;
            var left = new Rect(4, y, nodeWidth, 34);
            var right = new Rect(w - nodeWidth - 4, y, nodeWidth, 34);
            var stroke = new Pen(Brushes.SlateGray, 1);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#20242B")), stroke, left, 3, 3);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#20242B")), stroke, right, 3, 3);
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#88B9AD")), 1),
                new Point(left.Right, y + 17), new Point(right.Left, y + 17));
            Label(context, Fit($"{flow.ProcessName} · pid {flow.Pid}", nodeWidth), 10, y + 1, "#DAE3DF");
            Label(context, Fit($"{flow.Protocol} · {flow.LocalEndpoint}", nodeWidth), 10, y + 17, "#A8ABB4");
            Label(context, Fit(flow.RemoteEndpoint, nodeWidth), right.X + 6, y + 1, "#DAE3DF");
            Label(context, Fit(flow.State, nodeWidth), right.X + 6, y + 17, "#A8ABB4");
            _targets.Add((new Rect(0, y, w, 34), flow));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        foreach (var target in _targets)
            if (target.Bounds.Contains(e.GetPosition(this))) { FlowSelected?.Invoke(this, target.Flow); break; }
    }
    private static string Fit(string text, double width)
    {
        var count = Math.Max(5, (int)((width - 12) / 6.7));
        return text.Length > count ? text[..(count - 1)] + "…" : text;
    }
    internal static void Label(DrawingContext context, string text, double x, double y, string color)
        => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 11, new SolidColorBrush(Color.Parse(color))), new Point(x, y));
}

public sealed class EndpointHistoryControl : Control
{
    public static readonly StyledProperty<IEnumerable<double>?> ItemsProperty =
        AvaloniaProperty.Register<EndpointHistoryControl, IEnumerable<double>?>(nameof(Items));
    public IEnumerable<double>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    static EndpointHistoryControl()
    {
        AffectsRender<EndpointHistoryControl>(ItemsProperty, BoundsProperty);
        ItemsProperty.Changed.AddClassHandler<EndpointHistoryControl>((control, args) =>
        {
            if (args.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= control.Changed;
            if (args.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += control.Changed;
            control.InvalidateVisual();
        });
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var samples = Items?.TakeLast(60).ToArray() ?? [];
        if (samples.Length < 2) { NetworkMapControl.Label(context, "collecting endpoint history…", 8, 8, "#A8ABB4"); return; }
        var max = Math.Max(1, samples.Max());
        var w = Math.Max(1, Bounds.Width - 16);
        var h = Math.Max(1, Bounds.Height - 36);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#88B9AD")), 1.5);
        for (var i = 1; i < samples.Length; i++)
            context.DrawLine(pen, new Point(8 + w * (i - 1) / (samples.Length - 1), 8 + h * (1 - samples[i - 1] / max)),
                new Point(8 + w * i / (samples.Length - 1), 8 + h * (1 - samples[i] / max)));
        NetworkMapControl.Label(context, $"{samples[^1]:N0} endpoints · peak {max:N0} · last {samples.Length} captures", 8, h + 14, "#A8ABB4");
    }
}
