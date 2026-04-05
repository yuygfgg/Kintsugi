using System.IO.Compression;
using System.Runtime.InteropServices;

var options = ParseArguments(args);
if (string.IsNullOrWhiteSpace(options.OutputDirectory))
{
    throw new ArgumentException("Missing required argument: --output <directory>.");
}

var rid = string.IsNullOrWhiteSpace(options.RuntimeIdentifier)
    ? options.AssetSet == AssetSet.Native ? DetectRuntimeIdentifier() : string.Empty
    : options.RuntimeIdentifier!;

var outputDirectory = Path.GetFullPath(options.OutputDirectory!);
Directory.CreateDirectory(outputDirectory);

var assets = ResolveAssets(options.AssetSet, rid);
var assetSetDisplayName = GetAssetSetDisplayName(options.AssetSet);
var manifestPath = Path.Combine(outputDirectory, ".asset-manifest");
var manifest = string.Join(
    Environment.NewLine,
    assets.Select(asset => $"{asset.VersionStamp}|{asset.OutputFileName}|{asset.DownloadUrl}|{asset.ArchiveEntry ?? string.Empty}"));

if (File.Exists(manifestPath) &&
    string.Equals(File.ReadAllText(manifestPath), manifest, StringComparison.Ordinal) &&
    assets.All(asset => File.Exists(Path.Combine(outputDirectory, asset.OutputFileName))))
{
    Console.WriteLine(
        string.IsNullOrWhiteSpace(rid)
            ? $"{assetSetDisplayName} are up to date."
            : $"{assetSetDisplayName} are up to date for {rid}.");
    return;
}

var cacheDirectory = Path.Combine(outputDirectory, ".cache");
Directory.CreateDirectory(cacheDirectory);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("KintsugiMidiPlayerBuild/1.0");

foreach (var asset in assets)
{
    var downloadedAssetPath = Path.Combine(cacheDirectory, $"{asset.VersionStamp}-{Path.GetFileName(asset.DownloadUrl)}");
    if (!File.Exists(downloadedAssetPath))
    {
        Console.WriteLine($"Downloading {asset.DownloadUrl}...");
        await DownloadFileAsync(httpClient, asset.DownloadUrl, downloadedAssetPath);
    }

    var action = string.IsNullOrWhiteSpace(asset.ArchiveEntry) ? "Copying" : "Extracting";
    Console.WriteLine(
        string.IsNullOrWhiteSpace(rid)
            ? $"{action} {asset.OutputFileName}..."
            : $"{action} {asset.OutputFileName} for {rid}...");
    MaterializeAsset(downloadedAssetPath, asset, Path.Combine(outputDirectory, asset.OutputFileName));
}

DeleteStaleAssetFiles(outputDirectory, cacheDirectory, manifestPath, assets);

File.WriteAllText(manifestPath, manifest);
Console.WriteLine(
    string.IsNullOrWhiteSpace(rid)
        ? $"Prepared {assetSetDisplayName.ToLowerInvariant()} in {outputDirectory}."
        : $"Prepared {assetSetDisplayName.ToLowerInvariant()} for {rid} in {outputDirectory}.");

static async Task DownloadFileAsync(HttpClient httpClient, string downloadUrl, string destinationPath)
{
    using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    await using var input = await response.Content.ReadAsStreamAsync();
    await using var output = File.Create(destinationPath);
    await input.CopyToAsync(output);
}

static void MaterializeAsset(string downloadedAssetPath, DownloadAsset asset, string destinationPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

    var tempPath = destinationPath + ".tmp";
    if (File.Exists(tempPath))
    {
        File.Delete(tempPath);
    }

    if (string.IsNullOrWhiteSpace(asset.ArchiveEntry))
    {
        File.Copy(downloadedAssetPath, tempPath, overwrite: true);
    }
    else
    {
        ExtractZipEntry(downloadedAssetPath, asset.ArchiveEntry, tempPath);
    }

    TrySetUnixPermissions(tempPath);
    File.Move(tempPath, destinationPath, overwrite: true);
}

static void ExtractZipEntry(string archivePath, string archiveEntryPath, string destinationPath)
{
    using var archive = ZipFile.OpenRead(archivePath);
    var normalizedEntry = archiveEntryPath.Replace('\\', '/');
    var entry = archive.GetEntry(normalizedEntry)
        ?? throw new FileNotFoundException($"Archive entry '{archiveEntryPath}' was not found in '{archivePath}'.");

    entry.ExtractToFile(destinationPath, overwrite: true);
}

static void DeleteStaleAssetFiles(string outputDirectory, string cacheDirectory, string manifestPath, DownloadAsset[] assets)
{
    var expectedFiles = assets
        .Select(asset => Path.GetFullPath(Path.Combine(outputDirectory, asset.OutputFileName)))
        .ToHashSet(GetPathComparer());

    foreach (var existingFile in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
    {
        var fullPath = Path.GetFullPath(existingFile);
        if (string.Equals(fullPath, manifestPath, GetPathComparison()))
        {
            continue;
        }

        if (IsPathInsideDirectory(fullPath, cacheDirectory))
        {
            continue;
        }

        if (!expectedFiles.Contains(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    DeleteEmptyDirectories(outputDirectory, cacheDirectory);
}

static void DeleteEmptyDirectories(string rootDirectory, string cacheDirectory)
{
    foreach (var directory in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
        .OrderByDescending(path => path.Length))
    {
        var fullPath = Path.GetFullPath(directory);
        if (string.Equals(fullPath, Path.GetFullPath(cacheDirectory), GetPathComparison()))
        {
            continue;
        }

        if (!Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            Directory.Delete(fullPath);
        }
    }
}

static bool IsPathInsideDirectory(string path, string directory)
{
    var normalizedPath = Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var normalizedDirectory = Path.GetFullPath(directory)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    return normalizedPath.StartsWith(normalizedDirectory, GetPathComparison());
}

static StringComparer GetPathComparer()
    => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

static StringComparison GetPathComparison()
    => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

static void TrySetUnixPermissions(string path)
{
    if (OperatingSystem.IsWindows())
    {
        return;
    }

    try
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
    catch
    {
        // Best effort only.
    }
}

static DownloadAsset[] ResolveAssets(AssetSet assetSet, string rid)
    => assetSet switch
    {
        AssetSet.Native => ResolveNativeAssets(rid),
        AssetSet.Bundled => ResolveBundledAssets(),
        _ => throw new NotSupportedException($"Unsupported asset set: {assetSet}.")
    };

static string GetAssetSetDisplayName(AssetSet assetSet)
    => assetSet switch
    {
        AssetSet.Native => "Native assets",
        AssetSet.Bundled => "Bundled assets",
        _ => "Assets"
    };

static DownloadAsset[] ResolveBundledAssets()
{
    return
    [
        new DownloadAsset(
            VersionStamp: "generaluser-gs-sf2-2.0.3",
            DownloadUrl: "https://raw.githubusercontent.com/mrbumpy409/GeneralUser-GS/main/GeneralUser-GS.sf2",
            OutputFileName: "SoundFonts/GeneralUser-GS.sf2"),
        new DownloadAsset(
            VersionStamp: "generaluser-gs-readme-2.0.3",
            DownloadUrl: "https://raw.githubusercontent.com/mrbumpy409/GeneralUser-GS/main/README.md",
            OutputFileName: "SoundFonts/GeneralUser-GS.README.md"),
        new DownloadAsset(
            VersionStamp: "generaluser-gs-license-2.0.3",
            DownloadUrl: "https://raw.githubusercontent.com/mrbumpy409/GeneralUser-GS/main/documentation/LICENSE.txt",
            OutputFileName: "SoundFonts/GeneralUser-GS.LICENSE.txt")
    ];
}

static DownloadAsset[] ResolveNativeAssets(string rid)
{
    if (rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
    {
        return
        [
            new DownloadAsset(
                VersionStamp: "bass24-osx-2026-01-02",
                DownloadUrl: "https://www.un4seen.com/files/bass24-osx.zip",
                ArchiveEntry: "libbass.dylib",
                OutputFileName: "libbass.dylib"),
            new DownloadAsset(
                VersionStamp: "bassmidi24-osx-2025-10-28",
                DownloadUrl: "https://www.un4seen.com/files/bassmidi24-osx.zip",
                ArchiveEntry: "libbassmidi.dylib",
                OutputFileName: "libbassmidi.dylib")
        ];
    }

    if (rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase))
    {
        var architecture = NormalizeArchitecture(rid);
        var bassPath = architecture switch
        {
            "x64" => "libs/x86_64/libbass.so",
            "arm64" => "libs/aarch64/libbass.so",
            "arm" => "libs/armhf/libbass.so",
            "x86" => "libs/x86/libbass.so",
            _ => throw new NotSupportedException($"Unsupported Linux architecture for BASS native assets: {architecture}.")
        };

        var bassMidiPath = architecture switch
        {
            "x64" => "libs/x86_64/libbassmidi.so",
            "arm64" => "libs/aarch64/libbassmidi.so",
            "arm" => "libs/armhf/libbassmidi.so",
            "x86" => "libs/x86/libbassmidi.so",
            _ => throw new NotSupportedException($"Unsupported Linux architecture for BASSMIDI native assets: {architecture}.")
        };

        return
        [
            new DownloadAsset(
                VersionStamp: "bass24-linux-2026-01-02",
                DownloadUrl: "https://www.un4seen.com/files/bass24-linux.zip",
                ArchiveEntry: bassPath,
                OutputFileName: "libbass.so"),
            new DownloadAsset(
                VersionStamp: "bassmidi24-linux-2025-10-28",
                DownloadUrl: "https://www.un4seen.com/files/bassmidi24-linux.zip",
                ArchiveEntry: bassMidiPath,
                OutputFileName: "libbassmidi.so")
        ];
    }

    if (rid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
    {
        var architecture = NormalizeArchitecture(rid);
        var bassUrl = architecture switch
        {
            "x64" => "https://www.un4seen.com/files/bass24.zip",
            "x86" => "https://www.un4seen.com/files/bass24.zip",
            "arm64" => "https://www.un4seen.com/files/bass24-arm64.zip",
            _ => throw new NotSupportedException($"Unsupported Windows architecture for BASS native assets: {architecture}.")
        };

        var bassPath = architecture switch
        {
            "x64" => "x64/bass.dll",
            "x86" => "bass.dll",
            "arm64" => "arm64/bass.dll",
            _ => throw new NotSupportedException($"Unsupported Windows architecture for BASS native assets: {architecture}.")
        };

        var bassMidiUrl = architecture switch
        {
            "x64" => "https://www.un4seen.com/files/bassmidi24.zip",
            "x86" => "https://www.un4seen.com/files/bassmidi24.zip",
            "arm64" => "https://www.un4seen.com/files/bass24-arm64.zip",
            _ => throw new NotSupportedException($"Unsupported Windows architecture for BASSMIDI native assets: {architecture}.")
        };

        var bassMidiPath = architecture switch
        {
            "x64" => "x64/bassmidi.dll",
            "x86" => "bassmidi.dll",
            "arm64" => "arm64/bassmidi.dll",
            _ => throw new NotSupportedException($"Unsupported Windows architecture for BASSMIDI native assets: {architecture}.")
        };

        var bassVersionStamp = architecture switch
        {
            "x64" => "bass24-windows-2026-01-16",
            "x86" => "bass24-windows-2026-01-16",
            "arm64" => "bass24-windows-arm64-2025-12-16",
            _ => throw new NotSupportedException($"Unsupported Windows architecture for BASS native assets: {architecture}.")
        };

        var bassMidiVersionStamp = architecture switch
        {
            "x64" => "bassmidi24-windows-2025-10-28",
            "x86" => "bassmidi24-windows-2025-10-28",
            "arm64" => "bassmidi24-windows-arm64-2024-11-01",
            _ => throw new NotSupportedException($"Unsupported Windows architecture for BASSMIDI native assets: {architecture}.")
        };

        return
        [
            new DownloadAsset(
                VersionStamp: bassVersionStamp,
                DownloadUrl: bassUrl,
                ArchiveEntry: bassPath,
                OutputFileName: "bass.dll"),
            new DownloadAsset(
                VersionStamp: bassMidiVersionStamp,
                DownloadUrl: bassMidiUrl,
                ArchiveEntry: bassMidiPath,
                OutputFileName: "bassmidi.dll")
        ];
    }

    throw new NotSupportedException($"Unsupported runtime identifier: {rid}.");
}

static string NormalizeArchitecture(string rid)
{
    var segments = rid.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (segments.Length == 1)
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => throw new NotSupportedException($"Unsupported process architecture: {RuntimeInformation.ProcessArchitecture}.")
        };
    }

    var architecture = segments[^1].ToLowerInvariant();
    return architecture switch
    {
        "x64" => "x64",
        "x86" => "x86",
        "arm64" => "arm64",
        "arm" => "arm",
        _ => throw new NotSupportedException($"Unsupported architecture segment in runtime identifier '{rid}'.")
    };
}

static string DetectRuntimeIdentifier()
{
    if (OperatingSystem.IsMacOS())
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "osx-arm64",
            Architecture.X64 => "osx-x64",
            _ => throw new NotSupportedException($"Unsupported macOS architecture: {RuntimeInformation.ProcessArchitecture}.")
        };
    }

    if (OperatingSystem.IsLinux())
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.X86 => "linux-x86",
            Architecture.Arm64 => "linux-arm64",
            Architecture.Arm => "linux-arm",
            _ => throw new NotSupportedException($"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}.")
        };
    }

    if (OperatingSystem.IsWindows())
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => throw new NotSupportedException($"Unsupported Windows architecture: {RuntimeInformation.ProcessArchitecture}.")
        };
    }

    throw new NotSupportedException($"Unsupported operating system for native asset download: {RuntimeInformation.OSDescription}.");
}

static Options ParseArguments(string[] arguments)
{
    var options = new Options();

    for (int i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--rid":
                options.RuntimeIdentifier = RequireValue(arguments, ref i, "--rid");
                break;
            case "--output":
                options.OutputDirectory = RequireValue(arguments, ref i, "--output");
                break;
            case "--asset-set":
                options.AssetSet = ParseAssetSet(RequireValue(arguments, ref i, "--asset-set"));
                break;
            default:
                throw new ArgumentException($"Unknown argument: {arguments[i]}");
        }
    }

    return options;
}

static string RequireValue(string[] arguments, ref int index, string option)
{
    if (index + 1 >= arguments.Length)
    {
        throw new ArgumentException($"Missing value for {option}.");
    }

    index++;
    return arguments[index];
}

static AssetSet ParseAssetSet(string value)
    => value.ToLowerInvariant() switch
    {
        "native" => AssetSet.Native,
        "bundled" => AssetSet.Bundled,
        _ => throw new ArgumentException($"Unsupported asset set: {value}.")
    };

sealed class Options
{
    public string? RuntimeIdentifier { get; set; }

    public string? OutputDirectory { get; set; }

    public AssetSet AssetSet { get; set; } = AssetSet.Native;
}

enum AssetSet
{
    Native,
    Bundled
}

sealed record DownloadAsset(string VersionStamp, string DownloadUrl, string OutputFileName, string? ArchiveEntry = null);
