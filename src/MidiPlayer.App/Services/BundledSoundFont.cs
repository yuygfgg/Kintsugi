using System;
using System.IO;

namespace MidiPlayer.App.Services;

public static class BundledSoundFont
{
    public const string DirectoryName = "SoundFonts";
    public const string FileName = "FluidR3_GM.sf2";

    public static string GetDefaultPath()
        => Path.Combine(AppContext.BaseDirectory, DirectoryName, FileName);

    public static string? ResolvePreferredPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var bundledPath = GetDefaultPath();
        return File.Exists(bundledPath) ? bundledPath : null;
    }

    public static bool IsBundledDefault(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var left = Path.GetFullPath(path);
            var right = Path.GetFullPath(GetDefaultPath());
            return OperatingSystem.IsWindows()
                ? string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
                : string.Equals(left, right, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
