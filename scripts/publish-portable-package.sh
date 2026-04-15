#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/src/MidiPlayer.App/MidiPlayer.App.csproj"
PUBLISH_ROOT="$ROOT_DIR/artifacts/publish"
PACKAGE_ROOT_BASE="$ROOT_DIR/artifacts/package"
DIST_ROOT="$ROOT_DIR/artifacts/dist"
FRIENDLY_EXECUTABLE_NAME="Kintsugi Midi Player"

RID="${1:-}"
if [[ -z "$RID" ]]; then
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) RID="linux-x64" ;;
    Linux-aarch64|Linux-arm64) RID="linux-arm64" ;;
    Linux-armv7l) RID="linux-arm" ;;
    Darwin-arm64) RID="osx-arm64" ;;
    Darwin-x86_64) RID="osx-x64" ;;
    *)
      echo "Unsupported host platform: $(uname -s)-$(uname -m)" >&2
      exit 1
      ;;
  esac
fi

case "$RID" in
  win*)
    echo "Use scripts/publish-windows-portable.ps1 for Windows portable packages." >&2
    exit 1
    ;;
esac

PUBLISH_DIR="$PUBLISH_ROOT/$RID"
PACKAGE_NAME="Kintsugi.MidiPlayer-$RID-portable"
PACKAGE_DIR="$PACKAGE_ROOT_BASE/$PACKAGE_NAME"
ARCHIVE_PATH="$DIST_ROOT/$PACKAGE_NAME.tar.gz"
SOURCE_EXECUTABLE_NAME="Kintsugi.MidiPlayer"
TARGET_EXECUTABLE_PATH="$PACKAGE_DIR/$FRIENDLY_EXECUTABLE_NAME"

dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  -o "$PUBLISH_DIR" \
  --self-contained true \
  -p:CreateMacAppBundle=false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false

rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR" "$DIST_ROOT"

cp -a "$PUBLISH_DIR/." "$PACKAGE_DIR/"
find "$PACKAGE_DIR" -name '.DS_Store' -delete

if [[ ! -f "$PACKAGE_DIR/$SOURCE_EXECUTABLE_NAME" ]]; then
  echo "Expected publish output '$SOURCE_EXECUTABLE_NAME' was not found in $PUBLISH_DIR" >&2
  exit 1
fi

mv "$PACKAGE_DIR/$SOURCE_EXECUTABLE_NAME" "$TARGET_EXECUTABLE_PATH"
chmod +x "$TARGET_EXECUTABLE_PATH"

cp "$ROOT_DIR/README.md" "$PACKAGE_DIR/"
cp "$ROOT_DIR/LICENSE" "$PACKAGE_DIR/"

rm -f "$ARCHIVE_PATH" "$ARCHIVE_PATH.sha256"
tar -C "$PACKAGE_ROOT_BASE" -czf "$ARCHIVE_PATH" "$PACKAGE_NAME"
shasum -a 256 "$ARCHIVE_PATH" > "$ARCHIVE_PATH.sha256"

echo
echo "Portable package created:"
echo "  $ARCHIVE_PATH"
echo
echo "SHA-256 checksum:"
echo "  $ARCHIVE_PATH.sha256"
echo
echo "Package contents:"
echo "  $PACKAGE_DIR"
