using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace MidiPlayer.App.Controls;

public class KnobControl : RangeBase
{
    private double _startValue;
    private Point _startPoint;
    private bool _isDragging;

    public static readonly StyledProperty<IBrush> KnobBrushProperty =
        AvaloniaProperty.Register<KnobControl, IBrush>(nameof(KnobBrush), new ImmutableSolidColorBrush(Colors.LightGray));

    public static readonly StyledProperty<IBrush> IndicatorBrushProperty =
        AvaloniaProperty.Register<KnobControl, IBrush>(nameof(IndicatorBrush), new ImmutableSolidColorBrush(Colors.DarkBlue));

    public static readonly StyledProperty<IBrush> OutlineBrushProperty =
        AvaloniaProperty.Register<KnobControl, IBrush>(nameof(OutlineBrush), new ImmutableSolidColorBrush(Colors.Black));

    public IBrush KnobBrush
    {
        get => GetValue(KnobBrushProperty);
        set => SetValue(KnobBrushProperty, value);
    }

    public IBrush IndicatorBrush
    {
        get => GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    public IBrush OutlineBrush
    {
        get => GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    static KnobControl()
    {
        AffectsRender<KnobControl>(ValueProperty, MinimumProperty, MaximumProperty, KnobBrushProperty, IndicatorBrushProperty, OutlineBrushProperty);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(this);
            _startValue = Value;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            var currentPoint = e.GetPosition(this);
            var deltaY = _startPoint.Y - currentPoint.Y; // Move up increases value
            var range = Maximum - Minimum;
            if (range <= 0) return;
            
            // 100 pixels represents the full range
            var sensitivity = 100.0;
            var deltaValue = (deltaY / sensitivity) * range;
            
            Value = Math.Clamp(_startValue + deltaValue, Minimum, Maximum);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var radius = Math.Min(bounds.Width, bounds.Height) / 2.0 - 2.0;

        if (radius <= 0) return;

        // Draw the knob body
        var pen = new Pen(OutlineBrush, 1);
        context.DrawGeometry(KnobBrush, pen, new EllipseGeometry(new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2)));

        // Calculate indicator angle
        var range = Maximum - Minimum;
        var normalizedValue = range > 0 ? (Value - Minimum) / range : 0;
        
        // Angle from -135 deg to +135 deg (0 is top)
        var startAngle = -135.0;
        var endAngle = 135.0;
        var angleDeg = startAngle + (endAngle - startAngle) * normalizedValue;
        var angleRad = (angleDeg - 90) * Math.PI / 180.0;

        // Draw the indicator line
        var innerRadius = radius * 0.3;
        var outerRadius = radius * 0.8;

        var startPoint = new Point(center.X + Math.Cos(angleRad) * innerRadius, center.Y + Math.Sin(angleRad) * innerRadius);
        var endPoint = new Point(center.X + Math.Cos(angleRad) * outerRadius, center.Y + Math.Sin(angleRad) * outerRadius);

        var indicatorPen = new Pen(IndicatorBrush, 2, lineCap: PenLineCap.Round);
        context.DrawLine(indicatorPen, startPoint, endPoint);
    }
}
