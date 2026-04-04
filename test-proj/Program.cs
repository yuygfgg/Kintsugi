using System;
using System.Threading;
using ManagedBass;
using ManagedBass.Midi;

class Program {
    static void Main() {
        Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero);
        // We don't have a MIDI file easily available, but we can synthesize notes.
        var handle = BassMidi.CreateStream(16, BassFlags.Default, 44100);
        
        bool[] muted = new bool[16];
        MidiFilterProcedure filter = (int h, int t, MidiEvent m, bool r, IntPtr u) => {
            if (m.EventType == MidiEventType.Note && muted[m.Channel]) {
                int vel = (m.Parameter >> 8) & 0xFF;
                if (vel > 0) return false;
            }
            return true;
        };
        BassMidi.StreamSetFilter(handle, true, filter, IntPtr.Zero);
        
        Bass.ChannelPlay(handle);
        
        Console.WriteLine("Playing notes...");
        BassMidi.StreamEvent(handle, 0, MidiEventType.Note, (100 << 8) | 60); // C4 on
        Thread.Sleep(1000);
        BassMidi.StreamEvent(handle, 0, MidiEventType.Note, 60); // C4 off (vel 0)
        
        muted[0] = true;
        Console.WriteLine("Muted...");
        BassMidi.StreamEvent(handle, 0, MidiEventType.Note, (100 << 8) | 64); // E4 on
        Thread.Sleep(1000);
        BassMidi.StreamEvent(handle, 0, MidiEventType.Note, 64); // off
        
        Console.WriteLine("Done.");
        Bass.StreamFree(handle);
        Bass.Free();
    }
}
