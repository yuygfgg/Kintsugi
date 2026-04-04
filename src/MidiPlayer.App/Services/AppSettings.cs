using System;
using System.IO;
using System.Text.Json;
using ManagedBass.Midi;

namespace MidiPlayer.App.Services;

public class AppSettings
{
    public string? SoundFontPath { get; set; }
    public MidiSystem SystemMode { get; set; } = MidiSystem.Default;
    public int SampleRate { get; set; } = 44100;

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
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }

            var legacyPath = GetLegacyConfigPath();
            if (File.Exists(legacyPath))
            {
                var json = File.ReadAllText(legacyPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Ignore load errors
        }
        return new AppSettings();
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
}
