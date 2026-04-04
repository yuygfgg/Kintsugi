using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MidiPlayer.App.Services;

namespace MidiPlayer.App.Controls;

public class SpectrumControl : Control
{
    public static readonly StyledProperty<BassMidiPlayer?> PlayerProperty =
        AvaloniaProperty.Register<SpectrumControl, BassMidiPlayer?>(nameof(Player));

    public BassMidiPlayer? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    private readonly DispatcherTimer _timer;
    private readonly float[] _fftBuffer = new float[16384]; // Request FFT32768 for 16384 frequency bins
    private readonly IBrush _barBrush = new SolidColorBrush(Color.Parse("#4A90E2"));

    public SpectrumControl()
    {
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => InvalidateVisual());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var player = Player;
        if (player == null || !player.HasStream) return;

        int bytesRead = player.GetFFTData(_fftBuffer);
        if (bytesRead <= 0) return;

        var bounds = Bounds;
        int numBins = _fftBuffer.Length;
        double scaleHeight = 20;
        double maxHeight = bounds.Height - scaleHeight;

        double minFreq = 20.0;
        double maxFreq = player.SampleRate / 2.0;
        double logMin = Math.Log10(minFreq);
        double logMax = Math.Log10(maxFreq);

        // 我们将 X 轴划分为固定数量的“视觉柱”，例如每 3 个像素一个
        int visualBars = Math.Max(10, (int)(bounds.Width / 3.0));
        double visualBarWidth = bounds.Width / visualBars;

        for (int i = 0; i < visualBars; i++)
        {
            double logFreqStart = logMin + (i / (double)visualBars) * (logMax - logMin);
            double logFreqEnd = logMin + ((i + 1) / (double)visualBars) * (logMax - logMin);

            double freqStart = Math.Pow(10, logFreqStart);
            double freqEnd = Math.Pow(10, logFreqEnd);

            // 映射到 FFT 的 bin 索引
            int binStart = (int)(freqStart / maxFreq * numBins);
            int binEnd = (int)(freqEnd / maxFreq * numBins);

            if (binStart < 0) binStart = 0;
            if (binEnd >= numBins) binEnd = numBins - 1;
            if (binEnd < binStart) binEnd = binStart;

            // 取该频率区间内的最大振幅
            double maxAmp = 0;
            for (int b = binStart; b <= binEnd; b++)
            {
                double amp = Math.Abs(_fftBuffer[b]);
                if (amp > maxAmp) maxAmp = amp;
            }

            double db = 20 * Math.Log10(maxAmp + 1e-6);
            double val = (db + 60) / 60 * maxHeight;

            if (val > maxHeight) val = maxHeight;
            if (val < 0) val = 0;

            if (val > 0)
            {
                double xStart = i * visualBarWidth;
                // 柱子之间留出 0.5 像素的间隙，视觉上更清晰
                var rect = new Rect(xStart, maxHeight - val, visualBarWidth - 0.5, val);
                context.DrawRectangle(_barBrush, null, rect);
            }
        }

        // 绘制底部频率标尺
        int[] labelFreqs = { 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };
        string[] labels = { "50", "100", "200", "500", "1k", "2k", "5k", "10k", "20k" };

        var typeface = new Typeface("Arial");

        // 画出底部基准线
        context.DrawLine(new Pen(Brushes.DarkGray, 1), new Point(0, maxHeight), new Point(bounds.Width, maxHeight));

        for (int i = 0; i < labelFreqs.Length; i++)
        {
            int freq = labelFreqs[i];
            if (freq >= minFreq && freq <= maxFreq)
            {
                double x = (Math.Log10(freq) - logMin) / (logMax - logMin) * bounds.Width;
                var text = new FormattedText(
                    labels[i],
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    10,
                    Brushes.Gray);

                context.DrawText(text, new Point(x - text.Width / 2, bounds.Height - 18));

                // 绘制短刻度线
                context.DrawLine(new Pen(Brushes.Gray, 1), new Point(x, maxHeight), new Point(x, maxHeight + 4));
            }
        }
    }
}
