using System;

namespace MidiPlayer.App.Services;

internal static class MidiStandardNames
{
    // General MIDI Level 1 program names used by the player and event browser.
    // Source: Microsoft Learn, "Standard MIDI Patch Assignments".
    // The comments show the 1-based GM Program Number; the array index is Program Number - 1.
    private static readonly string[] GeneralMidiInstrumentNames =
    [
        /* 001 */ "Acoustic Grand Piano",
        /* 002 */ "Bright Acoustic Piano",
        /* 003 */ "Electric Grand Piano",
        /* 004 */ "Honky-tonk Piano",
        /* 005 */ "Electric Piano 1",
        /* 006 */ "Electric Piano 2",
        /* 007 */ "Harpsichord",
        /* 008 */ "Clavinet",
        /* 009 */ "Celesta",
        /* 010 */ "Glockenspiel",
        /* 011 */ "Music Box",
        /* 012 */ "Vibraphone",
        /* 013 */ "Marimba",
        /* 014 */ "Xylophone",
        /* 015 */ "Tubular Bells",
        /* 016 */ "Dulcimer",
        /* 017 */ "Drawbar Organ",
        /* 018 */ "Percussive Organ",
        /* 019 */ "Rock Organ",
        /* 020 */ "Church Organ",
        /* 021 */ "Reed Organ",
        /* 022 */ "Accordion",
        /* 023 */ "Harmonica",
        /* 024 */ "Tango Accordion",
        /* 025 */ "Acoustic Guitar (nylon)",
        /* 026 */ "Acoustic Guitar (steel)",
        /* 027 */ "Electric Guitar (jazz)",
        /* 028 */ "Electric Guitar (clean)",
        /* 029 */ "Electric Guitar (muted)",
        /* 030 */ "Overdriven Guitar",
        /* 031 */ "Distortion Guitar",
        /* 032 */ "Guitar Harmonics",
        /* 033 */ "Acoustic Bass",
        /* 034 */ "Electric Bass (finger)",
        /* 035 */ "Electric Bass (pick)",
        /* 036 */ "Fretless Bass",
        /* 037 */ "Slap Bass 1",
        /* 038 */ "Slap Bass 2",
        /* 039 */ "Synth Bass 1",
        /* 040 */ "Synth Bass 2",
        /* 041 */ "Violin",
        /* 042 */ "Viola",
        /* 043 */ "Cello",
        /* 044 */ "Contrabass",
        /* 045 */ "Tremolo Strings",
        /* 046 */ "Pizzicato Strings",
        /* 047 */ "Orchestral Harp",
        /* 048 */ "Timpani",
        /* 049 */ "String Ensemble 1",
        /* 050 */ "String Ensemble 2",
        /* 051 */ "Synth Strings 1",
        /* 052 */ "Synth Strings 2",
        /* 053 */ "Choir Aahs",
        /* 054 */ "Voice Oohs",
        /* 055 */ "Synth Voice",
        /* 056 */ "Orchestra Hit",
        /* 057 */ "Trumpet",
        /* 058 */ "Trombone",
        /* 059 */ "Tuba",
        /* 060 */ "Muted Trumpet",
        /* 061 */ "French Horn",
        /* 062 */ "Brass Section",
        /* 063 */ "Synth Brass 1",
        /* 064 */ "Synth Brass 2",
        /* 065 */ "Soprano Sax",
        /* 066 */ "Alto Sax",
        /* 067 */ "Tenor Sax",
        /* 068 */ "Baritone Sax",
        /* 069 */ "Oboe",
        /* 070 */ "English Horn",
        /* 071 */ "Bassoon",
        /* 072 */ "Clarinet",
        /* 073 */ "Piccolo",
        /* 074 */ "Flute",
        /* 075 */ "Recorder",
        /* 076 */ "Pan Flute",
        /* 077 */ "Blown Bottle",
        /* 078 */ "Shakuhachi",
        /* 079 */ "Whistle",
        /* 080 */ "Ocarina",
        /* 081 */ "Lead 1 (square)",
        /* 082 */ "Lead 2 (sawtooth)",
        /* 083 */ "Lead 3 (calliope)",
        /* 084 */ "Lead 4 (chiff)",
        /* 085 */ "Lead 5 (charang)",
        /* 086 */ "Lead 6 (voice)",
        /* 087 */ "Lead 7 (fifths)",
        /* 088 */ "Lead 8 (bass + lead)",
        /* 089 */ "Pad 1 (new age)",
        /* 090 */ "Pad 2 (warm)",
        /* 091 */ "Pad 3 (polysynth)",
        /* 092 */ "Pad 4 (choir)",
        /* 093 */ "Pad 5 (bowed)",
        /* 094 */ "Pad 6 (metallic)",
        /* 095 */ "Pad 7 (halo)",
        /* 096 */ "Pad 8 (sweep)",
        /* 097 */ "FX 1 (rain)",
        /* 098 */ "FX 2 (soundtrack)",
        /* 099 */ "FX 3 (crystal)",
        /* 100 */ "FX 4 (atmosphere)",
        /* 101 */ "FX 5 (brightness)",
        /* 102 */ "FX 6 (goblins)",
        /* 103 */ "FX 7 (echoes)",
        /* 104 */ "FX 8 (sci-fi)",
        /* 105 */ "Sitar",
        /* 106 */ "Banjo",
        /* 107 */ "Shamisen",
        /* 108 */ "Koto",
        /* 109 */ "Kalimba",
        /* 110 */ "Bag Pipe",
        /* 111 */ "Fiddle",
        /* 112 */ "Shanai",
        /* 113 */ "Tinkle Bell",
        /* 114 */ "Agogo",
        /* 115 */ "Steel Drums",
        /* 116 */ "Woodblock",
        /* 117 */ "Taiko Drum",
        /* 118 */ "Melodic Tom",
        /* 119 */ "Synth Drum",
        /* 120 */ "Reverse Cymbal",
        /* 121 */ "Guitar Fret Noise",
        /* 122 */ "Breath Noise",
        /* 123 */ "Seashore",
        /* 124 */ "Bird Tweet",
        /* 125 */ "Telephone Ring",
        /* 126 */ "Helicopter",
        /* 127 */ "Applause",
        /* 128 */ "Gunshot"
    ];

    private static readonly string?[] ControllerNames = BuildControllerNameTable();

    public static string GetProgramName(int program)
        => (uint)program < GeneralMidiInstrumentNames.Length
            ? GeneralMidiInstrumentNames[program]
            : $"Program {program}";

    public static string GetControllerName(int controller)
        => (uint)controller < ControllerNames.Length && !string.IsNullOrWhiteSpace(ControllerNames[controller])
            ? ControllerNames[controller]!
            : $"Controller {controller}";

    private static string?[] BuildControllerNameTable()
    {
        var names = new string?[128];

        names[0] = "Bank Select";
        names[1] = "Modulation Wheel or Lever";
        names[2] = "Breath Controller";
        names[4] = "Foot Controller";
        names[5] = "Portamento Time";
        names[6] = "Data Entry MSB";
        names[7] = "Channel Volume";
        names[8] = "Balance";
        names[10] = "Pan";
        names[11] = "Expression Controller";
        names[12] = "Effect Control 1";
        names[13] = "Effect Control 2";
        names[16] = "General Purpose Controller 1";
        names[17] = "General Purpose Controller 2";
        names[18] = "General Purpose Controller 3";
        names[19] = "General Purpose Controller 4";
        names[32] = "Bank Select LSB";
        names[33] = "Modulation Wheel or Lever LSB";
        names[34] = "Breath Controller LSB";
        names[36] = "Foot Controller LSB";
        names[37] = "Portamento Time LSB";
        names[38] = "Data Entry LSB";
        names[39] = "Channel Volume LSB";
        names[40] = "Balance LSB";
        names[42] = "Pan LSB";
        names[43] = "Expression Controller LSB";
        names[44] = "Effect Control 1 LSB";
        names[45] = "Effect Control 2 LSB";
        names[48] = "General Purpose Controller 1 LSB";
        names[49] = "General Purpose Controller 2 LSB";
        names[50] = "General Purpose Controller 3 LSB";
        names[51] = "General Purpose Controller 4 LSB";
        names[64] = "Damper Pedal (Sustain)";
        names[65] = "Portamento On/Off";
        names[66] = "Sostenuto On/Off";
        names[67] = "Soft Pedal On/Off";
        names[68] = "Legato Footswitch";
        names[69] = "Hold 2";
        names[70] = "Sound Controller 1 (Sound Variation)";
        names[71] = "Sound Controller 2 (Timbre/Harmonic Intensity)";
        names[72] = "Sound Controller 3 (Release Time)";
        names[73] = "Sound Controller 4 (Attack Time)";
        names[74] = "Sound Controller 5 (Brightness)";
        names[75] = "Sound Controller 6 (Decay Time)";
        names[76] = "Sound Controller 7 (Vibrato Rate)";
        names[77] = "Sound Controller 8 (Vibrato Depth)";
        names[78] = "Sound Controller 9 (Vibrato Delay)";
        names[79] = "Sound Controller 10";
        names[80] = "General Purpose Controller 5";
        names[81] = "General Purpose Controller 6";
        names[82] = "General Purpose Controller 7";
        names[83] = "General Purpose Controller 8";
        names[84] = "Portamento Control";
        names[88] = "High Resolution Velocity Prefix";
        names[91] = "Effects 1 Depth (Reverb Send Level)";
        names[92] = "Effects 2 Depth (Tremolo)";
        names[93] = "Effects 3 Depth (Chorus Send Level)";
        names[94] = "Effects 4 Depth (Celeste Detune)";
        names[95] = "Effects 5 Depth (Phaser)";
        names[96] = "Data Increment";
        names[97] = "Data Decrement";
        names[98] = "NRPN LSB";
        names[99] = "NRPN MSB";
        names[100] = "RPN LSB";
        names[101] = "RPN MSB";
        names[120] = "All Sound Off";
        names[121] = "Reset All Controllers";
        names[122] = "Local Control On/Off";
        names[123] = "All Notes Off";
        names[124] = "Omni Mode Off";
        names[125] = "Omni Mode On";
        names[126] = "Mono Mode On";
        names[127] = "Poly Mode On";

        return names;
    }
}
