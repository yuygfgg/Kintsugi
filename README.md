# Kintsugi Midi Player

Kintsugi Midi Player is a desktop MIDI player built with Avalonia and BASS/BASSMIDI. It supports SoundFont-based playback, drag-and-drop MIDI loading, switchable live visualizers, an interactive EQ, a piano-roll waterfall view, transport controls, tempo override, global transpose, per-channel mixing, a live MIDI event browser, and offline audio export.

## Build

```bash
dotnet build src/MidiPlayer.App/MidiPlayer.App.csproj
```

For a self-contained release package for a specific desktop RID:

```bash
dotnet publish src/MidiPlayer.App/MidiPlayer.App.csproj -c Release -r <RID> --self-contained true
```

On macOS, you can produce an `.app` bundle with:

```bash
scripts/publish-macos-app.sh
```

This bundle includes the native BASS/BASSMIDI/BASSenc libraries needed for playback plus `WAV`, `FLAC`, and `Opus` export on macOS.

## User Manual

### 1. Supported Files

- MIDI input: `.mid`, `.midi`, `.kar`, `.rmi`
- SoundFont input: `.sf2`, `.sfz`
- Export output: `.wav`, `.flac`, `.opus`

### 2. Quick Start

1. Open the app.
2. Open a MIDI file. The bundled `GeneralUser-GS.sf2` default SoundFont loads automatically in packaged builds. All other MIDI files in the same directory will automatically be imported into the Playlist.
3. Optional: click the gear button and replace it with your own SoundFont (`.sf2` or `.sfz`).
4. Use the transport controls at the bottom to play, pause, seek, or loop the track.
5. Click `EVENTS` if you want to inspect the current MIDI file's raw event stream while it plays.
6. Click `EXPORT AUDIO` if you want to render the current track to an audio file.

### 3. Main Window Overview

<img width="1512" height="982" alt="截屏2026-04-05 22 18 55" src="https://github.com/user-attachments/assets/0bf8d6f6-505e-4de3-bea9-3fdf5dc4a544" />

The main window is divided into four primary areas, along with a slide-out drawer.

#### Header

The header shows:

- The app title
- The current MIDI file name
- A status badge such as `Ready to play`, `Playing`, `Paused`, `Finished`, or an error message
- `OPEN FILE`
- `EXPORT AUDIO`
- `EVENTS`
- The settings button

#### Event Browser

Click `EVENTS` in the header to open the MIDI Event Browser for the currently loaded file.

<img width="1512" height="982" alt="截屏2026-04-06 00 17 09" src="https://github.com/user-attachments/assets/b46d84b4-382a-4f17-a4d4-52e4e90e207d" />

The Event Browser opens as a separate window, so it can stay visible while you continue using the main player window.

It shows:

- Track number
- Musical position
- Absolute time
- Event type
- MIDI channel
- Event number or controller/program identifier
- Event value
- A readable summary

While playback is running, the Event Browser follows the current playback position automatically:

- It scrolls to the most recent event at or before the current playback tick
- It highlights currently effective rows
- Channel-colored rows use the same per-channel color palette as the `CHANNEL ACTIVITY` strip

#### Channel Activity

The `CHANNEL ACTIVITY` strip shows 16 MIDI channels. Each block is one channel.

- Bright color: the channel is currently playing notes
- Dark gray: the channel is idle
- Very dark: the channel is muted

This strip is also the fastest way to mute, solo, and edit channels.

Hovering a channel shows the current instrument or drum kit name for that channel.

#### Visualizer

The center panel contains:

- A switchable `EQ` / `ROLL` visualizer header
- An `EQ` view with a real-time spectrum analyzer and integrated EQ curve
- A `ROLL` view with a piano-roll waterfall made of channel-colored falling note blocks
- A piano keyboard that lights up with active notes
- `KEY -` and `KEY +` buttons for global transpose in semitones

Colors match MIDI channels, so you can see which channel is playing which notes. In `ROLL`, the falling blocks use the same key geometry as the piano below it, so note lanes line up exactly with the keyboard. When transpose is active, both the piano-roll lanes and the piano display shift with it. EQ changes are reflected immediately in both live playback and the analyzer background.

#### Playlist Drawer

Click the `◀` tab located on the right edge of the main window to slide out the playlist panel.
- It automatically imports all valid MIDI files from the directory of the last opened file.
- Track durations are parsed instantly in the background.
- Click `↓ A-Z` or `↑ Z-A` to toggle alphabetical sorting.
- Click `▶` in the drawer's header to cleanly retract it.

#### Transport Bar

The bottom bar contains:

- Previous and Next track controls
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

Playback starts automatically when the bundled `GeneralUser-GS.sf2` or a previously selected custom SoundFont is available.

If neither the bundled default nor a custom SoundFont can be loaded, the file still loads, but playback will not start. Open `Settings`, load a SoundFont, then press Play.

#### Play and Pause

- Click the round Play/Pause button
- Press `Space`
- On macOS and Windows, system media controls can also control playback

If playback has already reached the end of the song, pressing Play starts again from the beginning. If the playlist has multiple items, the player will automatically advance to the next track upon finishing.

#### Track Navigation

- Use the `⏮` (Previous) or `⏭` (Next) buttons in the transport bar to skip between tracks in your auto-imported Playlist.
- These controls map directly to your system's native media controllers (SMTC/macOS remote commands), meaning your keyboard's hardware media keys will naturally switch tracks in Kintsugi.

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
- The transpose readout is shown below the piano keyboard
- The piano note display moves with the transpose setting

#### EQ

The spectrum panel also works as a live EQ editor.

- Default state is flat: all bell bands start at `0.0 dB`, and the cut bands are effectively off
- Drag a band handle left or right to change its center or cutoff frequency
- Drag a bell band up or down to boost or cut that band
- Nearby bands move visually with the curve, so the response stays continuous while you edit
- Use the mouse wheel on the selected band to adjust `Q` for bell bands or `dB/Oct` slope for the low-cut and high-cut bands
- Double-click the selected band to reset it to its default position
- Click the EQ power button to bypass or enable the entire EQ
- EQ currently affects live playback and the analyzer display in real time

#### Piano Roll / Waterfall

Click `ROLL` in the center visualizer header to switch from the EQ editor to the piano-roll waterfall.

- Notes appear as falling colored blocks
- Channel colors match the `CHANNEL ACTIVITY` strip and the piano keyboard highlights
- The waterfall lanes align directly with the piano below
- The bottom hit area shows where notes land on the keyboard
- Playback speed and transpose both affect the waterfall display

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

<img width="442" height="479" alt="截屏2026-04-05 17 16 23" src="https://github.com/user-attachments/assets/58b8e191-df51-421d-a647-ee6329247a85" />

#### SoundFont

The app ships with a bundled default SoundFont, `GeneralUser-GS.sf2`.

Use `Browse...` if you want to replace it with your own `.sf2` or `.sfz` file.

If you later want to switch back, click `Use Bundled` to restore the packaged `GeneralUser-GS.sf2` and clear the saved custom SoundFont path.

This is required for:

- Playback
- Audio export

The currently loaded SoundFont is applied to the current MIDI file. Custom selections are remembered for later launches.

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

### 8. Exporting Audio

Click `EXPORT AUDIO` to render the current MIDI file to an audio file.

The export button is enabled only when a MIDI file is loaded.

If no SoundFont can be loaded, export does not start. Load a SoundFont first.

<img width="478" height="479" alt="截屏2026-04-05 21 01 44" src="https://github.com/user-attachments/assets/80807717-3852-4953-a58e-f939eec1733d" />

Available export formats:

- `WAV`
- `FLAC`
- `Opus`

On platforms where compressed export is not available, the dialog only shows `WAV`.

#### Export Dialog

The export dialog lets you choose:

- Format
- Sample rate: `44100`, `48000`, `88200`, or `96000 Hz`
- Bit depth: `16-bit PCM`, `24-bit PCM`, or `32-bit Float`
- Destination path

Format-specific behavior:

- `WAV`: supports `16-bit PCM`, `24-bit PCM`, and `32-bit Float`
- `FLAC`: supports `16-bit PCM` and `24-bit PCM`
- `Opus`: supports a configurable bitrate, always exports at `48 kHz`

By default, the app suggests an output file in the same folder as the MIDI file, using the same base name and the selected format's extension.

If that file already exists, the app suggests a numbered filename instead.

#### What the Export Uses

Audio export uses the current playback state for rendering:

- Current SoundFont
- Current MIDI system mode
- Current tempo override
- Current global transpose
- Current EQ state
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

- Last selected custom SoundFont
- MIDI system mode
- Output sample rate
- Mix settings for each MIDI file path

When no saved custom SoundFont is available, the app falls back to the bundled `GeneralUser-GS.sf2` when present.

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

Most often, the bundled default was removed or a custom SoundFont path is no longer valid. Open `Settings`, load an `.sf2` or `.sfz`, then press Play again.

#### Export Audio does not start

Check these points:

- A MIDI file is loaded
- A SoundFont is loaded
- The destination path is writable

#### The wrong mix loads for a file

Mix settings are stored by full file path. If the file was copied, moved, or renamed, it will not reuse the old mix entry.

#### Playback changed after switching sample rate

This is expected. The player rebuilds the audio engine when the sample rate changes.

### 11. Control Summary

| Action                         | Result                                             |
| ------------------------------ | -------------------------------------------------- |
| `OPEN FILE`                    | Load a MIDI file                                   |
| `EVENTS`                       | Open the MIDI Event Browser                        |
| `◀` tab (Right Edge)           | Slide out the Playlist drawer                      |
| `EXPORT AUDIO`                 | Render the current track to WAV, FLAC, or Opus     |
| `Space`                        | Play or pause                                      |
| `⏮` / `⏭`                      | Previous / Next track                              |
| Channel single-click           | Mute/unmute channel                                |
| Channel double-click           | Solo/unsolo channel                                |
| Channel right-click            | Open per-channel mixer                             |
| Channel hover                  | Show channel instrument name                       |
| `MIX`                          | Open global mixer                                  |
| Loop button                    | Toggle looping                                     |
| `BPM`                          | Open tempo override popup                          |
| `EQ` / `ROLL`                  | Switch the center visualizer view                  |
| `KEY -` / `KEY +`              | Transpose down or up                               |
| EQ power button                | Bypass or enable the EQ                            |
| EQ drag / wheel / double-click | Edit EQ bands / adjust width or slope / reset band |
| Slider double-click            | Reset that control                                 |
