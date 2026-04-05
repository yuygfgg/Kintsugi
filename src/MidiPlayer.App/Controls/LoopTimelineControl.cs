using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MidiPlayer.App.Controls;

public sealed class LoopTimelineControl : Control
{
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LoopTimelineControl, double>(nameof(Maximum));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LoopTimelineControl, double>(nameof(Value));

    public static readonly StyledProperty<double> LoopStartProperty =
        AvaloniaProperty.Register<LoopTimelineControl, double>(nameof(LoopStart));

    public static readonly StyledProperty<double> LoopEndProperty =
        AvaloniaProperty.Register<LoopTimelineControl, double>(nameof(LoopEnd));

    public static readonly StyledProperty<bool> HasCustomLoopRangeProperty =
        AvaloniaProperty.Register<LoopTimelineControl, bool>(nameof(HasCustomLoopRange));

    public static readonly StyledProperty<bool> IsLoopEnabledProperty =
        AvaloniaProperty.Register<LoopTimelineControl, bool>(nameof(IsLoopEnabled));

    private static readonly IBrush LoopLaneFill = new SolidColorBrush(Color.Parse("#1C1C1C"));
    private static readonly IBrush SeekLaneFill = new SolidColorBrush(Color.Parse("#202020"));
    private static readonly IBrush SelectionFill = new SolidColorBrush(Color.Parse("#24496F"));
    private static readonly IBrush SelectionStandbyFill = new SolidColorBrush(Color.Parse("#1B3248"));
    private static readonly IBrush FullLoopFill = new SolidColorBrush(Color.Parse("#162B3E"));
    private static readonly IBrush RangeTintFill = new SolidColorBrush(Color.Parse("#14283B"));
    private static readonly IBrush ProgressFill = new SolidColorBrush(Color.Parse("#4A90E2"));
    private static readonly IBrush HandleFill = new SolidColorBrush(Color.Parse("#DCEEFF"));
    private static readonly IBrush PlayheadFill = new SolidColorBrush(Color.Parse("#DCEEFF"));
    private static readonly Pen LaneBorderPen = new(new SolidColorBrush(Color.Parse("#303030")), 1);
    private static readonly Pen SelectionBorderPen = new(new SolidColorBrush(Color.Parse("#4A90E2")), 1);
    private static readonly Pen SelectionStandbyBorderPen = new(new SolidColorBrush(Color.Parse("#3B5A78")), 1);
    private static readonly Pen PlayheadPen = new(new SolidColorBrush(Color.Parse("#DCEEFF")), 1.5);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor RangeCursor = new(StandardCursorType.Cross);

    private const double HorizontalPadding = 4;
    private const double TrackTop = 10;
    private const double TrackHeight = 16;
    private const double LaneCornerRadius = 999;
    private const double SelectionCornerRadius = 4;
    private const double SelectionVerticalInset = 2;
    private const double TrackCornerRadius = 3;
    private const double TrackVerticalInset = 1;
    private const double SeekRangeCornerRadius = 3;
    private const double SeekRangeVerticalInset = 1;
    private const double ProgressCornerRadius = 3;
    private const double HandleWidth = 6;
    private const double HandleHeight = 16;
    private const double HandleHotZone = 8;
    private const double PlayheadRadius = 4;
    private const double PlayheadBottomPadding = 1;
    private const double MinimumLoopSpanPixels = 18;

    private DragMode _dragMode;
    private IPointer? _capturedPointer;
    private double _dragAnchorValue;
    private double _dragInitialLoopStart;
    private double _dragInitialLoopEnd;
    private bool _dragMoved;
    private bool _usePreviewValue;
    private bool _usePreviewLoopRange;
    private double _previewValue;
    private double _previewLoopStart;
    private double _previewLoopEnd;
    private bool _previewHasCustomLoopRange;

    static LoopTimelineControl()
    {
        AffectsRender<LoopTimelineControl>(
            MaximumProperty,
            ValueProperty,
            LoopStartProperty,
            LoopEndProperty,
            HasCustomLoopRangeProperty,
            IsLoopEnabledProperty,
            IsEnabledProperty,
            BoundsProperty);
    }

    public LoopTimelineControl()
    {
        Cursor = HandCursor;
        Focusable = false;
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double LoopStart
    {
        get => GetValue(LoopStartProperty);
        set => SetValue(LoopStartProperty, value);
    }

    public double LoopEnd
    {
        get => GetValue(LoopEndProperty);
        set => SetValue(LoopEndProperty, value);
    }

    public bool HasCustomLoopRange
    {
        get => GetValue(HasCustomLoopRangeProperty);
        set => SetValue(HasCustomLoopRangeProperty, value);
    }

    public bool IsLoopEnabled
    {
        get => GetValue(IsLoopEnabledProperty);
        set => SetValue(IsLoopEnabledProperty, value);
    }

    public event EventHandler? SeekStarted;

    public event EventHandler<TimelineSeekChangedEventArgs>? SeekChanged;

    public event EventHandler<TimelineSeekChangedEventArgs>? SeekCompleted;

    public event EventHandler<TimelineLoopRangeChangedEventArgs>? LoopRangeChanged;

    public event EventHandler? LoopRangeCleared;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var trackRect = GetTrackRect();
        if (trackRect.Width <= 0)
        {
            return;
        }

        DrawRoundedLane(context, trackRect, SeekLaneFill, LaneBorderPen);

        bool hasCustomRange = GetDisplayedHasCustomLoopRange();
        if (hasCustomRange)
        {
            var rangeRect = GetSelectionRect(trackRect, SelectionVerticalInset);
            context.DrawRectangle(
                GetSelectionFill(),
                GetSelectionBorderPen(),
                rangeRect,
                SelectionCornerRadius,
                SelectionCornerRadius);
            DrawHandles(context, rangeRect);
        }
        else if (IsLoopEnabled)
        {
            context.DrawRectangle(FullLoopFill, null, trackRect, TrackCornerRadius, TrackCornerRadius);
        }

        var progressRect = GetProgressRect(trackRect);
        if (progressRect.Width > 0)
        {
            context.DrawRectangle(ProgressFill, null, progressRect, ProgressCornerRadius, ProgressCornerRadius);
        }

        DrawPlayhead(context, trackRect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsEnabled || GetMaximumValue() <= 0)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = point.Position;
        var hitTarget = GetHitTarget(position, e.KeyModifiers);

        if (e.ClickCount >= 2 && hitTarget is HitTarget.LoopRange or HitTarget.LoopStartHandle or HitTarget.LoopEndHandle)
        {
            _usePreviewLoopRange = false;
            LoopRangeCleared?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        switch (hitTarget)
        {
            case HitTarget.SeekLane:
                BeginSeekDrag(e.Pointer, position);
                e.Handled = true;
                return;
            case HitTarget.LoopStartHandle:
                BeginLoopDrag(e.Pointer, DragMode.ResizeLoopStart, position);
                e.Handled = true;
                return;
            case HitTarget.LoopEndHandle:
                BeginLoopDrag(e.Pointer, DragMode.ResizeLoopEnd, position);
                e.Handled = true;
                return;
            case HitTarget.LoopRange:
                BeginLoopDrag(e.Pointer, DragMode.MoveLoopRange, position);
                e.Handled = true;
                return;
            case HitTarget.LoopLane:
                BeginLoopDrag(e.Pointer, DragMode.CreateLoopRange, position);
                e.Handled = true;
                return;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_capturedPointer is not null && ReferenceEquals(e.Pointer, _capturedPointer))
        {
            var position = e.GetPosition(this);
            switch (_dragMode)
            {
                case DragMode.Seek:
                    _dragMoved = true;
                    UpdatePreviewValue(position);
                    RaiseSeekChanged(CurrentValue);
                    e.Handled = true;
                    return;
                case DragMode.CreateLoopRange:
                case DragMode.MoveLoopRange:
                case DragMode.ResizeLoopStart:
                case DragMode.ResizeLoopEnd:
                    _dragMoved = true;
                    UpdatePreviewLoopRange(position);
                    RaiseLoopRangeChanged(isFinal: false);
                    e.Handled = true;
                    return;
            }
        }

        UpdateCursor(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_capturedPointer is null || !ReferenceEquals(e.Pointer, _capturedPointer))
        {
            return;
        }

        var position = e.GetPosition(this);
        switch (_dragMode)
        {
            case DragMode.Seek:
                UpdatePreviewValue(position);
                RaiseSeekCompleted(CurrentValue);
                _usePreviewValue = false;
                break;
            case DragMode.CreateLoopRange:
                if (_dragMoved)
                {
                    UpdatePreviewLoopRange(position);
                    RaiseLoopRangeChanged(isFinal: true);
                }
                else
                {
                    _usePreviewLoopRange = false;
                }
                break;
            case DragMode.MoveLoopRange:
            case DragMode.ResizeLoopStart:
            case DragMode.ResizeLoopEnd:
                UpdatePreviewLoopRange(position);
                RaiseLoopRangeChanged(isFinal: true);
                break;
        }

        ReleasePointer();
        UpdateCursor(e);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _usePreviewValue = false;
        _usePreviewLoopRange = false;
        _capturedPointer = null;
        _dragMode = DragMode.None;
        _dragMoved = false;
        Cursor = HandCursor;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_capturedPointer is null)
        {
            Cursor = HandCursor;
        }
    }

    private void BeginSeekDrag(IPointer pointer, Point position)
    {
        CapturePointer(pointer);
        _dragMode = DragMode.Seek;
        _dragMoved = false;
        _usePreviewValue = true;
        UpdatePreviewValue(position);
        SeekStarted?.Invoke(this, EventArgs.Empty);
        RaiseSeekChanged(CurrentValue);
    }

    private void BeginLoopDrag(IPointer pointer, DragMode mode, Point position)
    {
        CapturePointer(pointer);
        _dragMode = mode;
        _dragMoved = false;
        _dragAnchorValue = PositionToValue(position.X);
        _dragInitialLoopStart = DisplayedLoopStart;
        _dragInitialLoopEnd = DisplayedLoopEnd;
        _usePreviewLoopRange = true;

        if (mode == DragMode.CreateLoopRange)
        {
            _previewHasCustomLoopRange = true;
            _previewLoopStart = _dragAnchorValue;
            _previewLoopEnd = _dragAnchorValue;
        }
        else
        {
            _previewHasCustomLoopRange = GetDisplayedHasCustomLoopRange();
            _previewLoopStart = DisplayedLoopStart;
            _previewLoopEnd = DisplayedLoopEnd;
        }

        InvalidateVisual();
    }

    private void CapturePointer(IPointer pointer)
    {
        _capturedPointer = pointer;
        pointer.Capture(this);
    }

    private void ReleasePointer()
    {
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        _dragMode = DragMode.None;
        _dragMoved = false;
    }

    private void UpdatePreviewValue(Point position)
    {
        _previewValue = PositionToValue(position.X);
        InvalidateVisual();
    }

    private void UpdatePreviewLoopRange(Point position)
    {
        double currentValue = PositionToValue(position.X);
        double maximum = GetMaximumValue();
        double minimumSpan = GetMinimumLoopSpan();

        switch (_dragMode)
        {
            case DragMode.CreateLoopRange:
                {
                    double start = Math.Min(_dragAnchorValue, currentValue);
                    double end = Math.Max(_dragAnchorValue, currentValue);

                    if (end - start < minimumSpan)
                    {
                        if (currentValue >= _dragAnchorValue)
                        {
                            end = Math.Min(maximum, start + minimumSpan);
                        }
                        else
                        {
                            start = Math.Max(0, end - minimumSpan);
                        }
                    }

                    _previewLoopStart = start;
                    _previewLoopEnd = Math.Max(start, end);
                    _previewHasCustomLoopRange = true;
                    break;
                }
            case DragMode.ResizeLoopStart:
                _previewLoopStart = Math.Clamp(currentValue, 0, Math.Max(0, _dragInitialLoopEnd - minimumSpan));
                _previewLoopEnd = _dragInitialLoopEnd;
                _previewHasCustomLoopRange = true;
                break;
            case DragMode.ResizeLoopEnd:
                _previewLoopStart = _dragInitialLoopStart;
                _previewLoopEnd = Math.Clamp(currentValue, Math.Min(maximum, _dragInitialLoopStart + minimumSpan), maximum);
                _previewHasCustomLoopRange = true;
                break;
            case DragMode.MoveLoopRange:
                {
                    double span = Math.Max(minimumSpan, _dragInitialLoopEnd - _dragInitialLoopStart);
                    double delta = currentValue - _dragAnchorValue;
                    double start = _dragInitialLoopStart + delta;
                    double end = _dragInitialLoopEnd + delta;

                    if (start < 0)
                    {
                        end -= start;
                        start = 0;
                    }

                    if (end > maximum)
                    {
                        start -= end - maximum;
                        end = maximum;
                    }

                    _previewLoopStart = Math.Clamp(start, 0, Math.Max(0, maximum - span));
                    _previewLoopEnd = Math.Clamp(Math.Max(_previewLoopStart + minimumSpan, end), minimumSpan, maximum);
                    _previewHasCustomLoopRange = true;
                    break;
                }
        }

        InvalidateVisual();
    }

    private void RaiseSeekChanged(double value)
        => SeekChanged?.Invoke(this, new TimelineSeekChangedEventArgs(value));

    private void RaiseSeekCompleted(double value)
        => SeekCompleted?.Invoke(this, new TimelineSeekChangedEventArgs(value));

    private void RaiseLoopRangeChanged(bool isFinal)
    {
        if (!_previewHasCustomLoopRange)
        {
            return;
        }

        double start = Math.Min(_previewLoopStart, _previewLoopEnd);
        double end = Math.Max(_previewLoopStart, _previewLoopEnd);
        if (end - start <= 0)
        {
            return;
        }

        LoopRangeChanged?.Invoke(this, new TimelineLoopRangeChangedEventArgs(start, end, isFinal));
    }

    private void UpdateCursor(PointerEventArgs e)
    {
        Cursor = GetHitTarget(e.GetPosition(this), e.KeyModifiers) switch
        {
            HitTarget.LoopStartHandle or HitTarget.LoopEndHandle => ResizeCursor,
            HitTarget.LoopRange => MoveCursor,
            HitTarget.LoopLane => RangeCursor,
            HitTarget.SeekLane => HandCursor,
            _ => HandCursor
        };
    }

    private HitTarget GetHitTarget(Point position, KeyModifiers modifiers)
    {
        if (!IsEnabled || GetMaximumValue() <= 0)
        {
            return HitTarget.None;
        }

        var trackRect = GetTrackRect();
        var hitRect = new Rect(
            trackRect.X,
            trackRect.Y - 4,
            trackRect.Width,
            trackRect.Height + 8);

        if (!hitRect.Contains(position))
        {
            return HitTarget.None;
        }

        if (GetDisplayedHasCustomLoopRange())
        {
            var selectionRect = GetSelectionRect(trackRect, SelectionVerticalInset);
            double startX = selectionRect.X;
            double endX = selectionRect.Right;

            if (Math.Abs(position.X - startX) <= HandleHotZone)
            {
                return HitTarget.LoopStartHandle;
            }

            if (Math.Abs(position.X - endX) <= HandleHotZone)
            {
                return HitTarget.LoopEndHandle;
            }

            if (position.X >= startX && position.X <= endX)
            {
                if (modifiers.HasFlag(KeyModifiers.Shift)) return HitTarget.LoopLane;
            }
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            return HitTarget.LoopLane;
        }

        return HitTarget.SeekLane;
    }

    private Rect GetTrackRect()
    {
        double width = Math.Max(0, Bounds.Width - (HorizontalPadding * 2));
        return new Rect(HorizontalPadding, TrackTop, width, TrackHeight);
    }

    private Rect GetSelectionRect(Rect trackRect, double verticalInset)
    {
        double startX = ValueToX(DisplayedLoopStart, trackRect);
        double endX = ValueToX(DisplayedLoopEnd, trackRect);
        double width = Math.Max(2, endX - startX);
        double inset = Math.Clamp(verticalInset, 0, Math.Max(0, trackRect.Height / 2 - 1));
        return new Rect(startX, trackRect.Y + inset, width, Math.Max(2, trackRect.Height - (inset * 2)));
    }

    private Rect GetProgressRect(Rect trackRect)
    {
        double valueX = ValueToX(CurrentValue, trackRect);
        double width = Math.Max(0, valueX - trackRect.X);
        return new Rect(trackRect.X, trackRect.Y, width, trackRect.Height);
    }

    private void DrawRoundedLane(DrawingContext context, Rect rect, IBrush fill, Pen borderPen)
        => context.DrawRectangle(fill, borderPen, rect, TrackCornerRadius, TrackCornerRadius);

    private void DrawHandles(DrawingContext context, Rect selectionRect)
    {
        double centerY = selectionRect.Center.Y;
        var leftHandle = new Rect(
            selectionRect.X - (HandleWidth / 2),
            centerY - (HandleHeight / 2),
            HandleWidth,
            HandleHeight);
        var rightHandle = new Rect(
            selectionRect.Right - (HandleWidth / 2),
            centerY - (HandleHeight / 2),
            HandleWidth,
            HandleHeight);

        context.DrawRectangle(HandleFill, null, leftHandle, HandleWidth / 2, HandleWidth / 2);
        context.DrawRectangle(HandleFill, null, rightHandle, HandleWidth / 2, HandleWidth / 2);
    }

    private void DrawPlayhead(DrawingContext context, Rect trackRect)
    {
        double playheadX = ValueToX(CurrentValue, trackRect);
        var lineTop = new Point(playheadX, trackRect.Y - 1);
        var lineBottom = new Point(playheadX, trackRect.Bottom + PlayheadRadius + PlayheadBottomPadding);
        context.DrawLine(PlayheadPen, lineTop, lineBottom);
        context.DrawEllipse(
            PlayheadFill,
            null,
            new Point(playheadX, trackRect.Bottom + PlayheadRadius + PlayheadBottomPadding),
            PlayheadRadius,
            PlayheadRadius);
    }

    private double PositionToValue(double positionX)
    {
        var laneRect = GetTrackRect();
        if (laneRect.Width <= 0)
        {
            return 0;
        }

        double ratio = (positionX - laneRect.X) / laneRect.Width;
        ratio = Math.Clamp(ratio, 0, 1);
        return ratio * GetMaximumValue();
    }

    private double ValueToX(double value, Rect laneRect)
    {
        if (laneRect.Width <= 0)
        {
            return laneRect.X;
        }

        double maximum = GetMaximumValue();
        double ratio = maximum <= 0 ? 0 : Math.Clamp(value / maximum, 0, 1);
        return laneRect.X + (laneRect.Width * ratio);
    }

    private double GetMinimumLoopSpan()
    {
        var laneRect = GetTrackRect();
        if (laneRect.Width <= 0)
        {
            return 0.1;
        }

        return Math.Max(0.1, GetMaximumValue() * (MinimumLoopSpanPixels / laneRect.Width));
    }

    private double GetMaximumValue()
        => Math.Max(0, Maximum);

    private bool GetDisplayedHasCustomLoopRange()
        => _usePreviewLoopRange ? _previewHasCustomLoopRange : HasCustomLoopRange;

    private double CurrentValue
        => Math.Clamp(_usePreviewValue ? _previewValue : Value, 0, GetMaximumValue());

    private double DisplayedLoopStart
        => Math.Clamp(_usePreviewLoopRange ? _previewLoopStart : LoopStart, 0, GetMaximumValue());

    private double DisplayedLoopEnd
        => Math.Clamp(_usePreviewLoopRange ? _previewLoopEnd : LoopEnd, 0, GetMaximumValue());

    private IBrush GetSelectionFill()
        => IsLoopEnabled ? SelectionFill : SelectionStandbyFill;

    private Pen GetSelectionBorderPen()
        => IsLoopEnabled ? SelectionBorderPen : SelectionStandbyBorderPen;

    private enum DragMode
    {
        None,
        Seek,
        CreateLoopRange,
        MoveLoopRange,
        ResizeLoopStart,
        ResizeLoopEnd
    }

    private enum HitTarget
    {
        None,
        SeekLane,
        LoopLane,
        LoopRange,
        LoopStartHandle,
        LoopEndHandle
    }
}

public sealed class TimelineSeekChangedEventArgs(double value) : EventArgs
{
    public double Value { get; } = value;
}

public sealed class TimelineLoopRangeChangedEventArgs(double startSeconds, double endSeconds, bool isFinal) : EventArgs
{
    public double StartSeconds { get; } = startSeconds;

    public double EndSeconds { get; } = endSeconds;

    public bool IsFinal { get; } = isFinal;
}
