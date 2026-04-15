param(
    [string]$Rid = ""
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$project = Join-Path $rootDir "src/MidiPlayer.App/MidiPlayer.App.csproj"
$publishRoot = Join-Path $rootDir "artifacts/publish"
$packageRootBase = Join-Path $rootDir "artifacts/package"
$distRoot = Join-Path $rootDir "artifacts/dist"
$friendlyExecutableName = "Kintsugi Midi Player.exe"

if ([string]::IsNullOrWhiteSpace($Rid)) {
    switch ($env:PROCESSOR_ARCHITECTURE) {
        "ARM64" { $Rid = "win-arm64" }
        "AMD64" { $Rid = "win-x64" }
        "x86" { $Rid = "win-x86" }
        default { throw "Unsupported Windows architecture: $($env:PROCESSOR_ARCHITECTURE)" }
    }
}

if (-not $Rid.StartsWith("win", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Use this script only for Windows RIDs. Received: $Rid"
}

$publishDir = Join-Path $publishRoot $Rid
$packageName = "Kintsugi.MidiPlayer-$Rid-portable"
$packageDir = Join-Path $packageRootBase $packageName
$archivePath = Join-Path $distRoot ($packageName + ".zip")
$sourceExecutablePath = Join-Path $packageDir "Kintsugi.MidiPlayer.exe"
$targetExecutablePath = Join-Path $packageDir $friendlyExecutableName

dotnet publish $project `
    -c Release `
    -r $Rid `
    -o $publishDir `
    --self-contained true `
    -p:CreateMacAppBundle=false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
New-Item -ItemType Directory -Force -Path $distRoot | Out-Null

Copy-Item (Join-Path $publishDir "*") $packageDir -Recurse -Force

if (-not (Test-Path $sourceExecutablePath)) {
    throw "Expected publish output 'Kintsugi.MidiPlayer.exe' was not found in $publishDir"
}

Rename-Item $sourceExecutablePath $friendlyExecutableName

Copy-Item (Join-Path $rootDir "README.md") $packageDir -Force
Copy-Item (Join-Path $rootDir "LICENSE") $packageDir -Force

if (Test-Path $archivePath) {
    Remove-Item $archivePath -Force
}

if (Test-Path ($archivePath + ".sha256")) {
    Remove-Item ($archivePath + ".sha256") -Force
}

Compress-Archive -Path $packageDir -DestinationPath $archivePath -Force

$hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path ($archivePath + ".sha256") -Value "$hash *$(Split-Path $archivePath -Leaf)"

Write-Host ""
Write-Host "Portable package created:"
Write-Host "  $archivePath"
Write-Host ""
Write-Host "SHA-256 checksum:"
Write-Host "  $($archivePath).sha256"
Write-Host ""
Write-Host "Package contents:"
Write-Host "  $packageDir"
