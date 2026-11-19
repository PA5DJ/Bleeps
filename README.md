# Bleeps

**Bleeps** is a free-running keyboard chat mode for radio amateurs: type a line, press Enter, and it goes out as a narrow
4-tone signal in the audio passband of your SSB/data radio. Everyone on the frequency who runs Bleeps reads it in a chat
window - no fixed time slots, no fixed exchange, just text (and emoji 🙂).

- Radio control through **Hamlib** (300+ radios), **rigctld** or **FLRig** - frequency, mode and PTT
- Band buttons and a frequency box like **WSJT-X**, with suggested Bleeps frequencies from 80 m to QO-100
- A **waterfall** built the way WSJT-X's Wide Graph is, with the same controls
- **Normal** (~83 Hz wide, most robust) and **Fast** (~166 Hz wide, twice the speed) - both are always received
- **Auto free spot**: at every Send it checks your place and moves to a quiet one if it is taken
- Several radios side by side with `--rig NAME`, each with its own settings and logs

The protocol is open: see [Protocol description](#protocol-description) below, with test vectors.

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
   - [Satellite radios (IC-9700)](#satellite-radios-ic-9700)
4. [Where Bleeps keeps its files](#where-bleeps-keeps-its-files)
5. [Troubleshooting](#troubleshooting)
6. [Protocol description](#protocol-description)
7. [Building from source](#building-from-source)
8. [Credits and licences](#credits-and-licences)

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

### Satellite radios (IC-9700)

Bleeps notices when the radio is in **satellite mode** (IC-9700 and other Icoms with a satellite mode) and then works with
**MAIN and SUB**, not with VFOs:

| | Band | Shown |
|---|---|---|
| **TX** (uplink) | **SUB** | in the dial, with `TX` before the mode |
| **RX** (downlink) | **MAIN** | under `SAT`, next to the dial |

- **Both are read automatically** - you do not have to select anything on the radio. In satellite mode the IC-9700 only
  tells the frequency of the band that is *selected* (it refuses `25`/`26`, the commands for the other band). So Bleeps
  reads the selected band at every poll, and the other one every 2 seconds the way satellite programs do it: select it
  for a moment (`07 D0`/`07 D1`), read it (`03`), and select **your** band again (checked with `07 D2`). You see the
  band marker on the radio blink briefly.
- Never while transmitting, and never `07 B0` (swap MAIN and SUB).
- Bleeps **does not tune** in satellite mode: the band buttons only tell you which uplink to set. Tune the radio (or
  your tracking program) yourself.
- This needs Bleeps to talk to the radio itself (a COM port, or the radio's own network port) - rigctld and FLRig do not
  pass raw CI-V on.
- **CI-V Transceive** can be left off; Bleeps does not need it. With it on, a reading can be missed while the knob turns;
  the frequencies then keep their last value rather than show the wrong band.
- When satellite mode is switched off on the radio, Bleeps notices within a few seconds and goes back to the normal dial.

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
| Satellite mode: the dial shows `-- . --- ---` | Bleeps reads MAIN and SUB itself within a few seconds. Over rigctld/FLRig it cannot - connect Bleeps to the radio directly (COM port). |
| Something else | Look in the debug log (**File -> Open the log folder**). |

## Protocol description

Bleeps protocol **version 0**. Anyone may implement it. The reference is the code in `src\Bleeps\Shared\Protocol\`
(`BleepsCode.cs`, `BleepsModem.cs`); an implementation is correct when it produces the [test vectors](#test-vectors) bit
for bit. A change that alters what goes on the air needs a new version number.

### Overview

```
text (UTF-8) -> varicode -> + header, CRCs, tails -> convolutional code K=7 r=1/2 -> interleaver
             -> 2 bits per symbol (Gray) -> 4-tone frame with Costas syncs -> GFSK audio
```

A message is one call sign plus up to 250 bytes of UTF-8 text. There are no time slots: a station sends whenever it likes,
and the receiver searches the whole passband for the start syncs.

### The signal

| | Normal | Fast |
|---|---|---|
| Samples per symbol (at 48 kHz) | 2304 | 1152 |
| Symbol rate | 20.833 baud | 41.667 baud |
| Tone spacing (= symbol rate) | 20.833 Hz | 41.667 Hz |
| Occupied bandwidth (4 tone steps) | ~83 Hz | ~167 Hz |
| Start sync (Costas, sent 3x) | `0 2 3 1` | `0 3 1 2` |
| End sync (sent 2x) | `1 3 2 0` | `2 1 3 0` |
| Interleaver (branches x step) | 8 x 2 | 8 x 4 |
| Text speed | ~3 characters/s | ~6 characters/s |

- **4 tones** (0-3); the transmit frequency is that of **tone 0**, tone *k* is at f0 + *k* x spacing.
- **GFSK** with a Gaussian frequency pulse, **BT = 1.0** (as FT4), modulation index 1, continuous phase. The pulse:
  `p(t) = ½ [erf(c(t+½)) − erf(c(t−½))]`, `c = π·√(2/ln 2)·BT`, t in symbols; the shifted pulses add up to 1.
- Half a symbol of raised-cosine **amplitude ramp** at the start and the end; the first and last tone are held through it.
- The sync arrays are order-4 Costas arrays that FT4 does **not** use, and Normal and Fast each have their own - that is how a
  receiver tells the two speeds apart.

### The frame (symbols)

```
START = start Costas x3 | DATA ... (one start Costas after every 48 data symbols, if more data follows) | END = end Costas x2
```

### The bits

The **info bits**, in this order (all numbers most significant bit first):

| Field | Bits | |
|---|---|---|
| Version | 4 | `0` |
| Call | 10 x 6 | Characters from ` ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789/` (index 0..37), padded with spaces to 10 |
| CRC-12 | 12 | Over the 64 bits above. Poly `0x80F`, init 0, MSB first |
| Tail | 6 | Zeros |
| Text | ... | Per UTF-8 byte: its varicode word + `00` |
| EOT | ... | The varicode word of byte `0x04` + `00` |
| CRC-16 | 16 | Over the UTF-8 text bytes: CRC-16/CCITT-FALSE (poly `0x1021`, init `0xFFFF`, no reflection) |
| Tail | 6 | Zeros |

A receiver trusts nothing when the CRC-12 of the header fails. Text that arrives without a valid CRC-16 is shown, but
marked incomplete.

**Varicode.** Bleeps has its own table, built by a rule both sides can repeat:
1. The words: all bit strings that start and end with `1` and have no `00` inside, in order of length, then of value
   (`1`, `11`, `101`, `111`, `1011`, `1101`, `1111`, `10101`, ...).
2. The byte values, from most to least common: first the characters of this line (it starts with a space)

   ```
    etaoinsrhldcumfpgwybvkxjqz.,ETAOINSRHLDCUMFPGWYBVKXJQZ0123456789?!'-/:;()"@#&+=*%$_<>[]{}|\~^`
   ```

   then `0x04` (EOT), `0xC3`, `0xE2`, `0x0A`, then every other byte value 0..255 in rising order.
3. The *n*-th byte value gets the *n*-th word.

Because a word never contains `00`, the `00` after every word marks where it ends.

**Convolutional code.** K = 7, rate 1/2, generator masks `0x6D` and `0x4F` (over the 7-bit register, the newest bit as the
least significant), starting in state 0. Per info bit: shift it in, output `parity(reg & 0x6D)`, then
`parity(reg & 0x4F)`.

**Interleaver.** Convolutional (Forney), 8 branches: `y[n] = x[n − (n mod 8) · 8 · step]`, with x = 0 before the start.
Append `7 · step · 8` zero bits (112 for Normal, 224 for Fast) so everything leaves. Both speeds spread a burst over about
2.7 s.

**Bits to tones.** Two coded bits per symbol, the first one first: the value `b0·2 + b1` maps to the tone `0 1 3 2`
(Gray code).

### Test vectors

Varicode: `' '` = `1`, `e` = `11`, `t` = `101`, `a` = `111`, `E` = `1110111`, `0` = `101010111`, `?` = `101111101`,
EOT = `1010111111`. CRC-16 of the ASCII bytes `123456789` = `0x29B1`.

**Call `PA5DJ`, text `hi`** - 129 info bits:

```
000001000000000110000000010000101000000000000000000000000000000001111100010000000011011001101001010111111000110001000000011000000
```

258 coded bits; Normal: 370 after the interleaver, 217 symbols (10.42 s):

```
0231023102310000000030000000200033000000310003002000033011100231020003330003001200223021301230130301230002010000023120303330030022203230330010111312300301111213030002310111012100330031000000030003000100000001013201320
```

Fast: 482 after the interleaver, 281 symbols (6.74 s):

```
03120312031200000000300000003000300010003000000033000000100003120300110003000000030032003130300000003020000010000312333033231313301331132311030203022331123001010330031210310010030002000110001001000113001200310023002103120000000000020001000000020003000000000001000000010312021302130
```

**Call `PA5DJ`, empty text** - 116 info bits:

```
00000100000000011000000001000010100000000000000000000000000000000111110001000000001010111111001111111111111111000000
```

Normal, 204 symbols (9.79 s):

```
023102310231000000003000000020003300000031000300200003301110023102000333000300120022302130123013030113003201000002310030333030002020203012000211020201200122013100120231001000020013000100020001000113201320
```

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
| `src\Bleeps\Shared\Protocol\` | The Bleeps protocol (`BleepsCode.cs` = the bit layer, `BleepsModem.cs` = the signal) |
| `src\Bleeps\Shared\` | Waterfall, spectrum, Hamlib wrapper, chat panel |
| `src\Bleeps\Twemoji\` | The emoji pictures |
| `hamlib\` | The Hamlib 4.7.2 DLLs |
| `INNO\` | Build scripts and the Inno Setup script |

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version.

## Credits and licences

- **Bleeps** by PA5DJ - <https://www.ham4life.com> - <https://www.pa5dj.nl>
- **Hamlib** 4.7.2 - LGPL; the licence travels along in `hamlib\COPYING.LIB.txt`. <https://hamlib.github.io>
- **Twemoji** emoji graphics by Twitter, Inc and other contributors - CC-BY 4.0 (`src\Bleeps\Twemoji\LICENSE.txt`).
