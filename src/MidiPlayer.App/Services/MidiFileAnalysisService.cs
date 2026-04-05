using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MidiPlayer.App.Models;

namespace MidiPlayer.App.Services;

internal static class MidiFileAnalysisService
{
    private const int DefaultTempoMicrosecondsPerQuarterNote = 500_000;

    public static MidiEventBrowserDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] midiData = ReadMidiPayload(path);
        var parser = new MidiFileParser(midiData);
        ParsedMidiFile midiFile = parser.Parse();
        TempoMap tempoMap = TempoMap.Create(midiFile.TimeBase, midiFile.Events);
        TimeSignatureMap timeSignatureMap = TimeSignatureMap.Create(midiFile.TimeBase, midiFile.Events);

        var orderedEvents = midiFile.Events
            .OrderBy(static evt => evt.Tick)
            .ThenBy(static evt => evt.TrackIndex)
            .ThenBy(static evt => evt.Sequence)
            .ToArray();

        var rows = new List<MidiEventBrowserRow>(orderedEvents.Length);
        foreach (ParsedMidiEvent midiEvent in orderedEvents)
        {
            EventDisplay display = CreateEventDisplay(midiEvent);
            long tick = Math.Max(0, midiEvent.Tick);
            double seconds = tempoMap.GetSeconds(tick);

            rows.Add(new MidiEventBrowserRow
            {
                TrackText = midiEvent.TrackIndex.ToString(CultureInfo.InvariantCulture),
                LocationText = timeSignatureMap.GetLocationText(tick),
                TimeText = FormatTimestamp(seconds),
                StatusText = display.StatusText,
                ChannelText = display.ChannelText,
                NumberText = display.NumberText,
                ValueText = display.ValueText,
                SummaryText = display.SummaryText,
                ToolTipText = CreateToolTipText(midiEvent, display, tick, seconds),
                Tick = tick,
                ChannelIndex = display.ChannelIndex
            });
        }

        return new MidiEventBrowserDocument
        {
            FileName = Path.GetFileName(path),
            FilePath = path,
            FormatText = $"Format {midiFile.Format}",
            DivisionText = midiFile.TimeBase.DisplayText,
            Rows = rows,
            TrackCount = midiFile.TrackCount
        };
    }

    public static PianoRollNote[] LoadPianoRollNotes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] midiData = ReadMidiPayload(path);
        var parser = new MidiFileParser(midiData);
        ParsedMidiFile midiFile = parser.Parse();
        TempoMap tempoMap = TempoMap.Create(midiFile.TimeBase, midiFile.Events);

        var orderedEvents = midiFile.Events
            .OrderBy(static evt => evt.Tick)
            .ThenBy(static evt => evt.TrackIndex)
            .ThenBy(static evt => evt.Sequence)
            .ToArray();

        var pendingNotes = new List<PendingNote>[16, 128];
        var notes = new List<PianoRollNote>(Math.Max(16, orderedEvents.Length / 4));
        long finalTick = 0;

        foreach (ParsedMidiEvent midiEvent in orderedEvents)
        {
            long tick = Math.Max(0, midiEvent.Tick);
            finalTick = Math.Max(finalTick, tick);

            if (midiEvent.Kind != ParsedEventKind.Channel)
            {
                continue;
            }

            int statusCode = midiEvent.Status & 0xF0;
            if (statusCode is not 0x80 and not 0x90)
            {
                continue;
            }

            int channel = midiEvent.Status & 0x0F;
            if ((uint)channel >= 16 || midiEvent.Data.Length < 2)
            {
                continue;
            }

            int note = midiEvent.Data[0];
            int velocity = midiEvent.Data[1];
            if ((uint)note >= 128)
            {
                continue;
            }

            if (statusCode == 0x90 && velocity > 0)
            {
                (pendingNotes[channel, note] ??= []).Add(new PendingNote(tick, tempoMap.GetSeconds(tick), velocity));
                continue;
            }

            List<PendingNote>? bucket = pendingNotes[channel, note];
            if (bucket is null || bucket.Count == 0)
            {
                continue;
            }

            PendingNote pending = bucket[0];
            bucket.RemoveAt(0);

            long endTick = Math.Max(tick, pending.StartTick + 1);
            notes.Add(new PianoRollNote(
                channel,
                note,
                pending.Velocity,
                pending.StartTick,
                endTick,
                pending.StartSeconds,
                tempoMap.GetSeconds(endTick)));
        }

        for (int channel = 0; channel < pendingNotes.GetLength(0); channel++)
        {
            for (int note = 0; note < pendingNotes.GetLength(1); note++)
            {
                List<PendingNote>? bucket = pendingNotes[channel, note];
                if (bucket is null || bucket.Count == 0)
                {
                    continue;
                }

                foreach (PendingNote pending in bucket)
                {
                    long endTick = Math.Max(finalTick, pending.StartTick + 1);
                    notes.Add(new PianoRollNote(
                        channel,
                        note,
                        pending.Velocity,
                        pending.StartTick,
                        endTick,
                        pending.StartSeconds,
                        tempoMap.GetSeconds(endTick)));
                }
            }
        }

        notes.Sort(static (left, right) =>
        {
            int startCompare = left.StartSeconds.CompareTo(right.StartSeconds);
            if (startCompare != 0)
            {
                return startCompare;
            }

            int noteCompare = left.Note.CompareTo(right.Note);
            return noteCompare != 0
                ? noteCompare
                : left.Channel.CompareTo(right.Channel);
        });

        return [.. notes];
    }

    private static string CreateToolTipText(ParsedMidiEvent midiEvent, EventDisplay display, long tick, double seconds)
    {
        var builder = new StringBuilder();
        builder.Append("Track ").Append(midiEvent.TrackIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append("  Tick ").Append(tick.ToString(CultureInfo.InvariantCulture));
        builder.Append("  Time ").Append(FormatTimestamp(seconds));
        builder.AppendLine();
        builder.Append(display.StatusText);
        if (!string.IsNullOrEmpty(display.ChannelText))
        {
            builder.Append("  Ch ").Append(display.ChannelText);
        }

        if (!string.IsNullOrEmpty(display.NumberText))
        {
            builder.Append("  No. ").Append(display.NumberText);
        }

        if (!string.IsNullOrEmpty(display.ValueText))
        {
            builder.Append("  Val ").Append(display.ValueText);
        }

        builder.AppendLine();
        builder.Append(display.SummaryText);
        builder.AppendLine();
        builder.Append("Raw: ").Append(FormatRawMessage(midiEvent));
        return builder.ToString();
    }

    private static string FormatRawMessage(ParsedMidiEvent midiEvent)
    {
        return midiEvent.Kind switch
        {
            ParsedEventKind.Meta => $"FF {midiEvent.MetaType:X2} {FormatHex(midiEvent.Data)}".TrimEnd(),
            ParsedEventKind.SysEx => $"{midiEvent.Status:X2} {FormatHex(midiEvent.Data)}".TrimEnd(),
            _ => $"{midiEvent.Status:X2} {FormatHex(midiEvent.Data)}".TrimEnd()
        };
    }

    private static EventDisplay CreateEventDisplay(ParsedMidiEvent midiEvent)
    {
        return midiEvent.Kind switch
        {
            ParsedEventKind.Channel => CreateChannelDisplay(midiEvent),
            ParsedEventKind.Meta => CreateMetaDisplay(midiEvent),
            ParsedEventKind.SysEx => CreateSysExDisplay(midiEvent),
            _ => new EventDisplay("Unknown", string.Empty, string.Empty, string.Empty, "Unsupported event", -1)
        };
    }

    private static EventDisplay CreateChannelDisplay(ParsedMidiEvent midiEvent)
    {
        int statusCode = midiEvent.Status & 0xF0;
        int zeroBasedChannel = midiEvent.Status & 0x0F;
        int channel = zeroBasedChannel + 1;
        string channelText = channel.ToString(CultureInfo.InvariantCulture);
        byte data1 = midiEvent.Data.Length > 0 ? midiEvent.Data[0] : (byte)0;
        byte data2 = midiEvent.Data.Length > 1 ? midiEvent.Data[1] : (byte)0;

        return statusCode switch
        {
            0x80 => new EventDisplay("Note", channelText, data1.ToString(CultureInfo.InvariantCulture), data2.ToString(CultureInfo.InvariantCulture), $"Note Off · {GetNoteName(data1)}", zeroBasedChannel),
            0x90 => data2 == 0
                ? new EventDisplay("Note", channelText, data1.ToString(CultureInfo.InvariantCulture), data2.ToString(CultureInfo.InvariantCulture), $"Note Off (vel 0) · {GetNoteName(data1)}", zeroBasedChannel)
                : new EventDisplay("Note", channelText, data1.ToString(CultureInfo.InvariantCulture), data2.ToString(CultureInfo.InvariantCulture), $"Note On · {GetNoteName(data1)}", zeroBasedChannel),
            0xA0 => new EventDisplay("Poly Press", channelText, data1.ToString(CultureInfo.InvariantCulture), data2.ToString(CultureInfo.InvariantCulture), $"Poly Pressure · {GetNoteName(data1)}", zeroBasedChannel),
            0xB0 => new EventDisplay("Control", channelText, data1.ToString(CultureInfo.InvariantCulture), data2.ToString(CultureInfo.InvariantCulture), GetControllerName(data1), zeroBasedChannel),
            0xC0 => new EventDisplay("Program", channelText, data1.ToString(CultureInfo.InvariantCulture), string.Empty, GetProgramName(channel - 1, data1), zeroBasedChannel),
            0xD0 => new EventDisplay("Aftertouch", channelText, string.Empty, data1.ToString(CultureInfo.InvariantCulture), "Channel Pressure", zeroBasedChannel),
            0xE0 => CreatePitchBendDisplay(channelText, data1, data2, zeroBasedChannel),
            _ => new EventDisplay("Channel", channelText, data1.ToString(CultureInfo.InvariantCulture), data2.ToString(CultureInfo.InvariantCulture), $"Status 0x{midiEvent.Status:X2}", zeroBasedChannel)
        };
    }

    private static EventDisplay CreatePitchBendDisplay(string channelText, byte lsb, byte msb, int zeroBasedChannel)
    {
        int rawValue = (msb << 7) | lsb;
        int centeredValue = rawValue - 8192;
        return new EventDisplay(
            "Pitch Bend",
            channelText,
            lsb.ToString(CultureInfo.InvariantCulture),
            msb.ToString(CultureInfo.InvariantCulture),
            $"Pitch {FormatSignedInteger(centeredValue)}",
            zeroBasedChannel);
    }

    private static EventDisplay CreateMetaDisplay(ParsedMidiEvent midiEvent)
    {
        ReadOnlySpan<byte> data = midiEvent.Data;
        string numberText = $"0x{midiEvent.MetaType:X2}";
        string valueText = data.Length.ToString(CultureInfo.InvariantCulture);

        return midiEvent.MetaType switch
        {
            0x00 => new EventDisplay("Sequence", string.Empty, numberText, ReadSequenceNumber(data), "Sequence Number", -1),
            0x01 => CreateTextMetaDisplay("Text", numberText, data),
            0x02 => CreateTextMetaDisplay("Copyright", numberText, data),
            0x03 => CreateTextMetaDisplay("Track Name", numberText, data),
            0x04 => CreateTextMetaDisplay("Instrument", numberText, data),
            0x05 => CreateTextMetaDisplay("Lyrics", numberText, data),
            0x06 => CreateTextMetaDisplay("Marker", numberText, data),
            0x07 => CreateTextMetaDisplay("Cue", numberText, data),
            0x20 => new EventDisplay("Ch Prefix", string.Empty, numberText, data.Length > 0 ? data[0].ToString(CultureInfo.InvariantCulture) : string.Empty, "MIDI Channel Prefix", -1),
            0x21 => new EventDisplay("Port", string.Empty, numberText, data.Length > 0 ? data[0].ToString(CultureInfo.InvariantCulture) : string.Empty, "MIDI Port", -1),
            0x2F => new EventDisplay("End", string.Empty, numberText, valueText, "End of Track", -1),
            0x51 => CreateTempoDisplay(numberText, data),
            0x54 => new EventDisplay("SMPTE", string.Empty, numberText, valueText, DescribeSmpteOffset(data), -1),
            0x58 => new EventDisplay("Time Sig", string.Empty, numberText, valueText, DescribeTimeSignature(data), -1),
            0x59 => new EventDisplay("Key Sig", string.Empty, numberText, valueText, DescribeKeySignature(data), -1),
            0x7F => new EventDisplay("Sequencer", string.Empty, numberText, valueText, $"Sequencer Specific · {FormatHexPreview(data, 12)}", -1),
            _ => new EventDisplay("Meta", string.Empty, numberText, valueText, $"Meta 0x{midiEvent.MetaType:X2} · {FormatHexPreview(data, 12)}", -1)
        };
    }

    private static EventDisplay CreateTextMetaDisplay(string statusText, string numberText, ReadOnlySpan<byte> data)
    {
        string text = DecodeText(data);
        return new EventDisplay(statusText, string.Empty, numberText, data.Length.ToString(CultureInfo.InvariantCulture), string.IsNullOrWhiteSpace(text) ? "(empty)" : text, -1);
    }

    private static EventDisplay CreateTempoDisplay(string numberText, ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return new EventDisplay("Tempo", string.Empty, numberText, data.Length.ToString(CultureInfo.InvariantCulture), "Invalid tempo event", -1);
        }

        int microsecondsPerQuarterNote = (data[0] << 16) | (data[1] << 8) | data[2];
        if (microsecondsPerQuarterNote <= 0)
        {
            return new EventDisplay("Tempo", string.Empty, numberText, "0", "Invalid tempo value", -1);
        }

        double bpm = 60_000_000d / microsecondsPerQuarterNote;
        return new EventDisplay(
            "Tempo",
            string.Empty,
            numberText,
            bpm.ToString(bpm >= 100d ? "0.0" : "0.00", CultureInfo.InvariantCulture),
            $"{bpm.ToString("0.##", CultureInfo.InvariantCulture)} BPM",
            -1);
    }

    private static EventDisplay CreateSysExDisplay(ParsedMidiEvent midiEvent)
    {
        string statusText = midiEvent.Status == 0xF7 ? "SysEx Cont" : "System";
        return new EventDisplay(
            statusText,
            string.Empty,
            $"0x{midiEvent.Status:X2}",
            midiEvent.Data.Length.ToString(CultureInfo.InvariantCulture),
            $"{FormatHexPreview(midiEvent.Data, 14)}",
            -1);
    }

    private static string DecodeText(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        string text = Encoding.UTF8.GetString(data);
        if (text.Contains('\uFFFD'))
        {
            text = Encoding.Latin1.GetString(data);
        }

        return CollapseWhitespace(text);
    }

    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string ReadSequenceNumber(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return string.Empty;
        }

        ushort value = BinaryPrimitives.ReadUInt16BigEndian(data);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string DescribeSmpteOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            return "Invalid SMPTE offset";
        }

        return $"SMPTE {data[0]:D2}:{data[1]:D2}:{data[2]:D2}:{data[3]:D2}.{data[4]:D2}";
    }

    private static string DescribeTimeSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return "Invalid time signature";
        }

        int numerator = data[0];
        int denominator = 1 << data[1];
        int clocksPerClick = data.Length > 2 ? data[2] : 24;
        int notated32ndsPerQuarter = data.Length > 3 ? data[3] : 8;
        return $"{numerator}/{denominator} · Metronome {clocksPerClick} · 32nds {notated32ndsPerQuarter}";
    }

    private static string DescribeKeySignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return "Invalid key signature";
        }

        int accidentals = unchecked((sbyte)data[0]);
        bool isMinor = data[1] != 0;

        string[] majorKeys = ["Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#"];
        string[] minorKeys = ["Abm", "Ebm", "Bbm", "Fm", "Cm", "Gm", "Dm", "Am", "Em", "Bm", "F#m", "C#m", "G#m", "D#m", "A#m"];
        int index = Math.Clamp(accidentals + 7, 0, majorKeys.Length - 1);
        return isMinor ? minorKeys[index] : majorKeys[index];
    }

    private static string GetProgramName(int zeroBasedChannel, int program)
    {
        if ((uint)zeroBasedChannel == 9)
        {
            return program switch
            {
                0 => "Standard Drum Kit",
                8 => "Room Drum Kit",
                16 => "Power Drum Kit",
                24 => "Electronic Drum Kit",
                25 => "TR-808 Drum Kit",
                32 => "Jazz Drum Kit",
                40 => "Brush Drum Kit",
                48 => "Orchestra Drum Kit",
                56 => "Sound FX Drum Kit",
                _ => $"Drum Kit {program}"
            };
        }

        return MidiStandardNames.GetProgramName(program);
    }

    private static string GetControllerName(int controller)
        => MidiStandardNames.GetControllerName(controller);

    private static string GetNoteName(int noteNumber)
    {
        string[] pitchClasses = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        int octave = (noteNumber / 12) - 1;
        return $"{pitchClasses[noteNumber % 12]}{octave}";
    }

    private static string FormatSignedInteger(int value)
    {
        return value > 0
            ? $"+{value.ToString(CultureInfo.InvariantCulture)}"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatHex(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(data.Length * 3);
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string FormatHexPreview(ReadOnlySpan<byte> data, int maxBytes)
    {
        if (data.IsEmpty)
        {
            return "(empty)";
        }

        int previewLength = Math.Min(data.Length, Math.Max(1, maxBytes));
        string preview = FormatHex(data[..previewLength]);
        return data.Length > previewLength
            ? $"{preview} ..."
            : preview;
    }

    private static string FormatTimestamp(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "00:00.000";
        }

        TimeSpan time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1d
            ? time.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static byte[] ReadMidiPayload(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 4)
        {
            throw new InvalidDataException("The MIDI file is empty or truncated.");
        }

        ReadOnlySpan<byte> span = bytes;
        if (span[..4].SequenceEqual("MThd"u8))
        {
            return bytes;
        }

        if (bytes.Length >= 12 && span[..4].SequenceEqual("RIFF"u8) && span[8..12].SequenceEqual("RMID"u8))
        {
            int offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                ReadOnlySpan<byte> chunkHeader = span.Slice(offset, 8);
                uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);
                int chunkDataOffset = offset + 8;
                if (chunkDataOffset + chunkSize > bytes.Length)
                {
                    break;
                }

                if (chunkHeader[..4].SequenceEqual("data"u8))
                {
                    return bytes.AsSpan(chunkDataOffset, checked((int)chunkSize)).ToArray();
                }

                int paddedChunkSize = checked((int)((chunkSize + 1u) & ~1u));
                offset = checked(chunkDataOffset + paddedChunkSize);
            }

            throw new InvalidDataException("The RMID file does not contain a MIDI data chunk.");
        }

        throw new InvalidDataException("Unsupported MIDI container. Expected SMF or RMID.");
    }

    private enum ParsedEventKind
    {
        Channel,
        Meta,
        SysEx
    }

    private readonly record struct ParsedMidiFile(int Format, int TrackCount, TimeBase TimeBase, ParsedMidiEvent[] Events);

    private readonly record struct ParsedMidiEvent(int TrackIndex, long Tick, int Sequence, ParsedEventKind Kind, byte Status, byte MetaType, byte[] Data);

    private readonly record struct PendingNote(long StartTick, double StartSeconds, int Velocity);

    private readonly record struct EventDisplay(string StatusText, string ChannelText, string NumberText, string ValueText, string SummaryText, int ChannelIndex);

    private sealed class MidiFileParser
    {
        private readonly byte[] _data;
        private int _sequence;

        public MidiFileParser(byte[] data)
        {
            _data = data;
        }

        public ParsedMidiFile Parse()
        {
            ReadOnlySpan<byte> span = _data;
            if (span.Length < 14 || !span[..4].SequenceEqual("MThd"u8))
            {
                throw new InvalidDataException("The MIDI header is missing.");
            }

            int headerLength = ReadInt32BigEndian(span[4..8]);
            if (headerLength < 6 || 8 + headerLength > span.Length)
            {
                throw new InvalidDataException("The MIDI header is truncated.");
            }

            ReadOnlySpan<byte> headerBody = span.Slice(8, headerLength);
            int format = ReadUInt16BigEndian(headerBody[..2]);
            int trackCount = ReadUInt16BigEndian(headerBody[2..4]);
            ushort division = BinaryPrimitives.ReadUInt16BigEndian(headerBody[4..6]);
            TimeBase timeBase = TimeBase.Create(division);

            int offset = 8 + headerLength;
            var events = new List<ParsedMidiEvent>();
            for (int trackIndex = 1; trackIndex <= trackCount; trackIndex++)
            {
                if (offset + 8 > span.Length || !span.Slice(offset, 4).SequenceEqual("MTrk"u8))
                {
                    throw new InvalidDataException($"Track {trackIndex} header is missing.");
                }

                int trackLength = ReadInt32BigEndian(span.Slice(offset + 4, 4));
                int trackDataOffset = offset + 8;
                if (trackLength < 0 || trackDataOffset + trackLength > span.Length)
                {
                    throw new InvalidDataException($"Track {trackIndex} is truncated.");
                }

                ParseTrack(trackIndex, span.Slice(trackDataOffset, trackLength), events);
                offset = trackDataOffset + trackLength;
            }

            return new ParsedMidiFile(format, trackCount, timeBase, [.. events]);
        }

        private void ParseTrack(int trackIndex, ReadOnlySpan<byte> trackData, List<ParsedMidiEvent> events)
        {
            int offset = 0;
            long tick = 0;
            byte runningStatus = 0;

            while (offset < trackData.Length)
            {
                tick += ReadVariableLength(trackData, ref offset);
                if (offset >= trackData.Length)
                {
                    throw new InvalidDataException($"Track {trackIndex} ended unexpectedly while reading an event.");
                }

                byte status = trackData[offset];
                if (status < 0x80)
                {
                    if (runningStatus == 0)
                    {
                        throw new InvalidDataException($"Track {trackIndex} uses running status before any status byte was defined.");
                    }

                    status = runningStatus;
                }
                else
                {
                    offset++;
                    if (status < 0xF0)
                    {
                        runningStatus = status;
                    }
                }

                if (status == 0xFF)
                {
                    if (offset >= trackData.Length)
                    {
                        throw new InvalidDataException($"Track {trackIndex} contains an incomplete meta event.");
                    }

                    byte metaType = trackData[offset++];
                    int length = checked((int)ReadVariableLength(trackData, ref offset));
                    byte[] data = ReadBytes(trackData, ref offset, length, trackIndex);
                    events.Add(new ParsedMidiEvent(trackIndex, tick, _sequence++, ParsedEventKind.Meta, status, metaType, data));

                    if (metaType == 0x2F)
                    {
                        break;
                    }

                    continue;
                }

                if (status is 0xF0 or 0xF7)
                {
                    int length = checked((int)ReadVariableLength(trackData, ref offset));
                    byte[] data = ReadBytes(trackData, ref offset, length, trackIndex);
                    events.Add(new ParsedMidiEvent(trackIndex, tick, _sequence++, ParsedEventKind.SysEx, status, 0, data));
                    continue;
                }

                int dataLength = GetChannelEventDataLength(status);
                byte[] messageData = ReadBytes(trackData, ref offset, dataLength, trackIndex);
                events.Add(new ParsedMidiEvent(trackIndex, tick, _sequence++, ParsedEventKind.Channel, status, 0, messageData));
            }
        }

        private static int GetChannelEventDataLength(byte status)
        {
            return (status & 0xF0) switch
            {
                0xC0 or 0xD0 => 1,
                >= 0x80 and <= 0xE0 => 2,
                _ => throw new InvalidDataException($"Unsupported MIDI status byte 0x{status:X2}.")
            };
        }

        private static byte[] ReadBytes(ReadOnlySpan<byte> source, ref int offset, int length, int trackIndex)
        {
            if (length < 0 || offset + length > source.Length)
            {
                throw new InvalidDataException($"Track {trackIndex} ended unexpectedly while reading event data.");
            }

            byte[] data = source.Slice(offset, length).ToArray();
            offset += length;
            return data;
        }

        private static int ReadInt32BigEndian(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
            {
                throw new InvalidDataException("Unexpected end of file.");
            }

            return BinaryPrimitives.ReadInt32BigEndian(data);
        }

        private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2)
            {
                throw new InvalidDataException("Unexpected end of file.");
            }

            return BinaryPrimitives.ReadUInt16BigEndian(data);
        }

        private static long ReadVariableLength(ReadOnlySpan<byte> source, ref int offset)
        {
            long value = 0;
            int bytesRead = 0;
            while (true)
            {
                if (offset >= source.Length)
                {
                    throw new InvalidDataException("Unexpected end of track while reading a variable-length value.");
                }

                byte current = source[offset++];
                value = (value << 7) | (uint)(current & 0x7F);
                bytesRead++;
                if ((current & 0x80) == 0)
                {
                    return value;
                }

                if (bytesRead >= 4)
                {
                    throw new InvalidDataException("Variable-length value exceeds four bytes.");
                }
            }
        }
    }

    private readonly record struct TimeBase(TimeBaseKind Kind, int TicksPerQuarterNote, double TicksPerSecond)
    {
        public static TimeBase Create(ushort division)
        {
            if ((division & 0x8000) == 0)
            {
                int ticksPerQuarterNote = division == 0 ? 480 : division;
                return new TimeBase(TimeBaseKind.Ppq, ticksPerQuarterNote, 0);
            }

            int ticksPerFrame = division & 0xFF;
            if (ticksPerFrame <= 0)
            {
                return new TimeBase(TimeBaseKind.Ppq, 480, 0);
            }

            sbyte frameCode = unchecked((sbyte)(division >> 8));
            double framesPerSecond = frameCode switch
            {
                -24 => 24d,
                -25 => 25d,
                -29 => 29.97d,
                -30 => 30d,
                _ => 30d
            };

            return new TimeBase(TimeBaseKind.Smpte, 0, framesPerSecond * ticksPerFrame);
        }

        public string DisplayText => Kind switch
        {
            TimeBaseKind.Ppq => $"PPQ {TicksPerQuarterNote}",
            TimeBaseKind.Smpte => $"SMPTE {TicksPerSecond.ToString("0.###", CultureInfo.InvariantCulture)} ticks/s",
            _ => "Unknown"
        };
    }

    private enum TimeBaseKind
    {
        Unknown,
        Ppq,
        Smpte
    }

    private sealed class TempoMap
    {
        private readonly TimeBase _timeBase;
        private readonly TempoPoint[] _points;

        private TempoMap(TimeBase timeBase, TempoPoint[] points)
        {
            _timeBase = timeBase;
            _points = points;
        }

        public static TempoMap Create(TimeBase timeBase, IReadOnlyList<ParsedMidiEvent> events)
        {
            if (timeBase.Kind != TimeBaseKind.Ppq)
            {
                return new TempoMap(timeBase, [new TempoPoint(0, 0, DefaultTempoMicrosecondsPerQuarterNote)]);
            }

            var tempoEvents = new List<(long Tick, int Sequence, int Tempo)>(events.Count);
            foreach (ParsedMidiEvent midiEvent in events)
            {
                if (midiEvent.Kind != ParsedEventKind.Meta || midiEvent.MetaType != 0x51 || midiEvent.Data.Length < 3)
                {
                    continue;
                }

                int tempo = (midiEvent.Data[0] << 16) | (midiEvent.Data[1] << 8) | midiEvent.Data[2];
                if (tempo > 0)
                {
                    tempoEvents.Add((midiEvent.Tick, midiEvent.Sequence, tempo));
                }
            }

            tempoEvents.Sort(static (left, right) =>
            {
                int tickCompare = left.Tick.CompareTo(right.Tick);
                return tickCompare != 0 ? tickCompare : left.Sequence.CompareTo(right.Sequence);
            });

            var points = new List<TempoPoint>(tempoEvents.Count + 1)
            {
                new(0, 0, DefaultTempoMicrosecondsPerQuarterNote)
            };

            foreach ((long tick, _, int tempo) in tempoEvents)
            {
                if (points.Count > 0 && points[^1].Tick == tick)
                {
                    points[^1] = new TempoPoint(tick, points[^1].SecondsAtTick, tempo);
                    continue;
                }

                TempoPoint previous = points[^1];
                double secondsAtTick = previous.SecondsAtTick + ConvertTicksToSeconds(tick - previous.Tick, previous.MicrosecondsPerQuarterNote, timeBase.TicksPerQuarterNote);
                points.Add(new TempoPoint(tick, secondsAtTick, tempo));
            }

            return new TempoMap(timeBase, [.. points]);
        }

        public double GetSeconds(long tick)
        {
            if (tick <= 0)
            {
                return 0;
            }

            if (_timeBase.Kind == TimeBaseKind.Smpte)
            {
                return _timeBase.TicksPerSecond <= 0
                    ? 0
                    : tick / _timeBase.TicksPerSecond;
            }

            TempoPoint point = _points[FindPointIndex(tick)];
            return point.SecondsAtTick + ConvertTicksToSeconds(tick - point.Tick, point.MicrosecondsPerQuarterNote, _timeBase.TicksPerQuarterNote);
        }

        private int FindPointIndex(long tick)
        {
            int low = 0;
            int high = _points.Length - 1;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (_points[mid].Tick <= tick)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low;
        }

        private static double ConvertTicksToSeconds(long deltaTicks, int microsecondsPerQuarterNote, int ticksPerQuarterNote)
        {
            if (deltaTicks <= 0 || microsecondsPerQuarterNote <= 0 || ticksPerQuarterNote <= 0)
            {
                return 0;
            }

            return deltaTicks * microsecondsPerQuarterNote / 1_000_000d / ticksPerQuarterNote;
        }
    }

    private readonly record struct TempoPoint(long Tick, double SecondsAtTick, int MicrosecondsPerQuarterNote);

    private sealed class TimeSignatureMap
    {
        private readonly TimeBase _timeBase;
        private readonly SignaturePoint[] _points;

        private TimeSignatureMap(TimeBase timeBase, SignaturePoint[] points)
        {
            _timeBase = timeBase;
            _points = points;
        }

        public static TimeSignatureMap Create(TimeBase timeBase, IReadOnlyList<ParsedMidiEvent> events)
        {
            if (timeBase.Kind != TimeBaseKind.Ppq || timeBase.TicksPerQuarterNote <= 0)
            {
                return new TimeSignatureMap(timeBase, [new SignaturePoint(0, 4, 4, 1, 0)]);
            }

            var rawPoints = new List<(long Tick, int Sequence, int Numerator, int Denominator)>(events.Count);
            foreach (ParsedMidiEvent midiEvent in events)
            {
                if (midiEvent.Kind != ParsedEventKind.Meta || midiEvent.MetaType != 0x58 || midiEvent.Data.Length < 2)
                {
                    continue;
                }

                int numerator = Math.Max(1, (int)midiEvent.Data[0]);
                int denominator = 1 << Math.Clamp((int)midiEvent.Data[1], 0, 7);
                rawPoints.Add((midiEvent.Tick, midiEvent.Sequence, numerator, denominator));
            }

            rawPoints.Sort(static (left, right) =>
            {
                int tickCompare = left.Tick.CompareTo(right.Tick);
                return tickCompare != 0 ? tickCompare : left.Sequence.CompareTo(right.Sequence);
            });

            var normalized = new List<(long Tick, int Numerator, int Denominator)>(rawPoints.Count + 1)
            {
                (0, 4, 4)
            };

            foreach ((long tick, _, int numerator, int denominator) in rawPoints)
            {
                if (normalized.Count > 0 && normalized[^1].Tick == tick)
                {
                    normalized[^1] = (tick, numerator, denominator);
                    continue;
                }

                normalized.Add((tick, numerator, denominator));
            }

            var points = new List<SignaturePoint>(normalized.Count);
            SignaturePoint current = new(0, normalized[0].Numerator, normalized[0].Denominator, 1, 0);
            points.Add(current);

            for (int i = 1; i < normalized.Count; i++)
            {
                long deltaTicks = Math.Max(0, normalized[i].Tick - current.Tick);
                long ticksPerBar = GetTicksPerBar(timeBase.TicksPerQuarterNote, current.Numerator, current.Denominator);
                long totalTicksIntoBar = current.TicksIntoBarAtStart + deltaTicks;
                long completedBars = ticksPerBar > 0 ? totalTicksIntoBar / ticksPerBar : 0;
                long ticksIntoBar = ticksPerBar > 0 ? totalTicksIntoBar % ticksPerBar : 0;

                current = new SignaturePoint(
                    normalized[i].Tick,
                    normalized[i].Numerator,
                    normalized[i].Denominator,
                    current.BarAtStart + completedBars,
                    ticksIntoBar);
                points.Add(current);
            }

            return new TimeSignatureMap(timeBase, [.. points]);
        }

        public string GetLocationText(long tick)
        {
            if (tick <= 0)
            {
                return _timeBase.Kind == TimeBaseKind.Ppq
                    ? "001:01:000"
                    : "000000";
            }

            if (_timeBase.Kind != TimeBaseKind.Ppq || _timeBase.TicksPerQuarterNote <= 0)
            {
                return tick.ToString("000000", CultureInfo.InvariantCulture);
            }

            SignaturePoint point = _points[FindPointIndex(tick)];
            long ticksPerBeat = GetTicksPerBeat(_timeBase.TicksPerQuarterNote, point.Denominator);
            long ticksPerBar = GetTicksPerBar(_timeBase.TicksPerQuarterNote, point.Numerator, point.Denominator);
            long totalTicksIntoBar = point.TicksIntoBarAtStart + Math.Max(0, tick - point.Tick);
            long barOffset = ticksPerBar > 0 ? totalTicksIntoBar / ticksPerBar : 0;
            long ticksIntoBar = ticksPerBar > 0 ? totalTicksIntoBar % ticksPerBar : totalTicksIntoBar;
            long beat = ticksPerBeat > 0 ? (ticksIntoBar / ticksPerBeat) + 1 : 1;
            long tickIntoBeat = ticksPerBeat > 0 ? ticksIntoBar % ticksPerBeat : 0;
            long bar = point.BarAtStart + barOffset;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{bar:000}:{beat:00}:{tickIntoBeat:000}");
        }

        private int FindPointIndex(long tick)
        {
            int low = 0;
            int high = _points.Length - 1;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (_points[mid].Tick <= tick)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low;
        }

        private static long GetTicksPerBeat(int ticksPerQuarterNote, int denominator)
        {
            if (ticksPerQuarterNote <= 0)
            {
                return 0;
            }

            return Math.Max(1, (long)Math.Round(ticksPerQuarterNote * (4d / denominator)));
        }

        private static long GetTicksPerBar(int ticksPerQuarterNote, int numerator, int denominator)
        {
            return GetTicksPerBeat(ticksPerQuarterNote, denominator) * Math.Max(1, numerator);
        }
    }

    private readonly record struct SignaturePoint(long Tick, int Numerator, int Denominator, long BarAtStart, long TicksIntoBarAtStart);
}
