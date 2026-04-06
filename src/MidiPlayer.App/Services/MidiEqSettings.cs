using System;

namespace MidiPlayer.App.Services;

public sealed class MidiEqSettings
{
    public const int BandCount = 8;
    private const double MinFrequency = 20.0;
    private const double MaxFrequency = 20000.0;
    private const double MinGainDb = -24.0;
    private const double MaxGainDb = 24.0;
    private const double MinQ = 0.0;
    private const double MaxQ = 2.5;
    private const int MinSlopeDbPerOct = 0;
    private const int MaxSlopeDbPerOct = 48;

    public bool IsEnabled { get; set; } = true;

    public MidiEqBandSettings[] Bands { get; set; } = CreateDefaultBands();

    public void Normalize()
    {
        var defaults = CreateDefaultBands();
        var normalized = new MidiEqBandSettings[BandCount];
        var sourceBands = Bands;

        for (var i = 0; i < normalized.Length; i++)
        {
            var source = sourceBands is not null && i < sourceBands.Length
                ? sourceBands[i] ?? defaults[i]
                : defaults[i];

            normalized[i] = new MidiEqBandSettings
            {
                Frequency = NormalizeDouble(source.Frequency, defaults[i].Frequency, MinFrequency, MaxFrequency),
                GainDb = NormalizeDouble(source.GainDb, defaults[i].GainDb, MinGainDb, MaxGainDb),
                Q = NormalizeDouble(source.Q, defaults[i].Q, MinQ, MaxQ),
                SlopeDbPerOct = Math.Clamp(source.SlopeDbPerOct, MinSlopeDbPerOct, MaxSlopeDbPerOct)
            };
        }

        Bands = normalized;
    }

    public MidiEqSettings Clone()
        => new()
        {
            IsEnabled = IsEnabled,
            Bands = CloneBands(Bands)
        };

    private static MidiEqBandSettings[] CreateDefaultBands()
        =>
        [
            new MidiEqBandSettings { Frequency = 20.0, GainDb = 0.0, Q = 0.0, SlopeDbPerOct = 0 },
            new MidiEqBandSettings { Frequency = 75.0, GainDb = 0.0, Q = 0.0, SlopeDbPerOct = 0 },
            new MidiEqBandSettings { Frequency = 100.0, GainDb = 0.0, Q = 0.0, SlopeDbPerOct = 0 },
            new MidiEqBandSettings { Frequency = 250.0, GainDb = 0.0, Q = 0.0, SlopeDbPerOct = 0 },
            new MidiEqBandSettings { Frequency = 1040.0, GainDb = 0.0, Q = 0.0, SlopeDbPerOct = 0 },
            new MidiEqBandSettings { Frequency = 2460.0, GainDb = 0.0, Q = 0.0, SlopeDbPerOct = 0 },
            new MidiEqBandSettings { Frequency = 7500.0, GainDb = 0.0, Q = 0.0, SlopeDbPerOct = 0 },
            new MidiEqBandSettings { Frequency = 20000.0, GainDb = 0.0, Q = 0.0, SlopeDbPerOct = 0 }
        ];

    private static MidiEqBandSettings[] CloneBands(MidiEqBandSettings[]? source)
    {
        var defaults = CreateDefaultBands();
        var clone = new MidiEqBandSettings[BandCount];

        for (var i = 0; i < clone.Length; i++)
        {
            var band = source is not null && i < source.Length
                ? source[i] ?? defaults[i]
                : defaults[i];

            clone[i] = band.Clone();
        }

        return clone;
    }

    private static double NormalizeDouble(double value, double defaultValue, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : defaultValue;
}

public sealed class MidiEqBandSettings
{
    public double Frequency { get; set; }

    public double GainDb { get; set; }

    public double Q { get; set; }

    public int SlopeDbPerOct { get; set; }

    public MidiEqBandSettings Clone()
        => new()
        {
            Frequency = Frequency,
            GainDb = GainDb,
            Q = Q,
            SlopeDbPerOct = SlopeDbPerOct
        };
}
