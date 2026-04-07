using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MidiPlayer.App.Services;

namespace MidiPlayer.App.Controls;

public class SpectrumControl : Control
{
    private const double MinDisplayFrequency = 20.0;
    private const double MaxDisplayFrequency = 20000.0;
    private const double MinGainDb = -15.0;
    private const double MaxGainDb = 15.0;
    private const double TopOverlayReservedHeight = 12.0;
    private const double TopGlyphAreaHeight = 36.0;
    private const double BottomReadoutAreaHeight = 68.0;
    private const double SidePadding = 72.0;
    private const double PlotTopPadding = 12.0;
    private const double PlotBottomPadding = 12.0;
    private const double HandleRadius = 7.0;
    private const double ReadoutHorizontalInset = 12.0;
    private const double ReadoutVerticalInset = 8.0;
    private const double AdjacentBandSpacingRatio = 1.08;
    private const double EdgeCutSpacingRatio = 1.3;

    private static readonly Typeface UiTypeface = new("Segoe UI");
    private static readonly Typeface MonoTypeface = new("Cascadia Mono");
    private static readonly EqBand[] DefaultBands =
    [
        new EqBand("Low Cut", EqBandKind.LowCut, 20.0, 0.0, 0.0, Color.Parse("#7E90AC"), 0),
        new EqBand("Band 1", EqBandKind.Peak, 75.0, 0.0, 0.0, Color.Parse("#E39A31")),
        new EqBand("Band 2", EqBandKind.Peak, 100.0, 0.0, 0.0, Color.Parse("#D8E74C")),
        new EqBand("Band 3", EqBandKind.Peak, 250.0, 0.0, 0.0, Color.Parse("#73DF61")),
        new EqBand("Band 4", EqBandKind.Peak, 1040.0, 0.0, 0.0, Color.Parse("#67E1B1")),
        new EqBand("Band 5", EqBandKind.Peak, 2460.0, 0.0, 0.0, Color.Parse("#75BCF4")),
        new EqBand("Band 6", EqBandKind.Peak, 7500.0, 0.0, 0.0, Color.Parse("#B58AE6")),
        new EqBand("High Cut", EqBandKind.HighCut, 20000.0, 0.0, 0.0, Color.Parse("#7E90AC"), 0)
    ];
    private static readonly double[] AxisLabelFrequencies =
    [
        20.0, 30.0, 40.0, 50.0, 60.0, 80.0, 100.0, 200.0, 300.0, 400.0, 500.0, 800.0,
        1000.0, 2000.0, 3000.0, 4000.0, 6000.0, 8000.0, 10000.0, 20000.0
    ];
    private static readonly int[] CutSlopeOptions = [0, 6, 12, 18, 24, 36, 48];

    public static readonly StyledProperty<BassMidiPlayer?> PlayerProperty =
        AvaloniaProperty.Register<SpectrumControl, BassMidiPlayer?>(nameof(Player));
    public static readonly StyledProperty<bool> IsEqEnabledProperty =
        AvaloniaProperty.Register<SpectrumControl, bool>(nameof(IsEqEnabled), true);

    private readonly DispatcherTimer _timer;
    private readonly float[] _fftBuffer = new float[16384];
    private readonly EqBand[] _bands = CloneBands(DefaultBands);
    private int _selectedBandIndex = 1;
    private int _draggingBandIndex = -1;

    public SpectrumControl()
    {
        ClipToBounds = true;
        Focusable = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => InvalidateVisual());

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => StopDragging();
        PointerWheelChanged += OnPointerWheelChanged;
        DoubleTapped += OnDoubleTapped;
    }

    public BassMidiPlayer? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public bool IsEqEnabled
    {
        get => GetValue(IsEqEnabledProperty);
        set => SetValue(IsEqEnabledProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PlayerProperty)
        {
            if (change.OldValue is BassMidiPlayer oldPlayer)
            {
                oldPlayer.EqStateChanged -= OnPlayerEqStateChanged;
            }

            if (change.NewValue is BassMidiPlayer player)
            {
                player.EqStateChanged += OnPlayerEqStateChanged;
                SyncFromPlayer(player);
            }
        }
        else if (change.Property == IsEqEnabledProperty && Player is not null)
        {
            Player.IsEqEnabled = IsEqEnabled;
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        App.Current.SkinManager.SkinChanged += OnSkinChanged;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        App.Current.SkinManager.SkinChanged -= OnSkinChanged;
        _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var plotRect = GetPlotRect(bounds);
        var readoutRect = GetReadoutRect(bounds);

        RenderBackground(context, bounds, plotRect, readoutRect);
        RenderGrid(context, plotRect);
        RenderSpectrumBars(context, plotRect);
        RenderResponseFill(context, plotRect);
        RenderResponseLine(context, plotRect);
        RenderHandles(context, plotRect);
        RenderGlyphs(context, bounds, plotRect);
        RenderReadouts(context, readoutRect);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEqEnabled)
        {
            return;
        }

        Focus();

        var point = e.GetPosition(this);
        var plotRect = GetPlotRect(Bounds);

        var handleIndex = HitTestHandle(point, plotRect);
        if (handleIndex >= 0)
        {
            _selectedBandIndex = handleIndex;
            _draggingBandIndex = handleIndex;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (TryGetReadoutIndex(point, out var readoutIndex))
        {
            _selectedBandIndex = readoutIndex;
            e.Handled = true;
            InvalidateVisual();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsEqEnabled)
        {
            return;
        }

        if (_draggingBandIndex < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        UpdateDraggedBand(e.GetPosition(this), GetPlotRect(Bounds));
        e.Handled = true;
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingBandIndex < 0)
        {
            return;
        }

        StopDragging();
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!IsEqEnabled)
        {
            return;
        }

        var band = _bands[_selectedBandIndex];
        var delta = Math.Sign(e.Delta.Y);
        if (delta == 0)
        {
            return;
        }

        if (band.Kind == EqBandKind.Peak)
        {
            band.Q = Math.Clamp(band.Q + delta * 0.05, 0.0, 2.50);
        }
        else
        {
            var currentIndex = Array.IndexOf(CutSlopeOptions, band.SlopeDbPerOct);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            currentIndex = Math.Clamp(currentIndex + delta, 0, CutSlopeOptions.Length - 1);
            band.SlopeDbPerOct = CutSlopeOptions[currentIndex];
        }

        ApplyBandToPlayer(_selectedBandIndex);
        e.Handled = true;
        InvalidateVisual();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEqEnabled)
        {
            return;
        }

        _bands[_selectedBandIndex].Reset();
        ApplyBandToPlayer(_selectedBandIndex);
        InvalidateVisual();
        e.Handled = true;
    }

    private void StopDragging()
    {
        if (_draggingBandIndex >= 0)
        {
            _draggingBandIndex = -1;
            InvalidateVisual();
        }
    }

    private void UpdateDraggedBand(Point point, Rect plotRect)
    {
        var band = _bands[_draggingBandIndex];
        band.Frequency = ClampBandFrequency(_draggingBandIndex, XToFrequency(plotRect, point.X));

        if (band.Kind == EqBandKind.Peak)
        {
            var targetGain = Math.Clamp(YToGain(plotRect, point.Y), MinGainDb, MaxGainDb);
            band.GainDb = SolvePeakBandGain(_draggingBandIndex, targetGain);
        }

        ApplyBandToPlayer(_draggingBandIndex);
    }

    private double SolvePeakBandGain(int bandIndex, double targetDisplayedGain)
    {
        var frequency = _bands[bandIndex].Frequency;
        var otherGain = GetResponseGain(frequency, bandIndex, true);
        return Math.Clamp(targetDisplayedGain - otherGain, MinGainDb, MaxGainDb);
    }

    private double ClampBandFrequency(int index, double frequency)
    {
        var min = GetMinimumBandFrequency(index);
        var max = GetMaximumBandFrequency(index);

        if (min > max)
        {
            // If an invalid state is loaded from elsewhere, keep the band stable instead of
            // letting Math.Clamp throw during pointer drag.
            return Math.Clamp(_bands[index].Frequency, MinDisplayFrequency, MaxDisplayFrequency);
        }

        return Math.Clamp(frequency, min, max);
    }

    private double GetMinimumBandFrequency(int index)
    {
        if (index <= 0)
        {
            return MinDisplayFrequency;
        }

        var previousBand = _bands[index - 1];
        return Math.Max(MinDisplayFrequency, previousBand.Frequency * GetBandSpacingRatio(index - 1, index));
    }

    private double GetMaximumBandFrequency(int index)
    {
        if (index >= _bands.Length - 1)
        {
            return MaxDisplayFrequency;
        }

        var nextBand = _bands[index + 1];
        return Math.Min(MaxDisplayFrequency, nextBand.Frequency / GetBandSpacingRatio(index, index + 1));
    }

    private double GetBandSpacingRatio(int leftIndex, int rightIndex)
    {
        var leftKind = _bands[leftIndex].Kind;
        var rightKind = _bands[rightIndex].Kind;
        return leftKind == EqBandKind.LowCut || rightKind == EqBandKind.HighCut
            ? EdgeCutSpacingRatio
            : AdjacentBandSpacingRatio;
    }

    private int HitTestHandle(Point point, Rect plotRect)
    {
        for (var i = 0; i < _bands.Length; i++)
        {
            var handle = GetBandPoint(i, plotRect);
            var dx = point.X - handle.X;
            var dy = point.Y - handle.Y;
            if (dx * dx + dy * dy <= 196.0)
            {
                return i;
            }
        }

        return -1;
    }

    private bool TryGetReadoutIndex(Point point, out int index)
    {
        var readoutRect = GetReadoutRect(Bounds);
        if (!readoutRect.Contains(point))
        {
            index = -1;
            return false;
        }

        var cellWidth = readoutRect.Width / _bands.Length;
        index = Math.Clamp((int)((point.X - readoutRect.X) / cellWidth), 0, _bands.Length - 1);
        return true;
    }

    private void RenderBackground(DrawingContext context, Rect bounds, Rect plotRect, Rect readoutRect)
    {
        var background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(BackgroundTopColor, 0.0),
                new GradientStop(BackgroundBottomColor, 1.0)
            ]
        };

        context.DrawRectangle(background, new Pen(new SolidColorBrush(SpectrumPanelBorderColor), 1), bounds, 8, 8);

        var glowBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.25, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.25, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(70, 73, 137, 208), 0.0),
                new GradientStop(Color.FromArgb(0, 73, 137, 208), 1.0)
            ]
        };

        context.DrawRectangle(glowBrush, null, plotRect.Inflate(18));
        context.DrawRectangle(new SolidColorBrush(SpectrumReadoutOverlayColor), null, readoutRect);

        if (!IsEqEnabled)
        {
            context.DrawRectangle(new SolidColorBrush(SpectrumDisabledOverlayColor), null, bounds, 8, 8);
        }
    }

    private void RenderGrid(DrawingContext context, Rect plotRect)
    {
        for (var gain = -15; gain <= 15; gain += 5)
        {
            var y = GainToY(plotRect, gain);
            var color = gain == 0 ? ZeroLineColor : GridMajorColor;
            var thickness = gain == 0 ? 1.4 : 1.0;
            context.DrawLine(new Pen(new SolidColorBrush(color), thickness), new Point(plotRect.Left, y), new Point(plotRect.Right, y));

            DrawText(
                context,
                Math.Abs(gain).ToString(CultureInfo.InvariantCulture),
                UiTypeface,
                10,
                new SolidColorBrush(SpectrumPanelTextColor),
                new Point(plotRect.Left - 10, y - 8),
                TextAlignment.Right);

            DrawText(
                context,
                Math.Abs(gain).ToString(CultureInfo.InvariantCulture),
                UiTypeface,
                10,
                new SolidColorBrush(SpectrumPanelTextColor),
                new Point(plotRect.Right + 10, y - 8),
                TextAlignment.Left);
        }

        foreach (var freq in AxisLabelFrequencies)
        {
            var x = FrequencyToX(plotRect, freq);
            var isMajor = freq is 20.0 or 100.0 or 1000.0 or 10000.0 or 20000.0;
            var pen = new Pen(new SolidColorBrush(isMajor ? GridMajorColor : GridMinorColor), 1);
            context.DrawLine(pen, new Point(x, plotRect.Top), new Point(x, plotRect.Bottom));

            var label = FormatAxisFrequency(freq);
            var labelY = GainToY(plotRect, 0.0) + 6;
            DrawText(
                context,
                label,
                MonoTypeface,
                10,
                new SolidColorBrush(SpectrumAxisTextColor),
                new Point(x, labelY),
                TextAlignment.Center);
        }
    }

    private void RenderSpectrumBars(DrawingContext context, Rect plotRect)
    {
        var player = Player;
        if (player == null || !player.HasStream)
        {
            return;
        }

        var bytesRead = player.GetFFTData(_fftBuffer);
        if (bytesRead <= 0)
        {
            return;
        }

        var numBins = _fftBuffer.Length;
        var maxFreq = player.SampleRate / 2.0;
        var visualBars = Math.Max(12, (int)(plotRect.Width / 3.0));
        var barWidth = plotRect.Width / visualBars;
        var barBrush = new SolidColorBrush(Color.FromArgb(90, SpectrumColor.R, SpectrumColor.G, SpectrumColor.B));
        var maxBarHeight = plotRect.Height;

        for (var i = 0; i < visualBars; i++)
        {
            var startFreq = Math.Pow(10.0, LogLerp(Math.Log10(MinDisplayFrequency), Math.Log10(maxFreq), i / (double)visualBars));
            var endFreq = Math.Pow(10.0, LogLerp(Math.Log10(MinDisplayFrequency), Math.Log10(maxFreq), (i + 1) / (double)visualBars));

            var binStart = Math.Clamp((int)(startFreq / maxFreq * numBins), 0, numBins - 1);
            var binEnd = Math.Clamp((int)(endFreq / maxFreq * numBins), binStart, numBins - 1);

            double maxAmp = 0.0;
            for (var bin = binStart; bin <= binEnd; bin++)
            {
                var amp = Math.Abs(_fftBuffer[bin]);
                if (amp > maxAmp)
                {
                    maxAmp = amp;
                }
            }

            var db = 20.0 * Math.Log10(maxAmp + 1e-6);
            var height = Math.Clamp((db + 60.0) / 60.0 * maxBarHeight, 0.0, maxBarHeight);
            if (height <= 0.0)
            {
                continue;
            }

            var rect = new Rect(plotRect.Left + i * barWidth, plotRect.Bottom - height, Math.Max(1.0, barWidth - 0.5), height);
            context.DrawRectangle(barBrush, null, rect);
        }
    }

    private void RenderResponseFill(DrawingContext context, Rect plotRect)
    {
        var centerY = GainToY(plotRect, 0.0);
        var fillGeometry = new StreamGeometry();

        using (var geometryContext = fillGeometry.Open())
        {
            var started = false;
            for (var x = 0; x <= (int)plotRect.Width; x++)
            {
                var plotX = plotRect.Left + x;
                var frequency = XToFrequency(plotRect, plotX);
                var y = GainToY(plotRect, GetResponseGain(frequency));
                var point = new Point(plotX, y);

                if (!started)
                {
                    geometryContext.BeginFigure(new Point(plotRect.Left, centerY), true);
                    geometryContext.LineTo(point);
                    started = true;
                }
                else
                {
                    geometryContext.LineTo(point);
                }
            }

            geometryContext.LineTo(new Point(plotRect.Right, centerY));
            geometryContext.EndFigure(true);
        }

        var fillOpacity = IsEqEnabled ? 44 : 14;
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)fillOpacity, ResponseFillColor.R, ResponseFillColor.G, ResponseFillColor.B)), null, fillGeometry);
    }

    private void RenderResponseLine(DrawingContext context, Rect plotRect)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            var firstPoint = new Point(plotRect.Left, GainToY(plotRect, GetResponseGain(MinDisplayFrequency)));
            geometryContext.BeginFigure(firstPoint, false);

            for (var x = 1; x <= (int)plotRect.Width; x++)
            {
                var plotX = plotRect.Left + x;
                var frequency = XToFrequency(plotRect, plotX);
                geometryContext.LineTo(new Point(plotX, GainToY(plotRect, GetResponseGain(frequency))));
            }
        }

        var lineColor = IsEqEnabled
            ? ResponseLineColor
            : Color.FromArgb(150, ResponseLineColor.R, ResponseLineColor.G, ResponseLineColor.B);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(lineColor), 2), geometry);
    }

    private void RenderHandles(DrawingContext context, Rect plotRect)
    {
        for (var i = 0; i < _bands.Length; i++)
        {
            var band = _bands[i];
            var point = GetBandPoint(i, plotRect);
            var radius = i == _selectedBandIndex ? HandleRadius + 1.5 : HandleRadius;
            var fill = IsEqEnabled
                ? new SolidColorBrush(band.Color)
                : new SolidColorBrush(Color.FromArgb(110, band.Color.R, band.Color.G, band.Color.B));
            var outline = new Pen(
                new SolidColorBrush(i == _selectedBandIndex && IsEqEnabled ? SpectrumHandleOutlineActiveColor : SpectrumHandleOutlineInactiveColor),
                i == _selectedBandIndex && IsEqEnabled ? 2.0 : 1.4);
            context.DrawEllipse(fill, outline, point, radius, radius);
        }
    }

    private void RenderGlyphs(DrawingContext context, Rect bounds, Rect plotRect)
    {
        var glyphCenterY = bounds.Top + TopOverlayReservedHeight + TopGlyphAreaHeight / 2.0;
        for (var i = 0; i < _bands.Length; i++)
        {
            var band = _bands[i];
            var x = FrequencyToX(plotRect, band.Frequency);

            if (i == _selectedBandIndex && IsEqEnabled)
            {
                var highlight = new Rect(x - 26.0, glyphCenterY - 14.0, 52.0, 28.0);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(56, band.Color.R, band.Color.G, band.Color.B)), null, highlight, 5, 5);
            }

            var glyph = CreateBandGlyphGeometry(band.Kind, x, glyphCenterY, 18.0, 10.0);
            var glyphColor = IsEqEnabled
                ? band.Color
                : Color.FromArgb(120, SpectrumGlyphInactiveColor.R, SpectrumGlyphInactiveColor.G, SpectrumGlyphInactiveColor.B);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(glyphColor), 2), glyph);
        }
    }

    private void RenderReadouts(DrawingContext context, Rect readoutRect)
    {
        var cellWidth = readoutRect.Width / _bands.Length;

        for (var i = 0; i < _bands.Length; i++)
        {
            var band = _bands[i];
            var cell = new Rect(readoutRect.X + i * cellWidth, readoutRect.Y, cellWidth, readoutRect.Height);
            var centerX = cell.X + cell.Width / 2.0;
            var activeColor = IsEqEnabled
                ? band.Color
                : Color.FromArgb(110, band.Color.R, band.Color.G, band.Color.B);
            var valueBrush = new SolidColorBrush(i == _selectedBandIndex && IsEqEnabled ? band.Color : Color.FromArgb(216, activeColor.R, activeColor.G, activeColor.B));
            var secondaryBrush = new SolidColorBrush(i == _selectedBandIndex ? band.Color : ReadoutMutedColor);

            if (i == _selectedBandIndex && IsEqEnabled)
            {
                var highlight = new Rect(cell.X + 4.0, cell.Y, cell.Width - 8.0, cell.Height + 2.0);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(58, band.Color.R, band.Color.G, band.Color.B)), null, highlight, 6, 6);
            }

            DrawText(context, FormatReadoutFrequency(band.Frequency), MonoTypeface, 12, valueBrush, new Point(centerX, cell.Y + 4.0), TextAlignment.Center);
            DrawText(context, GetBandMiddleReadout(band), MonoTypeface, 11, valueBrush, new Point(centerX, cell.Y + 22.0), TextAlignment.Center);
            DrawText(context, GetBandBottomReadout(band), MonoTypeface, 10, secondaryBrush, new Point(centerX, cell.Y + 38.0), TextAlignment.Center);
        }
    }

    private double GetResponseGain(double frequency, int excludedBandIndex = -1, bool ignoreEnabledState = false)
    {
        if (!ignoreEnabledState && !IsEqEnabled)
        {
            return 0.0;
        }

        double gain = 0.0;

        for (var index = 0; index < _bands.Length; index++)
        {
            if (index == excludedBandIndex)
            {
                continue;
            }

            var band = _bands[index];
            switch (band.Kind)
            {
                case EqBandKind.Peak:
                {
                    var distance = Math.Log(frequency / band.Frequency) / Math.Log(2.0);
                    var width = Math.Max(0.12, 1.35 - band.Q * 0.35);
                    gain += band.GainDb * Math.Exp(-(distance * distance) / (2.0 * width * width));
                    break;
                }
                case EqBandKind.LowCut:
                {
                    if (frequency < band.Frequency)
                    {
                        var octaves = Math.Log(band.Frequency / frequency) / Math.Log(2.0);
                        gain -= Math.Min(24.0, band.SlopeDbPerOct * octaves);
                    }

                    break;
                }
                case EqBandKind.HighCut:
                {
                    if (frequency > band.Frequency)
                    {
                        var octaves = Math.Log(frequency / band.Frequency) / Math.Log(2.0);
                        gain -= Math.Min(24.0, band.SlopeDbPerOct * octaves);
                    }

                    break;
                }
            }
        }

        return Math.Clamp(gain, MinGainDb, MaxGainDb);
    }

    private Point GetBandPoint(int bandIndex, Rect plotRect)
    {
        var band = _bands[bandIndex];
        var displayGain = GetResponseGain(band.Frequency);
        var y = GainToY(plotRect, displayGain);
        return new Point(FrequencyToX(plotRect, band.Frequency), y);
    }

    private Rect GetPlotRect(Rect bounds)
    {
        var x = bounds.Left + SidePadding;
        var y = bounds.Top + TopOverlayReservedHeight + TopGlyphAreaHeight + PlotTopPadding;
        var width = Math.Max(80.0, bounds.Width - SidePadding * 2.0);
        var height = Math.Max(80.0, bounds.Height - TopOverlayReservedHeight - TopGlyphAreaHeight - BottomReadoutAreaHeight - PlotTopPadding - PlotBottomPadding);
        return new Rect(x, y, width, height);
    }

    private Rect GetReadoutRect(Rect bounds)
    {
        return new Rect(
            bounds.Left + ReadoutHorizontalInset,
            bounds.Bottom - BottomReadoutAreaHeight + ReadoutVerticalInset,
            Math.Max(80.0, bounds.Width - ReadoutHorizontalInset * 2.0),
            BottomReadoutAreaHeight - ReadoutVerticalInset * 2.0);
    }

    private double FrequencyToX(Rect plotRect, double frequency)
    {
        var logMin = Math.Log10(MinDisplayFrequency);
        var logMax = Math.Log10(MaxDisplayFrequency);
        var logValue = Math.Log10(Math.Clamp(frequency, MinDisplayFrequency, MaxDisplayFrequency));
        return plotRect.Left + (logValue - logMin) / (logMax - logMin) * plotRect.Width;
    }

    private double XToFrequency(Rect plotRect, double x)
    {
        var ratio = Math.Clamp((x - plotRect.Left) / plotRect.Width, 0.0, 1.0);
        return Math.Pow(10.0, LogLerp(Math.Log10(MinDisplayFrequency), Math.Log10(MaxDisplayFrequency), ratio));
    }

    private static double GainToY(Rect plotRect, double gainDb)
    {
        var ratio = (MaxGainDb - Math.Clamp(gainDb, MinGainDb, MaxGainDb)) / (MaxGainDb - MinGainDb);
        return plotRect.Top + ratio * plotRect.Height;
    }

    private static double YToGain(Rect plotRect, double y)
    {
        var ratio = Math.Clamp((y - plotRect.Top) / plotRect.Height, 0.0, 1.0);
        return MaxGainDb - ratio * (MaxGainDb - MinGainDb);
    }

    private static double LogLerp(double a, double b, double t)
    {
        return a + (b - a) * t;
    }

    private static string FormatAxisFrequency(double frequency)
    {
        if (frequency >= 1000.0)
        {
            var kValue = frequency / 1000.0;
            return Math.Abs(kValue - Math.Round(kValue)) < 0.01
                ? $"{kValue:0}k"
                : $"{kValue:0.#}k";
        }

        return frequency.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FormatReadoutFrequency(double frequency)
    {
        if (frequency >= 1000.0)
        {
            return $"{frequency:0} Hz";
        }

        return $"{frequency:0.0} Hz";
    }

    private static string GetBandMiddleReadout(EqBand band)
    {
        return band.Kind == EqBandKind.Peak
            ? $"{band.GainDb:+0.0;-0.0;0.0} dB"
            : $"{band.SlopeDbPerOct} dB/Oct";
    }

    private static string GetBandBottomReadout(EqBand band)
    {
        return band.Kind == EqBandKind.Peak
            ? band.Q.ToString("0.00", CultureInfo.InvariantCulture)
            : (band.SlopeDbPerOct == 0 ? "OFF" : "--");
    }

    private static StreamGeometry CreateBandGlyphGeometry(EqBandKind kind, double centerX, double centerY, double width, double height)
    {
        var geometry = new StreamGeometry();
        var left = centerX - width;
        var right = centerX + width;

        using (var geometryContext = geometry.Open())
        {
            switch (kind)
            {
                case EqBandKind.LowCut:
                    geometryContext.BeginFigure(new Point(left, centerY + height * 0.45), false);
                    geometryContext.LineTo(new Point(centerX - width * 0.25, centerY + height * 0.45));
                    geometryContext.LineTo(new Point(centerX + width * 0.25, centerY - height * 0.45));
                    geometryContext.LineTo(new Point(right, centerY - height * 0.45));
                    break;
                case EqBandKind.HighCut:
                    geometryContext.BeginFigure(new Point(left, centerY - height * 0.45), false);
                    geometryContext.LineTo(new Point(centerX - width * 0.25, centerY - height * 0.45));
                    geometryContext.LineTo(new Point(centerX + width * 0.25, centerY + height * 0.45));
                    geometryContext.LineTo(new Point(right, centerY + height * 0.45));
                    break;
                default:
                    geometryContext.BeginFigure(new Point(left, centerY), false);
                    geometryContext.LineTo(new Point(centerX - width * 0.35, centerY));
                    geometryContext.LineTo(new Point(centerX - width * 0.1, centerY - height * 0.55));
                    geometryContext.LineTo(new Point(centerX + width * 0.1, centerY - height * 0.55));
                    geometryContext.LineTo(new Point(centerX + width * 0.35, centerY));
                    geometryContext.LineTo(new Point(right, centerY));
                    break;
            }
        }

        return geometry;
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        Typeface typeface,
        double size,
        IBrush brush,
        Point anchor,
        TextAlignment alignment)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush);

        var x = alignment switch
        {
            TextAlignment.Center => anchor.X - formatted.Width / 2.0,
            TextAlignment.Right => anchor.X - formatted.Width,
            _ => anchor.X
        };

        context.DrawText(formatted, new Point(x, anchor.Y));
    }

    private static EqBand[] CloneBands(EqBand[] source)
    {
        var clone = new EqBand[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            clone[i] = source[i].Clone();
        }

        return clone;
    }

    private void OnPlayerEqStateChanged(object? sender, EventArgs e)
    {
        if (sender is BassMidiPlayer player)
        {
            SyncFromPlayer(player);
        }
    }

    private void SyncFromPlayer(BassMidiPlayer player)
    {
        var settings = player.CaptureEqSettings();

        for (var i = 0; i < _bands.Length && i < settings.Bands.Length; i++)
        {
            var source = settings.Bands[i];
            var band = _bands[i];
            band.Frequency = source.Frequency;
            band.GainDb = source.GainDb;
            band.Q = source.Q;
            band.SlopeDbPerOct = source.SlopeDbPerOct;
        }

        if (IsEqEnabled != settings.IsEnabled)
        {
            SetCurrentValue(IsEqEnabledProperty, settings.IsEnabled);
        }

        InvalidateVisual();
    }

    private void ApplyBandToPlayer(int bandIndex)
    {
        if (Player is null || (uint)bandIndex >= _bands.Length)
        {
            return;
        }

        var band = _bands[bandIndex];
        Player.SetEqBand(bandIndex, band.Frequency, band.GainDb, band.Q, band.SlopeDbPerOct);
    }

    private enum EqBandKind
    {
        LowCut,
        Peak,
        HighCut
    }

    private sealed class EqBand
    {
        public EqBand(string label, EqBandKind kind, double frequency, double gainDb, double q, Color color, int slopeDbPerOct = 12)
        {
            Label = label;
            Kind = kind;
            DefaultFrequency = frequency;
            DefaultGainDb = gainDb;
            DefaultQ = q;
            DefaultSlopeDbPerOct = slopeDbPerOct;
            Color = color;
            Reset();
        }

        public string Label { get; }

        public EqBandKind Kind { get; }

        public double Frequency { get; set; }

        public double GainDb { get; set; }

        public double Q { get; set; }

        public int SlopeDbPerOct { get; set; }

        public double DefaultFrequency { get; }

        public double DefaultGainDb { get; }

        public double DefaultQ { get; }

        public int DefaultSlopeDbPerOct { get; }

        public Color Color { get; }

        public EqBand Clone()
        {
            var copy = new EqBand(Label, Kind, DefaultFrequency, DefaultGainDb, DefaultQ, Color, DefaultSlopeDbPerOct)
            {
                Frequency = Frequency,
                GainDb = GainDb,
                Q = Q,
                SlopeDbPerOct = SlopeDbPerOct
            };

            return copy;
        }

        public void Reset()
        {
            Frequency = DefaultFrequency;
            GainDb = DefaultGainDb;
            Q = DefaultQ;
            SlopeDbPerOct = DefaultSlopeDbPerOct;
        }
    }

    private static Color BackgroundTopColor => App.Current.SkinManager.GetColor("Theme.SpectrumBackgroundTopColor", "#13243B");

    private static Color BackgroundBottomColor => App.Current.SkinManager.GetColor("Theme.SpectrumBackgroundBottomColor", "#0A1321");

    private static Color GridMajorColor => App.Current.SkinManager.GetColor("Theme.SpectrumGridMajorColor", "#2A4364");

    private static Color GridMinorColor => App.Current.SkinManager.GetColor("Theme.SpectrumGridMinorColor", "#1B2B44");

    private static Color ZeroLineColor => App.Current.SkinManager.GetColor("Theme.SpectrumZeroLineColor", "#95B9E6");

    private static Color SpectrumColor => App.Current.SkinManager.GetColor("Theme.SpectrumBarsColor", "#77B8FF");

    private static Color ResponseFillColor => App.Current.SkinManager.GetColor("Theme.SpectrumResponseFillColor", "#B7ECFF");

    private static Color ResponseLineColor => App.Current.SkinManager.GetColor("Theme.SpectrumResponseLineColor", "#BFE8FF");

    private static Color ReadoutMutedColor => App.Current.SkinManager.GetColor("Theme.SpectrumReadoutMutedColor", "#61758C");

    private static Color SpectrumPanelBorderColor => App.Current.SkinManager.GetColor("Theme.SpectrumPanelBorderColor", "#23384F");

    private static Color SpectrumPanelTextColor => App.Current.SkinManager.GetColor("Theme.SpectrumPanelTextColor", "#88A6C8");

    private static Color SpectrumAxisTextColor => App.Current.SkinManager.GetColor("Theme.SpectrumAxisTextColor", "#9AB7D8");

    private static Color SpectrumReadoutOverlayColor => App.Current.SkinManager.GetColor("Theme.SpectrumReadoutOverlayColor", "#1C0A1626");

    private static Color SpectrumDisabledOverlayColor => App.Current.SkinManager.GetColor("Theme.SpectrumDisabledOverlayColor", "#6E050A10");

    private static Color SpectrumGlyphInactiveColor => App.Current.SkinManager.GetColor("Theme.SpectrumGlyphInactiveColor", "#7A8A9A");

    private static Color SpectrumHandleOutlineActiveColor => App.Current.SkinManager.GetColor("Theme.ForegroundBrush", "#FFFFFF");

    private static Color SpectrumHandleOutlineInactiveColor => App.Current.SkinManager.GetColor("Theme.SpectrumHandleTextColor", "#D2A6C8");

    private void OnSkinChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }
}
