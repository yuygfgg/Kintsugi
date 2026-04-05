using System.IO.Compression;
using System.Runtime.InteropServices;

var options = ParseArguments(args);
if (string.IsNullOrWhiteSpace(options.OutputDirectory))
{
    throw new ArgumentException("Missing required argument: --output <directory>.");
}

var rid = string.IsNullOrWhiteSpace(options.RuntimeIdentifier)
    ? DetectRuntimeIdentifier()
    : options.RuntimeIdentifier!;

var outputDirectory = Path.GetFullPath(options.OutputDirectory!);
Directory.CreateDirectory(outputDirectory);

var assets = ResolveAssets(rid);
var manifestPath = Path.Combine(outputDirectory, ".asset-manifest");
var manifest = string.Join(
    Environment.NewLine,
    assets.Select(asset => $"{asset.VersionStamp}|{asset.OutputFileName}|{asset.DownloadUrl}|{asset.ArchiveEntry}"));

if (File.Exists(manifestPath) &&
    string.Equals(File.ReadAllText(manifestPath), manifest, StringComparison.Ordinal) &&
    assets.All(asset => File.Exists(Path.Combine(outputDirectory, asset.OutputFileName))))
{
    Console.WriteLine($"Native assets are up to date for {rid}.");
    return;
}

var cacheDirectory = Path.Combine(outputDirectory, ".cache");
Directory.CreateDirectory(cacheDirectory);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("KintsugiMidiPlayerBuild/1.0");

foreach (var asset in assets)
{
    var archivePath = Path.Combine(cacheDirectory, $"{asset.VersionStamp}-{Path.GetFileName(asset.DownloadUrl)}");
    if (!File.Exists(archivePath))
    {
        Console.WriteLine($"Downloading {asset.DownloadUrl}...");
        await DownloadFileAsync(httpClient, asset.DownloadUrl, archivePath);
    }

    Console.WriteLine($"Extracting {asset.OutputFileName} for {rid}...");
    ExtractFile(archivePath, asset.ArchiveEntry, Path.Combine(outputDirectory, asset.OutputFileName));
}

File.WriteAllText(manifestPath, manifest);
Console.WriteLine($"Prepared native assets for {rid} in {outputDirectory}.");

static async Task DownloadFileAsync(HttpClient httpClient, string downloadUrl, string destinationPath)
{
    using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    await using var input = await response.Content.ReadAsStreamAsync();
    await using var output = File.Create(destinationPath);
    await input.CopyToAsync(output);
}

static void ExtractFile(string archivePath, string archiveEntryPath, string destinationPath)
{
    using var archive = ZipFile.OpenRead(archivePath);
    var normalizedEntry = archiveEntryPath.Replace('\\', '/');
    var entry = archive.GetEntry(normalizedEntry)
        ?? throw new FileNotFoundException($"Archive entry '{archiveEntryPath}' was not found in '{archivePath}'.");

    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

    var tempPath = destinationPath + ".tmp";
    if (File.Exists(tempPath))
    {
        File.Delete(tempPath);
    }

    entry.ExtractToFile(tempPath, overwrite: true);
    TrySetUnixPermissions(tempPath);
    File.Move(tempPath, destinationPath, overwrite: true);
}

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

static NativeAsset[] ResolveAssets(string rid)
{
    if (rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
    {
        return
        [
            new NativeAsset(
                VersionStamp: "bass24-osx-2026-01-02",
                DownloadUrl: "https://www.un4seen.com/files/bass24-osx.zip",
                ArchiveEntry: "libbass.dylib",
                OutputFileName: "libbass.dylib"),
            new NativeAsset(
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
            new NativeAsset(
                VersionStamp: "bass24-linux-2026-01-02",
                DownloadUrl: "https://www.un4seen.com/files/bass24-linux.zip",
                ArchiveEntry: bassPath,
                OutputFileName: "libbass.so"),
            new NativeAsset(
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
            new NativeAsset(
                VersionStamp: bassVersionStamp,
                DownloadUrl: bassUrl,
                ArchiveEntry: bassPath,
                OutputFileName: "bass.dll"),
            new NativeAsset(
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

sealed class Options
{
    public string? RuntimeIdentifier { get; set; }

    public string? OutputDirectory { get; set; }
}

sealed record NativeAsset(string VersionStamp, string DownloadUrl, string ArchiveEntry, string OutputFileName);
