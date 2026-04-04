# Kintsugi Midi Player

Kintsugi Midi Player is a desktop MIDI player built with Avalonia and BASS/BASSMIDI. It supports SoundFont-based playback, live visualizers, transport controls, and offline WAV export.

## Build

```bash
dotnet build src/MidiPlayer.App/MidiPlayer.App.csproj
```

On macOS, you can produce an `.app` bundle with:

```bash
scripts/publish-macos-app.sh
```
