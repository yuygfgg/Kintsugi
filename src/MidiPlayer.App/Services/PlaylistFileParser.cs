using System;
using System.Collections.Generic;
using System.IO;

namespace MidiPlayer.App.Services;

public static class PlaylistFileParser
{
    private static readonly string[] MidiExtensions = [".mid", ".midi", ".kar", ".rmi"];
    private static readonly string[] PlaylistExtensions = [".m3u", ".m3u8", ".pls"];

    public static IReadOnlyList<string> SupportedMidiExtensions => MidiExtensions;

    public static IReadOnlyList<string> SupportedPlaylistExtensions => PlaylistExtensions;

    public static bool IsSupportedMidiPath(string? path)
        => HasSupportedExtension(path, MidiExtensions);

    public static bool IsSupportedPlaylistPath(string? path)
        => HasSupportedExtension(path, PlaylistExtensions);

    public static IReadOnlyList<string> ParseLocalMidiEntries(string playlistPath)
    {
        if (string.IsNullOrWhiteSpace(playlistPath))
        {
            throw new ArgumentException("Playlist path is required.", nameof(playlistPath));
        }

        string fullPlaylistPath = Path.GetFullPath(playlistPath);
        if (!File.Exists(fullPlaylistPath))
        {
            throw new FileNotFoundException("Playlist file not found.", fullPlaylistPath);
        }

        if (!IsSupportedPlaylistPath(fullPlaylistPath))
        {
            throw new NotSupportedException("Unsupported playlist format.");
        }

        string baseDirectory = Path.GetDirectoryName(fullPlaylistPath) ?? Directory.GetCurrentDirectory();
        string extension = Path.GetExtension(fullPlaylistPath);
        IEnumerable<string> entries = string.Equals(extension, ".pls", StringComparison.OrdinalIgnoreCase)
            ? EnumeratePlsEntries(fullPlaylistPath)
            : EnumerateM3uEntries(fullPlaylistPath);

        var resolvedPaths = new List<string>();
        foreach (string entry in entries)
        {
            if (!TryResolveLocalPath(entry, baseDirectory, out string? localPath))
            {
                continue;
            }

            if (!IsSupportedMidiPath(localPath) || !File.Exists(localPath))
            {
                continue;
            }

            resolvedPaths.Add(Path.GetFullPath(localPath));
        }

        return resolvedPaths;
    }

    private static IEnumerable<string> EnumerateM3uEntries(string playlistPath)
    {
        foreach (string rawLine in File.ReadLines(playlistPath))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            yield return line;
        }
    }

    private static IEnumerable<string> EnumeratePlsEntries(string playlistPath)
    {
        foreach (string rawLine in File.ReadLines(playlistPath))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith(";", StringComparison.Ordinal) ||
                line.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            if (!key.StartsWith("File", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static bool TryResolveLocalPath(string rawEntry, string baseDirectory, out string? localPath)
    {
        localPath = null;
        if (string.IsNullOrWhiteSpace(rawEntry))
        {
            return false;
        }

        string entry = rawEntry.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        if (Uri.TryCreate(entry, UriKind.Absolute, out Uri? uri))
        {
            if (!uri.IsFile)
            {
                return false;
            }

            localPath = Path.GetFullPath(uri.LocalPath);
            return true;
        }

        localPath = Path.GetFullPath(Path.IsPathRooted(entry) ? entry : Path.Combine(baseDirectory, entry));
        return true;
    }

    private static bool HasSupportedExtension(string? path, IReadOnlyList<string> supportedExtensions)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        foreach (string supportedExtension in supportedExtensions)
        {
            if (string.Equals(extension, supportedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
