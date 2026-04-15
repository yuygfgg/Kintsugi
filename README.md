# Kintsugi Midi Player

Kintsugi Midi Player is a desktop MIDI player built with Avalonia and BASS/BASSMIDI. It supports SoundFont-based playback, drag-and-drop MIDI or playlist loading, switchable live visualizers, an interactive EQ, a piano-roll waterfall view, transport controls, tempo override, global transpose, per-channel mixing, a live MIDI event browser, offline audio export, and a reorderable effect chain with the built-in EQ plus VST3 and macOS Audio Unit effects.

## Build

```bash
dotnet build src/MidiPlayer.App/MidiPlayer.App.csproj
```

For a raw self-contained publish directory for a specific desktop RID:

```bash
dotnet publish src/MidiPlayer.App/MidiPlayer.App.csproj -c Release -r <RID> --self-contained true
```

For end-user-friendly packages, use the platform packaging scripts instead:

```bash
scripts/publish-macos-app.sh
bash scripts/publish-portable-package.sh linux-x64
pwsh -File scripts/publish-windows-portable.ps1 -Rid win-x64
```

## License

Kintsugi is licensed under `AGPL-3.0-only`.
