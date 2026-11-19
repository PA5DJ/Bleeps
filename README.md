# Bleeps

**Bleeps** is a free-running keyboard chat mode for radio amateurs: type a line, press Enter, and it goes out as a narrow
4-tone signal in the audio passband of your SSB/data radio. Everyone on the frequency who runs Bleeps reads it in a chat
window - no fixed time slots, no fixed exchange, just text (and emoji 🙂).

This is the **stand-alone Windows program**. The same Bleeps is built into
[ShackAssistant](https://shackassistant.com/guides/bleeps.html); the two talk to each other on the air.

- Radio control through **Hamlib** (300+ radios), **rigctld** or **FLRig** - frequency, mode and PTT
- Band buttons and a frequency box like **WSJT-X**, with suggested Bleeps frequencies from 80 m to QO-100
- A **waterfall** built the way WSJT-X's Wide Graph is, with the same controls
- **Normal** (~83 Hz wide, most robust) and **Fast** (~166 Hz wide, twice the speed) - both are always received
- **Auto free spot**: at every Send it checks your place and moves to a quiet one if it is taken
- Several radios side by side with `--rig NAME`, each with its own settings and logs

> Website and protocol description: <https://shackassistant.com/guides/bleeps.html>
> (the [specification](https://shackassistant.com/guides/bleeps.html#spec) is public, with test vectors).

---

## Contents

1. [Install](#install)
2. [Quick start](#quick-start)
3. [Guide](#guide)
   - [Settings - Station and Audio](#settings---station-and-audio)
   - [Settings - Radio](#settings---radio)
   - [Settings - Frequencies](#settings---frequencies)
   - [Bands and frequency](#bands-and-frequency)
   - [The waterfall](#the-waterfall)
   - [Chatting](#chatting)
   - [TX drive and levels](#tx-drive-and-levels)
   - [More than one radio (`--rig`)](#more-than-one-radio---rig)
   - [QO-100 and full duplex](#qo-100-and-full-duplex)
4. [Where Bleeps keeps its files](#where-bleeps-keeps-its-files)
5. [Troubleshooting](#troubleshooting)
6. [Building from source](#building-from-source)
7. [Credits and licences](#credits-and-licences)

---

## Install

1. Download `BleepsSetup_vX.Y.Z.exe` (or the `.zip` with the same name) from the
   [Releases](https://github.com/PA5DJ/Bleeps/releases) page. The `.sha256.txt` next to it holds the checksums.
2. Run it. The installer checks for the **.NET 10 Desktop Runtime** and downloads and installs it when it is missing.
3. Bleeps goes to `C:\Program Files\Bleeps`, with Hamlib in its own `hamlib` folder. Nothing else needs installing.

Windows 10/11, 64-bit. The setup is not code-signed, so Windows SmartScreen may ask you to confirm
(*More info* -> *Run anyway*).

## Quick start

1. Start Bleeps. The first time, **Settings** opens on its own.
2. **Station**: enter your callsign.
3. **Audio**: pick the input (from the radio) and the output (to the radio) - the same sound card you use for FT8.
4. **Radio**: pick your radio from the list and its COM port (or see [Settings - Radio](#settings---radio) for rigctld,
   FLRig, VOX or RTS/DTR PTT). Press **OK**.
5. Click a **band button** (20m, 40m, ...). The radio tunes to the Bleeps frequency of that band.
6. Watch the waterfall: Bleeps signals show up as narrow four-tone stripes, and what is decoded appears in the chat.
7. Type a line in the **TX** box and press **Enter**. That's it - you're on the air.

---

## Guide

### Settings - Station and Audio

Open with **File -> Settings...**.

| Setting | What it does |
|---|---|
| **Callsign** | Goes out at the start of every message, so the others see who is talking. |
| **Audio input** | The sound card input that carries the radio's receive audio. |
| **Audio output** | The sound card output that goes to the radio's data/mic input. |

### Settings - Radio

One radio, no profiles - what you set here is what Bleeps uses. Fields that do not apply to your choice turn grey.

| Setting | What it does |
|---|---|
| **Rig** | Your radio from Hamlib's list. **None (no CAT)** = no radio control: PTT on VOX or on an RTS/DTR line still works. **Hamlib NET rigctl** talks to a running `rigctld`, **FLRig** to FLRig. |
| **Port** | The COM port of the radio, or `host:port` for rigctld (`127.0.0.1:4532`), FLRig (`127.0.0.1:12345`) or a radio on the network. |
| **Baud rate, Data/Stop bits, Handshake** | Must match the radio's CAT menu. *Default* = what Hamlib uses for that radio. |
| **DTR / RTS** | The control lines of the CAT port. Some interfaces need one high (power) or low (to avoid keying the radio). |
| **CI-V address** | For CI-V radios (Icom, Xiegu, ...). Filled in automatically with the model's default from Hamlib (94 for an IC-7300). Only change it if you changed it in the radio. Grey for radios without CI-V. |
| **PTT** | **CAT**: key through Hamlib (also via rigctld/FLRig). **RTS/DTR**: a serial line, on the CAT port or a PTT port of its own. **VOX**: the radio keys on the audio. |
| **PTT port** | For RTS/DTR: a separate COM port. Empty = the CAT port. |
| **Mode** | What the radio is set to when you pick a band or frequency. **Data** = USB-D / DATA-U (PKTUSB), **USB**, or **None** to leave the mode alone. |
| **TX delay (ms)** | Time between PTT and the audio, so the relays settle first. |
| **PTT tail (ms)** | How long PTT stays down after the audio. |
| **Poll every (ms)** | How often the frequency is read back from the radio. |

**Using rigctld.** Start rigctld yourself (for example `rigctld -m 3073 -r COM5 -s 19200` for an IC-7300), then pick
*Hamlib NET rigctl* and port `127.0.0.1:4532`. Handy when another program (WSJT-X, a logger) shares the radio.

**Using FLRig.** Start FLRig, pick *FLRig* and port `127.0.0.1:12345`.

### Settings - Frequencies

A grid like WSJT-X's: one row per frequency, with band, frequency in MHz, IARU region and a description.
**Insert** adds a row, **Delete** removes one, **Reset** brings back the standard list:

| Band | MHz | Band | MHz |
|---|---|---|---|
| 80m | 3.570 | 10m | 28.071 |
| 60m | 5.360 | 6m | 50.385 |
| 40m | 7.051 | 4m | 70.270 |
| 30m | 10.143 | 2m | 144.605 |
| 20m | 14.071 | 70cm | 432.090 |
| 17m | 18.097 | 13cm | 2400.060 (QO-100 uplink) |
| 15m | 21.071 | | |
| 12m | 24.926 | | |

These are suggestions inside the digimode segments of the IARU Region 1 band plans, next to FT8/FT4 and clear of
CW, phone and RTTY. Frequencies are always written with a point (`14.071`).

### Bands and frequency

The bar at the top works like WSJT-X:

- **Band buttons** - one per band in the frequency list. A band with more than one frequency opens a small menu.
  The button of the band the radio is on is orange.
- **Band box** - pick a frequency from the list, or type one in MHz (`14.071`) or a band (`20m`) and press **Enter**.
- **Dial** - the radio's frequency (`14.071 000`). It follows the radio when you turn the knob. **Red** means the radio
  does not answer.

### The waterfall

The waterfall shows the audio passband, built like WSJT-X's Wide Graph (fine resolution, averaged in power). Bleeps
signals are four thin lines next to each other; stations being decoded are marked in their colour.

- **Click** in the waterfall where you want to send. The red **TX** marker and the shaded strip show where your
  signal will be (the lowest of the four tones).
- **Free spot** moves TX to the quietest place in the passband, away from strong stations.

The controls under the waterfall are the ones from WSJT-X:

| Control | What it does |
|---|---|
| **Bins/Pixel** | Horizontal zoom: how many frequency bins one pixel holds. Clears the picture. |
| **Start** | The audio frequency (Hz) at the left edge. Clears the picture. |
| **N Avg** | How many spectra are averaged per line - the **speed** of the waterfall (higher = slower, smoother). |
| **Gain** | Contrast: higher makes weak signals stand out more. |
| **Zero** | Moves the noise floor up or down (brightness). |

### Chatting

- Type in the **TX** box and press **Enter** (or **Send**). While one message goes out you can already type the next;
  the waiting ones show greyed at the bottom of the chat.
- **Esc** (or **Stop**) stops sending at once. The others see the message end with ⚠.
- **Speed**: **Normal** (~3 characters/s, ~83 Hz wide, the most robust) or **Fast** (twice as fast, ~166 Hz wide).
  You choose the speed you *send* at; receiving always hears both.
- **Auto** (free spot): at every Send Bleeps first checks your place. Still free - it stays, so your chat partner finds
  you again. Taken or busy - it moves to the quietest place, then sends.
- The counter shows how much of a message is used (at most 250 bytes; an accent or € takes 2-3).
- **Emoji**: click the 🙂 button, or type a classic smiley (`:)` `;)` `:D` `<3`) or a code like `:beer:` - it turns into
  the emoji as you type. On the air each emoji goes out in its shortest form.
- **Chat lines**: hover a line to see its audio frequency and strength (SNR in 2500 Hz); a ✓ means the message came in
  complete and checked. **Click a callsign** to start your TX line with `CALL: ` and reply to that station; **right-click** a line to copy it, send next to
  that station, or give the station its own colour.
- **Settings...** (next to the TX line) sets where Hz and dB stand in a chat line, colours or mono, full duplex and how
  many processor cores the decoders may use.

Every chat is saved per day in `Documents\Bleeps\chat\YYYY-MM-DD.txt` (**File -> Open the chat log folder**).

### TX drive and levels

The **TX drive** slider (right of the waterfall) attenuates the transmit audio in dB, like WSJT-X's *Pwr*. Set it so
the radio's **ALC stays (almost) still** - a clean signal is decoded far better than a loud one. You can turn it while
sending.

### More than one radio (`--rig`)

Like WSJT-X's `--rig-name`, you can run several Bleeps at the same time, each with its own radio:

```powershell
& "C:\Program Files\Bleeps\Bleeps.exe" --rig IC9700
```

`--rig=IC9700`, `--rig-name IC9700` and `-r IC9700` work too. Make a shortcut with the flag after the target to start
it with one click.

Each name gets its own:

- settings: `%APPDATA%\Bleeps - IC9700\`
- log and chat logs: `Documents\Bleeps - IC9700\`
- title bar (`Bleeps vX.Y.Z - IC9700`)

A new name starts with empty settings. The same name cannot run twice. Without the flag, Bleeps uses the ordinary
`Bleeps` folders.

### QO-100 and full duplex

The 13cm entry is the QO-100 uplink (2400.060 MHz; the downlink is 10489.560). With a separate receiver you can turn on
**Full duplex** in the Bleeps Settings: the decoder and the waterfall keep running while you send, and your own signal
coming back from the satellite is marked ↩ ✓ on your own line. Leave it off with a single HF radio - its receiver is
muted while sending.

---

## Where Bleeps keeps its files

| What | Where |
|---|---|
| Settings | `%APPDATA%\Bleeps\bleeps.ini` |
| Frequency list | `%APPDATA%\Bleeps\frequencies.json` |
| Chat logs (per day) | `Documents\Bleeps\chat\` |
| Debug log | `Documents\Bleeps\debug\Bleeps.log` (**File -> Open the log folder**) |

With `--rig NAME` the folders are called `Bleeps - NAME`.

## Troubleshooting

| Problem | Try this |
|---|---|
| Dial is **red** / "no CAT" | Wrong COM port or baud rate, or another program has the port open (a COM port takes one program at a time). Close the other program, or share the radio through rigctld or FLRig. |
| "port in use by another program" | Same as above - for example a browser logbook or WSJT-X holding the port. |
| Radio does not key | Check **PTT** in Settings -> Radio. With CAT, the radio must accept CAT PTT; with RTS/DTR, check the line and the PTT port; with VOX, turn VOX on in the radio. |
| Nothing decodes | Check the audio input and the level (the noise should be visible in the waterfall), and that the radio is in USB/DATA mode on the right frequency. |
| Signal splatters / others can't read you | Lower the **TX drive** until the ALC stays still. |
| Something else | Look in the debug log (**File -> Open the log folder**). |

## Building from source

Requirements: Windows, the **.NET 10 SDK**, and for the installer **Inno Setup 6**. The scripts in `INNO\` fetch the
SDK and Inno Setup when they are missing.

```powershell
git clone https://github.com/PA5DJ/Bleeps.git
cd Bleeps\INNO
.\build_bleeps.bat            # publish to INNO\build
.\build_bleeps_and_run.bat    # publish and start
.\build_bleeps_setup.bat      # publish + installer + zip + sha256
```

Or open `src\Bleeps.slnx` in Visual Studio. Everything needed is in this repository:

| Folder | What |
|---|---|
| `src\Bleeps\` | The program (WinForms, .NET 10) |
| `src\Bleeps\Shared\Protocol\` | The Bleeps protocol - keep it identical to ShackAssistant's, or the two stop reading each other |
| `src\Bleeps\Shared\` | Waterfall, spectrum, Hamlib wrapper, chat panel |
| `src\Bleeps\Twemoji\` | The emoji pictures |
| `hamlib\` | The Hamlib 4.7.2 DLLs |
| `INNO\` | Build scripts and the Inno Setup script |

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version.

## Credits and licences

- **Bleeps** by PA5DJ - <https://www.ham4life.com> - <https://www.pa5dj.nl>
- **Hamlib** 4.7.2 - LGPL; the licence travels along in `hamlib\COPYING.LIB.txt`. <https://hamlib.github.io>
- **Twemoji** emoji graphics by Twitter, Inc and other contributors - CC-BY 4.0 (`src\Bleeps\Twemoji\LICENSE.txt`).
