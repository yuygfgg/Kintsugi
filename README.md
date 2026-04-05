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

## User Manual

Kintsugi Midi Player is a desktop MIDI player with SoundFont-based playback, live note and spectrum visualization, per-channel mixing, and offline WAV export.

### 1. Supported Files

- MIDI input: `.mid`, `.midi`, `.kar`, `.rmi`
- SoundFont input: `.sf2`, `.sfz`
- Export output: `.wav`

### 2. Quick Start

1. Open the app.
2. Click the gear button and load a SoundFont (`.sf2` or `.sfz`).
3. Click `OPEN FILE` and choose a MIDI file.
4. Use the transport controls at the bottom to play, pause, seek, or loop the track.
5. Click `EXPORT WAV` if you want to render the current track to a WAV file.

<!-- Insert screenshot: Main window with one MIDI file loaded and playback active. -->

### 3. Main Window Overview

The main window is divided into four areas.

#### Header

The header shows:

- The app title
- The current MIDI file name
- A status badge such as `Ready to play`, `Playing`, `Paused`, `Finished`, or an error message
- `OPEN FILE`
- `EXPORT WAV`
- The settings button

#### Channel Activity

The `CHANNEL ACTIVITY` strip shows 16 MIDI channels. Each block is one channel.

- Bright color: the channel is currently playing notes
- Dark gray: the channel is idle
- Very dark: the channel is muted

This strip is also the fastest way to mute, solo, and edit channels.

#### Visualizer

The center panel contains:

- A real-time spectrum analyzer
- A full-width piano keyboard that lights up with active notes

Colors match MIDI channels, so you can see which channel is playing which notes.

#### Transport Bar

The bottom bar contains:

- Play/Pause
- Current position and total length
- Seek slider
- `MIX` button for the global mixer
- Loop button
- Live BPM display

### 4. Playback Basics

#### Open a MIDI File

Click `OPEN FILE` and choose a MIDI file. The app loads the file immediately.

If a SoundFont is already loaded, playback starts automatically.

If no SoundFont is loaded yet, the file still loads, but playback will not start. Open `Settings`, load a SoundFont, then press Play.

#### Play and Pause

- Click the round Play/Pause button
- Press `Space`
- On macOS and Windows, system media controls can also control playback

If playback has already reached the end of the song, pressing Play starts again from the beginning.

#### Seek

Drag the seek slider to move to a different point in the song. The time readout updates as you move.

#### Loop

Click the loop button to toggle loop playback on or off.

When loop is off, the status changes to `Finished` when the song reaches the end.

### 5. Channel Activity and Per-Channel Mixer

Each channel block in `CHANNEL ACTIVITY` supports three actions:

- Single-click: mute or unmute that channel
- Double-click: solo that channel; double-click the same channel again to leave solo mode
- Right-click: open the mixer popup for that channel

<!-- Insert screenshot: Channel Activity strip with one channel muted, one soloed, and the channel mixer popup open. -->

#### Channel Mixer Controls

The channel mixer popup has three controls:

- `VOL`: channel volume
- `REV`: channel reverb send
- `CHO`: channel chorus send

#### Mode Labels

The `REV` and `CHO` rows have a mode button. Click it to cycle through these modes:

- `SCL`: scale the value from the MIDI file
- `ABS`: replace it with an absolute value
- `BIA`: add or subtract from the MIDI file value

The meaning of the slider changes with the mode:

- `VOL`: `0%` to `200%`
- `REV`/`CHO` in `SCL`: `0%` to `200%`
- `REV`/`CHO` in `ABS`: MIDI controller value `0` to `127`
- `REV`/`CHO` in `BIA`: offset `-127` to `+127`

#### Reset Shortcuts

Double-click a slider to reset it:

- `VOL` resets to `100%`
- `REV` and `CHO` reset according to the current mode

### 6. Global Mixer

Click `MIX` to open the global mixer.

The global mixer controls:

- `VOL`: master output level
- `REV`: global reverb return
- `CHO`: global chorus return

The `REV` and `CHO` mode buttons use the same labels as the channel mixer:

- `SCL`: scale the current return level
- `ABS`: use a fixed return level
- `BIA`: add or subtract from the current return level

Global return ranges are:

- `SCL`: `0%` to `200%`
- `ABS`: `0%` to `200%`
- `BIA`: `-200` to `+200`

Double-clicking the slider resets the current mode back to its default value.

Click outside the popup to close it.

### 7. Settings

Click the gear button to open `Player Settings`.

<!-- Insert screenshot: Settings window showing SoundFont, MIDI System Mode, and Output Sample Rate. -->

#### SoundFont

Use `Browse...` to load an `.sf2` or `.sfz` file.

This is required for:

- Playback
- WAV export

The loaded SoundFont is applied to the current MIDI file and remembered for later launches.

#### MIDI System Mode

Available modes:

- `Auto (Use MIDI File Default)`
- `GM1`
- `GM2`
- `XG`
- `GS`

Use this when a file was authored for a specific MIDI standard or when you want to override the file's own system mode behavior.

#### Output Sample Rate

Available sample rates:

- `44100 Hz`
- `48000 Hz`
- `88200 Hz`
- `96000 Hz`

### 8. Exporting WAV

Click `EXPORT WAV` to render the current MIDI file to a WAV file.

The export button is enabled only when a MIDI file is loaded.

If no SoundFont is loaded, export does not start. Load a SoundFont first.

<!-- Insert screenshot: Export WAV dialog with format and destination fields filled in. -->

#### Export Dialog

The export dialog lets you choose:

- Sample rate: `44100`, `48000`, `88200`, or `96000 Hz`
- Bit depth: `16-bit PCM`, `24-bit PCM`, or `32-bit Float`
- Destination path

By default, the app suggests a WAV file in the same folder as the MIDI file, using the same base name.

If that file already exists, the app suggests a numbered filename instead.

#### What the Export Uses

WAV export uses the current playback state for rendering:

- Current SoundFont
- Current MIDI system mode
- Current master mix
- Current reverb and chorus return settings
- Current per-channel volume, reverb, and chorus settings
- Current mute or solo state

#### During Export

While export is running:

- Opening another MIDI file is disabled
- Opening settings is disabled
- Closing the app is blocked until export finishes

The status badge shows export progress and completion messages.

### 9. Saved Preferences and Mix Recall

The app saves these settings automatically:

- Last selected SoundFont
- MIDI system mode
- Output sample rate
- Mix settings for each MIDI file path

Per-file mix recall includes:

- Master volume
- Global reverb and chorus return settings
- Per-channel volume
- Per-channel reverb and chorus settings

Mix recall is path-based. If you move or rename a MIDI file, the app treats it as a different file and starts with a fresh mix.

### 10. Troubleshooting

#### Playback does not start

Most often, no SoundFont is loaded. Open `Settings`, load an `.sf2` or `.sfz`, then press Play again.

#### Export WAV does not start

Check these points:

- A MIDI file is loaded
- A SoundFont is loaded
- The destination path is writable

#### The wrong mix loads for a file

Mix settings are stored by full file path. If the file was copied, moved, or renamed, it will not reuse the old mix entry.

#### Playback changed after switching sample rate

This is expected. The player rebuilds the audio engine when the sample rate changes.

### 11. Control Summary

| Action               | Result                          |
| -------------------- | ------------------------------- |
| `OPEN FILE`          | Load a MIDI file                |
| `EXPORT WAV`         | Render the current track to WAV |
| `Space`              | Play or pause                   |
| Channel single-click | Mute/unmute channel             |
| Channel double-click | Solo/unsolo channel             |
| Channel right-click  | Open per-channel mixer          |
| `MIX`                | Open global mixer               |
| Loop button          | Toggle looping                  |
| Slider double-click  | Reset that control              |

