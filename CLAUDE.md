# Bleeps (stand-alone program) - CLAUDE.md

PA5DJ, 19 November 2026: *"Build a stand-alone version of Bleeps"* - with Inno and .bat files, full Hamlib connection also via
rigctld (no profiles), the Bleeps logo as splash (`Bleeps-emoji-waves.png`), and frequency management + band buttons like WSJT-X.

## Language
- **Chat with PA5DJ in Dutch.**
- **Everything in the repository and on GitHub is ALWAYS English**: code, comments, UI text, commit messages, CHANGELOG.md,
  README.md, this file. PA5DJ's quotes in comments and the changelog are translated to English as well. (PA5DJ, 19 November
  2026: what we chat is Dutch, but code and comments are always English, everywhere.)

Push only when asked; remote: https://github.com/PA5DJ/Bleeps (branch `main`).

## What it is
WinForms, .NET 10 (`src\Bleeps\Bleeps.csproj`, `src\Bleeps.slnx`). Version in `MainForm.Version` + `AssemblyVersion/FileVersion`
in the csproj + a section in `CHANGELOG.md`; bump at every change and commit afterwards.

## STAND-ALONE since v1.0.9 - separate from ShackAssistant
PA5DJ, 19 November 2026: *"a project standing on its own, separate from ShackAssistant"*. Everything needed is in THIS repository;
the csproj no longer has a path to `..\ShackAssistant`, and a fresh clone builds without ShackAssistant next to it.
- `src\Bleeps\Shared\` - the code that was linked from ShackAssistant up to v1.0.8, now a copy OF ITS OWN (of SA v1.0.750):
  `Shared\Protocol\` = the protocol (`BleepsCode.cs`, `BleepsModem.cs`, `BleepsReceiver.cs`, `BleepsEmoji.cs`), and
  `BleepsFrequencies.cs`, `Fft.cs`, `SpectrumAnalyzer.cs`, `HamlibNative.cs`, `WaterfallPanel.cs`, `BleepsPanel.cs` (chat + TX line),
  `BleepsSettingsForm.cs`, `BleepsWaterfall.cs`. The namespaces (`ShackAssistant.Helpers...`) stayed as they were; what they called
  in ShackAssistant (`AppSettings`, `AppTheme`, `UITheme`) is rebuilt in `Shims.cs`.
- **The PROTOCOL must stay identical to ShackAssistant's**, or the two programs no longer decode each other. It is publicly
  described (shackassistant.com/guides/bleeps.html#spec, version 0, with test vectors). A change that alters what goes on the air =
  a new `Version` in the header, in BOTH programs, and update the description + test vectors.
- `src\Bleeps\Twemoji\` - the emoji (Twemoji 72 x 72, CC-BY 4.0, `LICENSE.txt`); the credit (About, picker) must not go.
- `hamlib\` - the Hamlib DLLs (4.7.2, LGPL; `COPYING.LIB.txt` goes along next to the exe) - a new Hamlib = replace these four DLLs.
- **Hamlib ends the process on a backend registered twice** (`rig_register`: "Hash collision!!! Fatal error!!", `exit(1)`):
  `HamlibNative.rig_init` therefore loads the model list first and inits under the same lock (v1.0.6). Never call
  `rig_load_all_backends` anywhere else.

## Own code (`src\Bleeps`)
- `MainForm.cs` - radio bar like WSJT-X (band list that also takes a typed frequency/band, dial "14.071 000", mode), band buttons
  (one per band from the frequency list, more frequencies on a band = a small menu; the radio's band orange), chat, waterfall with
  TX drive (attenuation in dB, `[audio] tx_atten`). The receive/send glue is that of ShackAssistant's `CwWindow.Bleeps.cs`
  (receiver on its own thread, queue, chat log `Documents\Bleeps\chat\YYYY-MM-DD.txt`, own echo in full duplex).
  Menus: File = Settings, chat log folder, log folder, Exit; Help = only About (a small window with links to www.ham4life.com
  and www.pa5dj.nl, Hamlib version and the Twemoji credit).
- **The waterfall like WSJT-X** (v1.0.1; since v1.0.4 the code is in the shared `BleepsWaterfall.cs`, INI here `[waterfall]`
  without a prefix, PA5DJ: *"it really has to go towards the resolution of WSJT-X"*): `MainForm.WaterfallStep`
  - window 65536 at 48 kHz (= 16384 at 12 kHz, 0.73 Hz), every `WfHop` (3840 = 80 ms) of new audio a spectrum, `[waterfall] avg`
  (5) of them averaged in POWER per line, per pixel all bins added up (never pick one bin). Colour: `FloorDb` = noise + 7 + `floor`
  (0), `CurveDb` = `span` (30) - the Skim curve of `WaterfallPanel` starts 7 dB under FloorDb; with ShackAssistant's 11 dB every
  station went white. CeilingDb does nothing in this mode. Measured in scratchpad `bleepswf` (fake band: -10/0/+10 dB Bleeps +
  carrier).
  - **Controls under the waterfall (v1.0.2)** like WSJT-X: Bins/Pixel (`bpp`, 4), Start (`start`, 100), N Avg (`avg`, 5 = the
    SPEED), Gain (`gain`: span = 30 / 10^(0.015 x Gain) dB, WSJT-X's step) and Zero (`zero`: floor -0.2 dB per step). The range
    is Start .. Start + width x Bins/Pixel x 0.73 Hz (max 6 kHz, `ApplyWfRange`, also on Resize); Bins/Pixel and Start clear the
    picture.
- `Rig.cs` - `RigConfig` (ONE radio, `[radio]` in the INI) and `RigControl`: Hamlib on ONE thread of its own with a queue (Hamlib is
  not thread-safe). Model 2 = rigctld, 4 = FLRig (port `host:port`); no radio + PTT RTS/DTR = Hamlib's dummy rig with only the PTT
  port. `SetPtt` WAITS until it is done (the audio starts after PTT). Mode when tuning: None / USB / Data (PKTUSB).
  `RigConfig.DefaultCivAddress(model)` (v1.0.14): the model's default CI-V address from Hamlib (`rig_init` + `civaddr`, decimal ->
  hex), "" = not a CI-V radio.
- `Frequencies.cs` - `FrequencyList` (default = the shared `BleepsFrequencies.All` since v1.0.5) (`frequencies.json` in
  `%APPDATA%\Bleeps`), by default the Bleeps suggestions (R1 band plan, next to FT8/FT4, clear of CW/phone/RTTY; QO-100 as uplink
  2400.060). `Bands` = band table and frequency text ALWAYS with a point.
- `SettingsForm.cs` - Station (call), Audio (in/out), Radio (Hamlib list, port or host:port, serial lines, CI-V (filled in with the
  model's default, grey without CI-V), PTT VOX/CAT/RTS/DTR + own PTT port, mode, TX delay/tail, poll), Frequencies (grid like
  WSJT-X: Insert/Delete/Reset).
- `Audio.cs` - WinMM in/out + `TxPlayer` (PTT always released in the finally), without ShackAssistant's LAN audio.
- `SplashForm.cs` (logo `Splash.png` = `Bleeps-emoji-waves.png`; since v1.0.3 it stays on screen ALONE for `Program.SplashMs`
  (3 s) and the main window appears only after it - before, that opened on top of it at once and the splash seemed to close
  immediately), `Log.cs` (`Documents\Bleeps\debug\Bleeps.log`), `Program.cs`.
- Settings: `%APPDATA%\Bleeps\bleeps.ini`.
- **`--rig NAME`** (v1.0.8, like WSJT-X's `--rig-name`): `Instance.cs` - its own `%APPDATA%\Bleeps - NAME`,
  `Documents\Bleeps - NAME`, mutex and title. A new place that uses a folder or a fixed name: take `Instance.FolderName` /
  `Instance.Suffix`, never "Bleeps" literally, or the sessions get mixed up.
- `README.md` (v1.0.15) - the user guide on GitHub; update it when a feature, setting or menu changes.

## Building
- `INNO\build_bleeps.bat` (publish to `INNO\build`), `build_bleeps_and_run.bat`, `build_bleeps_setup.bat` (publish + Inno
  `bleeps.iss` + zip + sha256). `ensure_dotnet_sdk.bat` / `ensure_inno_setup.bat` are ShackAssistant's.
- Measured in the scratchpad harness `bleepsapp` (19): a real `rigctld -m 1` on 127.0.0.1:45320 - connect, band button, typed
  frequency and band, dial follows the radio, PTT on/off via CAT, a Bleeps message as audio in -> in the chat and the chat log,
  the settings (305 Hamlib models, 14 frequencies). Never the real sound card or PTT; the harness cleans up `%APPDATA%\Bleeps` and
  `Documents\Bleeps` (and refuses if they already exist).
- Testing in the real app without touching PA5DJ's settings: start the build with `--rig CLAUDETEST` (seed
  `%APPDATA%\Bleeps - CLAUDETEST\bleeps.ini` if needed, e.g. a radio on a non-existent COM port) and remove both
  `Bleeps - CLAUDETEST` folders afterwards. Note: it listens on the default sound card (receive only).
