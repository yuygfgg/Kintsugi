#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/src/MidiPlayer.App/MidiPlayer.App.csproj"
PUBLISH_ROOT="$ROOT_DIR/artifacts/publish"

RID="${1:-}"
if [[ -z "$RID" ]]; then
  case "$(uname -m)" in
    arm64) RID="osx-arm64" ;;
    x86_64) RID="osx-x64" ;;
    *)
      echo "Unsupported macOS architecture: $(uname -m)" >&2
      exit 1
      ;;
  esac
fi

PUBLISH_DIR="$PUBLISH_ROOT/$RID"

dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  -o "$PUBLISH_DIR" \
  -p:CreateMacAppBundle=true \
  --self-contained true

echo
echo "Published files created under:"
echo "  $PUBLISH_DIR"
echo
echo "App bundle created under:"
echo "  $ROOT_DIR/artifacts/macos/Release/$RID/Kintsugi Midi Player.app"
