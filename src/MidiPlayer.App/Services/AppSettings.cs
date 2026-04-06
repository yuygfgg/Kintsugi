using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ManagedBass.Midi;

namespace MidiPlayer.App.Services;

public class AppSettings
{
    public string? SoundFontPath { get; set; }
    public MidiSystem SystemMode { get; set; } = MidiSystem.Default;
    public int SampleRate { get; set; } = 44100;
    public Dictionary<string, MidiMixSettings> MidiMixSettingsByHash { get; set; } = [];

    private static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "KintsugiMidiPlayer");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return Path.Combine(dir, "settings.json");
    }

    private static string GetLegacyConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "MidiPlayer", "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            var path = GetConfigPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }

            var legacyPath = GetLegacyConfigPath();
            if (File.Exists(legacyPath))
            {
                var json = File.ReadAllText(legacyPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch
        {
            // Ignore load errors
        }
        return new AppSettings();
    }

    public MidiMixSettings? GetMidiMixSettings(string midiPath, out string midiKey)
    {
        midiKey = GetMidiMixSettingsKey(midiPath);
        if (string.IsNullOrEmpty(midiKey))
        {
            return null;
        }

        if (MidiMixSettingsByHash.TryGetValue(midiKey, out var settings))
        {
            return settings.Clone();
        }

        return null;
    }

    public string GetMidiMixSettingsKey(string midiPath)
    {
        var normalizedPath = NormalizeMidiPath(midiPath);
        if (string.IsNullOrEmpty(normalizedPath))
        {
            return string.Empty;
        }

        try
        {
            using var stream = File.OpenRead(normalizedPath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return string.Empty;
        }
    }

    public void SetMidiMixSettings(string midiKey, MidiMixSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(midiKey);
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();
        MidiMixSettingsByHash[NormalizeMidiHash(midiKey)] = settings.Clone();
    }

    public void Save()
    {
        try
        {
            var path = GetConfigPath();
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private void Normalize()
    {
        MidiMixSettingsByHash ??= [];
        var normalizedByHash = new Dictionary<string, MidiMixSettings>();
        foreach (var pair in MidiMixSettingsByHash)
        {
            var key = NormalizeMidiHash(pair.Key);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var settings = pair.Value ?? new MidiMixSettings();
            settings.Normalize();
            normalizedByHash[key] = settings;
        }

        MidiMixSettingsByHash = normalizedByHash;
    }

    private static string NormalizeMidiPath(string? midiPath)
    {
        if (string.IsNullOrWhiteSpace(midiPath))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.GetFullPath(midiPath);
            return OperatingSystem.IsWindows()
                ? fullPath.ToUpperInvariant()
                : fullPath;
        }
        catch
        {
            return midiPath;
        }
    }

    private static string NormalizeMidiHash(string? midiHash)
        => string.IsNullOrWhiteSpace(midiHash)
            ? string.Empty
            : midiHash.Trim().ToUpperInvariant();
}
