# Kintsugi Midi Player

Kintsugi Midi Player is a desktop MIDI player built with Avalonia and BASS/BASSMIDI. It supports SoundFont-based playback, drag-and-drop MIDI loading, live visualizers, transport controls, tempo override, global transpose, per-channel mixing, and offline WAV export.

## Build

```bash
dotnet build src/MidiPlayer.App/MidiPlayer.App.csproj
```

On macOS, you can produce an `.app` bundle with:

```bash
scripts/publish-macos-app.sh
```

## User Manual

Kintsugi Midi Player is a desktop MIDI player with SoundFont-based playback, drag-and-drop MIDI loading, live note and spectrum visualization, per-channel mixing, tempo override, global transpose, and offline WAV export.

### 1. Supported Files

- MIDI input: `.mid`, `.midi`, `.kar`, `.rmi`
- SoundFont input: `.sf2`, `.sfz`
- Export output: `.wav`

### 2. Quick Start

1. Open the app.
2. Click the gear button and load a SoundFont (`.sf2` or `.sfz`).
3. Click `OPEN FILE` and choose a MIDI file, or drag a MIDI file into the window.
4. Use the transport controls at the bottom to play, pause, seek, or loop the track.
5. Click `EXPORT WAV` if you want to render the current track to a WAV file.

<img width="1512" height="982" alt="截屏2026-04-05 12 48 38 1" src="https://github.com/user-attachments/assets/d37db25c-5444-4a5a-ba24-bb801367df2d" />

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

Hovering a channel shows the current instrument or drum kit name for that channel.

#### Visualizer

The center panel contains:

- A real-time spectrum analyzer
- A piano keyboard that lights up with active notes
- `KEY -` and `KEY +` buttons for global transpose in semitones

Colors match MIDI channels, so you can see which channel is playing which notes. When transpose is active, the piano display shifts with it.

#### Transport Bar

The bottom bar contains:

- Play/Pause
- Current position and total length
- Seek slider
- `MIX` button for the global mixer
- Loop button
- Live BPM display
- Clickable BPM popup for tempo override

### 4. Playback Basics

#### Open a MIDI File

Click `OPEN FILE` and choose a MIDI file, or drag a MIDI file into the window. The app loads the file immediately.

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

To loop only a short section instead of the full track:

1. Hold `Shift` and drag on the seek timeline to create an A-B range.
2. Release the pointer to enable looping for that selected range immediately.
3. Drag inside the highlighted range to move it, or drag either edge to resize it.
4. Click `CLEAR` to remove the A-B range and return to full-track playback.

You can also turn loop off while keeping the A-B range selected, then click the loop button again to resume looping that same section.

#### Tempo Override

Click the `BPM` badge to open the tempo override popup.

<img width="241" height="146" alt="截屏2026-04-05 12 49 50" src="https://github.com/user-attachments/assets/235d437c-47f9-4935-854e-781069b8409d" />

- Range: `25%` to `400%`
- `100%` keeps the MIDI file's original tempo map
- The live BPM display reflects the effective playback tempo after the override

Double-click the popup slider to reset it to `100%`.

#### Global Transpose

Use the `KEY -` and `KEY +` buttons on the left and right sides of the piano keyboard to transpose playback in semitones.

- Range: `-24` to `+24` semitones
- The transpose readout is shown above the piano keyboard
- The piano note display moves with the transpose setting

### 5. Channel Activity and Per-Channel Mixer

Each channel block in `CHANNEL ACTIVITY` supports three actions:

- Single-click: mute or unmute that channel
- Double-click: solo that channel; double-click the same channel again to leave solo mode
- Right-click: open the mixer popup for that channel
- Hover: show the current instrument or drum kit name for that channel

The hover label follows the channel's current MIDI state, so it updates when the file switches program, bank, or drum mode.

<img width="254" height="250" alt="截屏2026-04-05 10 56 20" src="https://github.com/user-attachments/assets/aa13da1f-ae85-43fa-a3f8-9dbf8a71fd08" />

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

<img width="247" height="189" alt="截屏2026-04-05 11 01 15" src="https://github.com/user-attachments/assets/b2e80ffc-1689-4402-a6ed-72193574081a" />

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

<img width="439" height="480" alt="截屏2026-04-05 10 57 25" src="https://github.com/user-attachments/assets/871a6cec-16e9-42f7-a1fe-f5b7f32534e6" />

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

<img width="480" height="402" alt="截屏2026-04-05 10 57 45" src="https://github.com/user-attachments/assets/e3af57d5-43e3-4f10-b394-b35eba11042f" />

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
- Current tempo override
- Current global transpose
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

- Tempo override
- Global transpose
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
| Channel hover        | Show channel instrument name    |
| `MIX`                | Open global mixer               |
| Loop button          | Toggle looping                  |
| `BPM`                | Open tempo override popup       |
| `KEY -` / `KEY +`    | Transpose down or up            |
| Slider double-click  | Reset that control              |
