namespace MidiPlayer.App.Models;

public readonly record struct PianoRollNote(
    int Channel,
    int Note,
    int Velocity,
    long StartTick,
    long EndTick,
    double StartSeconds,
    double EndSeconds);
