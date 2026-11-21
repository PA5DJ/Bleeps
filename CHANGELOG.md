# Bleeps Changelog

## v1.0.27

- **A countdown before a ping is answered** (PA5DJ: *"when a user gets a ping, a popup with a timer until the automatic
  answer; within that time the user can cancel"*). A ping to you opens "Bleeps - Ping": who, at what level, and "Bleeps
  answers automatically in 10 s". **Don't answer** (or Esc, or the X) sends nothing, **Answer now** answers at once, otherwise
  the answer goes out when the time is up. Pings that come in meanwhile each get their window after it; the same ping heard
  twice gets one. Settings -> Ping: **Wait before answering** (`[ping] answer_delay`, 10 s; 0 = answer at once without the
  window).
- **Windows open over the middle of the Bleeps window** (PA5DJ: *"make sure windows are shown above the middle of the app
  window"*). Windows puts a standard message box in the middle of the SCREEN; `Dialogs.Show` (a CBT hook on the UI thread)
  moves it over the owner window, kept on its screen. Used for every message box with an owner; the update window asks
  for `CenterOwner`; our own windows already use `CenterParent`.
  Tested with a scripted operator (nothing on the air: the answer is caught): the timer answers after 3 s and not before,
  Don't answer sends nothing, Answer now sends at once, two pings are answered one after the other, a ping heard twice once,
  delay 0 at once - 8 of 8; and with Bleeps at the top left of the screen both the OK box and the countdown opened centred on
  Bleeps (490,364 against Bleeps' 490,365; the screen's middle is 1280,696).

## v1.0.26

- **Ping events in a window you click OK on** (PA5DJ: *"a popup with the status text next to Settings"* - *"no, really a
  window where you have to click OK to go on"*). The answer to your ping, "no answer", and a ping to you (answered, ignored
  by the blacklist, answering off) open a message box "Bleeps - Ping" with the same text as the status line (which cut long
  texts off with "..."). It opens after the automatic answer is queued, and Bleeps keeps receiving and answering while it is
  open; one box at a time - what comes in meanwhile is shown after OK. "Settings -> Station" in those texts is now
  "Settings -> Ping".
- **Ping and pong are kept out of the chat window** (PA5DJ: *"make it so ping/pong messages are not shown in the chat
  window"*): neither received nor our own. While a message is still coming in and could be a ping ("p", "ping pa5") it is
  held back; one that turns out to be ordinary text ("ping me when you are on") is shown when it is clear. The chat log file
  still has them. `Ping.HideInChat`.
  Tested: a scripted operator - the box stays until it is closed, the second event follows after OK, the window keeps working
  while a box is open; and 8 new checks on what is hidden or shown (ping tests now 30 of 30).

## v1.0.25

- **DPI scaling checked across the whole program** (PA5DJ: *"go over the whole app for DPI scaling problems"*). Measured
  on a 200 % screen (scratchpad harness `dpi`: every control's size in the main window, Settings, Bleeps Settings, Ping, About,
  Insert a frequency and the splash, plus screenshots), and again at 100 %: every size at 200 % is now exactly twice the one
  at 100 %. Found and fixed:
  - **The emoji button is exactly as high as Send and Stop** (PA5DJ: *"make the emoji button as high as the Send and Stop
    buttons in general"*). It was 136 x 104 px at 200 % (scaled twice) beside an 82 px Send. Now it asks Send for its
    height at every layout, so it matches at any DPI and after moving to another screen; the smiley follows the height.
  - **Band buttons scaled twice at start**: 232 x 104 px at 200 %, and 116 x 52 after Settings. They are now built after
    the window has scaled itself: the same size always.
  - **Buttons too big**: every text button (Send, Stop, Free spot, Ping…, Settings…, OK, Cancel, Add, Remove, Insert…,
    Delete) kept the size that DPI scaling gave its default size - 82 px high at 200 % where 41 is right. They now fit their
    text, with the usual minimum width.
  - **The line under the TX line** was 94 px high at 200 % (the status text was docked and stretched itself); now as high
    as its buttons.
  - **Status bar**: "Audio in -40 dBFS" was cut off (a fixed 130 px); the fields are now measured in their own font.
  - **Ping, About and Insert a frequency** did not scale: the call box was half as wide at 200 % and the About text wrapped
    at half its width.
  - **A second screen with other scaling**: the chat text and the waterfall labels kept the size of the screen Windows
    started on; they now follow the screen the window is on (the emoji picker is built again there too). Not tried on a
    real second screen - there is only one here.
  - **Window size after changing Windows' scaling**: the size is saved with its DPI (`[window] dpi`); after going from 100 %
    to 200 % the window came back half as big.
  - **The chat keeps room**: in the smallest window (or when the band buttons wrap to a second row) the waterfall took all
    the height and the chat was gone; the waterfall now gives way down to a minimum.

## v1.0.24

- **Settings are kept and loaded reliably** (PA5DJ: *"check save and load of the settings - I often notice settings are not
  kept or loaded right at start; check --rig too"*). Three causes found and fixed:
  - **Sound cards were saved by NUMBER only.** WinMM numbers the cards in the order Windows found them, so plugging a USB
    radio (IC-705, IC-9700 ...) in or out made the saved number point at another card. Now the NAME is saved too
    (`[audio] input_name` / `output_name`) and a card is found by name first, the number only as a fallback
    (`WaveIn.Resolve`). Settings from before get the names of the cards they point at now, at the first start.
  - **Everything set during a session waited for a clean exit** (TX place, waterfall, drive, speed ...) and was gone after a
    crash or a hard stop. Now saved every 30 s when something changed.
  - **A settings file that could not be read at start was overwritten with defaults at exit** (e.g. a virus scanner or
    OneDrive holding it for a moment). Now: 5 tries over a second; if it still fails, Bleeps runs on defaults and does NOT
    write over the file (logged). Every save also keeps the previous version as `bleeps.ini.bak`. A BOM from another editor
    is read too.
  - `--rig` checked: settings, frequency list, logs and chat logs all go through `Instance.FolderName`; two names write two
    files, the name is case-insensitive, the plain Bleeps settings are not touched. Nothing wrong found there.
- **N Avg defaults to 1** (PA5DJ: *"set N Avg default to 1"*): a waterfall line every 80 ms. A value saved earlier stays.
  Tested (scratchpad `settest`): round trip of every kind of value, no .tmp left, .bak, BOM, a locked file (defaults, file not
  overwritten) and a briefly locked one (read on a retry), finding cards by name (moved, any case, gone, old settings, too
  high, none), two `--rig` names in two processes, case-insensitive name, plain settings untouched, the real main window
  saving a change within 30 s without closing, and old settings getting the card names - 20 of 20.

## v1.0.23

- **Ping** (PA5DJ: *"a Ping option: it sends a message to a callsign, the other station answers with a short confirmation; a
  short ID is enough"*, *"also an option whether the app may answer pings"* and *"a blacklist of calls that may not ping you,
  in a tab of its own with an Add, Remove structure"*).
  - **Ping…** button (next to Auto) and **Ping CALL** in the chat's right-click menu send `ping <call> <id>`; the pinged
    station's Bleeps answers by itself with `pong <call> <id> <snr>` (how strong it heard the ping). The status line shows the
    answer ("answered after 31 s - DL1ABC heard you at -12 dB, you hear DL1ABC at -8 dB") or "no answer" after 2 minutes.
  - **Settings -> Ping** (a new tab): *Answer pings automatically* (`[ping] auto_reply`, on) and the **blacklist**
    (`[ping] blacklist`): a list with a call box, **Add** (or Enter) and **Remove** (or Delete). A blacklisted call's ping is
    shown but never answered.
  - A text convention (`Ping.cs`), not a protocol change: Bleeps version 0 stays as it is; ShackAssistant shows ping/pong as
    text. Only a complete message counts; the same ping heard twice is answered once; our own ping is never answered. Sent in
    lower case: 14.7 s Normal / 8.8 s Fast instead of 16.0 / 9.4 s (lower-case letters have the short varicode words).
  - README: a section *Ping*, and the convention under *Protocol description*.
  Tested (scratchpad `ping`): texts, parsing (case, ordinary text is no ping), the answer rules (for us / not for us /
  answering off / blacklist / heard twice / own echo / a pong is never answered), and ping and pong as real audio through the
  modem and the decoder with noise, Normal and Fast - 22 of 22. Main window and the Ping tab rendered and checked. Not tried
  between two stations on the air.

## v1.0.22

- **Check for updates** (PA5DJ: *"build in check for updates too"*), the same as ShackAssistant's: **Help -> Check for
  updates...** and, at every start, a quiet check (Settings > Station > **Updates**, `[update] check_at_start`, on by
  default). It asks `https://bleeps.ham4life.com/api/latest.php` - the new Bleeps site, which reads the version from the
  setup file names in `downloads/` with the same code as shackassistant.com - so uploading a setup publishes it. Only a
  NEWER release gets a message (an older one on the site is never offered); no internet or an error stays in the log when
  quiet. The message offers **Download page** and **Do not remind me about vX** (`[update] skip_version`; a newer release
  speaks again, Help asks anyway). About now links to bleeps.ham4life.com as well.

## v1.0.21

- **The splash shows the credit** (PA5DJ: *"put credits on the splash"* - PA5DJ and the websites): under the logo
  "by PA5DJ - www.ham4life.com - www.pa5dj.nl", above the "Bleeps vX - starting..." line. Checked by rendering the splash
  to a picture.

## v1.0.20

- **Dark mode** (PA5DJ: *"make dark mode in Bleeps"*). Settings > Station > **Theme**: System (follows Windows' app mode,
  the default), Light or Dark - `[display] theme` in the INI, applied at start (a change says it takes effect at the next
  start). Built on WinForms' own colour mode (`Application.SetColorMode`, set in `Program.Main` before the first window), so
  title bar, menus, buttons, text boxes, lists, tabs and the status bar follow by themselves; the shared chat panel reads
  `AppTheme.IsDark` (now real instead of always false) for its own colours. Fixed by hand: the orange band button keeps
  black text, the "SAT / RX" label a lighter blue in dark, and the frequency grid's header no longer stays light.
  Checked in a test instance: main window, File menu, Settings (Station, Frequencies) dark; Light looks as before.

## v1.0.19

- **The TX marker is in the waterfall from the start** (PA5DJ: *"when the app starts I don't see the TX marker"*). The markers
  were only drawn when the TX place changed or a station was decoded, so after starting the waterfall had no TX line until
  then. `MainForm.OnShown` now draws them once. Checked in a test instance: the red TX line with its strip at 1000 Hz right
  after starting.

## v1.0.18

- **Satellite mode: MAIN and SUB are both read automatically** (PA5DJ: *"this does not work well at all.. just read MAIN and
  SUB, without me having to do anything on the radio"*). Measured raw on PA5DJ's IC-9700 (straight over COM3, no Hamlib, only
  reading questions): in satellite mode `25 00`, `25 01`, `26 00` and `26 01` are all refused (NG), and `03` and `1C 03` give
  only the selected band - the radio itself has no command that reads the other band; it is not a Hamlib limit. So Bleeps
  now does what satellite programs do: the selected band at every poll, the other one every 2 s by selecting it for a moment
  (`07 D0`/`07 D1`), reading it (`03`) and selecting the operator's band again - always, checked with `07 D2`. Never while
  transmitting, never `07 B0`. The band marker on the radio blinks briefly.
  - Once the CI-V bus is seen to echo, echo + answer are read in one go, so every command goes out once.
  - A refused band select (the radio did that right after satellite mode was switched off) makes Bleeps ask about satellite
    mode at once: back to the normal dial within ~3 s instead of ~15 s.
  Tested: the fake IC-9700 (now really selecting on `07 D0/D1`, with PTT, bus echo, and NG when not in satellite mode) with the
  real RigControl and libhamlib - both known without touching the radio, the operator's band stays selected, the other band
  follows within ~2 s, no select while transmitting, TX never MAIN and RX never SUB, no `07 B0`, satellite mode off noticed in
  ~3 s; 28 of 28, with and without echo. On PA5DJ's real IC-9700: TX (SUB) 435.385 000 and RX (MAIN) 144.754 360 read at once,
  nothing selected by hand (PA5DJ: "yes, it matches").

## v1.0.17

- **Satellite radios: MAIN and SUB in satellite mode** (PA5DJ: *"apply ShackAssistant's PLAN-SAT9700 here too, for satellite
  radios"*). Ported from ShackAssistant v1.0.753-755, as measured on PA5DJ's IC-9700:
  - Bleeps asks the radio whether it is in satellite mode (after connecting, when a poll is refused, and every 30 polls). That
    question is also what makes Hamlib 4.7.2 stop using 0x25/0x26, which the IC-9700 refuses in satellite mode - before, the
    connection kept failing.
  - In satellite mode `03` reads the SELECTED band and CI-V `07 D2` says which: MAIN = downlink (RX), SUB = uplink (TX). The dial
    shows TX (SUB) with "TX" before the mode, and "SAT / RX ..." (MAIN) next to it. A band that is not selected keeps its last
    value; the TX is unknown until SUB has been selected once.
  - Bleeps never selects a band or VFO, and does not tune in satellite mode (a band button says which uplink to set).
  - A missed `07 D2` (CI-V Transceive frames while the knob turns) keeps the last values instead of guessing.
  - Not over rigctld/FLRig (they do not pass raw CI-V on).
  - Settings > Radio's model's CI-V address (from Hamlib) is what the question goes to.
  Tested: ShackAssistant's fake IC-9700 on 127.0.0.1 with the real RigControl and libhamlib - normal mode tunes; satellite mode
  noticed; MAIN selected = RX, TX unknown; SUB selected = TX; each follows its knob while selected; with CI-V Transceive TX never
  shows MAIN and RX never SUB; no tuning and no band selected in satellite mode; back to normal when it goes off (12 of 12). On
  screen: dial 145.900 000 "TX USB", "SAT RX 435.500 000", band 2m orange. README: a section *Satellite radios (IC-9700)*.
  On PA5DJ's real IC-9700 (COM3, 115200, A2, read-only test instance: PTT VOX, Mode None): connected in 0.8 s, satellite mode
  seen right after rig_open, TX and RX matched the radio's display while selecting MAIN/SUB and turning both knobs (PA5DJ: "yes,
  it matches"); no missed 07 D2 in the log. Satellite mode switched off was not tried on the real radio.

## v1.0.16

- **Everything in the repository is English** (PA5DJ: *"what we chat here is Dutch, but code and comments are always English,
  everywhere"*): CHANGELOG.md and CLAUDE.md translated, and the last Dutch quotes in comments (`HamlibNative.cs`,
  `BleepsReceiver.cs`). No code change.
- **README: the protocol description is in the README, the references to ShackAssistant are gone** (PA5DJ: *"put the protocol
  description in the readme and remove the reference to ShackAssistant"*): a new section *Protocol description* - the signal
  (Normal/Fast, GFSK BT 1.0, Costas syncs), the frame, the bits (header, CRC-12, varicode rule, CRC-16, K=7 code,
  interleaver, Gray) and test vectors made with the real encoder (`PA5DJ` "hi" and `PA5DJ` empty, Normal and Fast).

## v1.0.15

- **README.md for GitHub with a guide** (PA5DJ: *"make a readme for GitHub with a guide"*): what Bleeps is, installing, a quick
  start, and a section per part (Settings Station/Audio/Radio/Frequencies, band buttons and dial, the waterfall and its controls,
  chatting, TX drive, `--rig`, QO-100/full duplex), where the files are, troubleshooting, building from source, and the credits
  (ham4life.com, pa5dj.nl, Hamlib LGPL, Twemoji CC-BY 4.0). No code change.

## v1.0.14

- **Settings > Radio: the CI-V address is filled in when we know it** (PA5DJ: *"fill it in when we know it"*). The default address
  comes from Hamlib itself (`RigConfig.DefaultCivAddress`: `rig_init` without opening anything, then the backend's `civaddr`
  setting, which Hamlib gives in decimal: 148 -> `94`). Measured: IC-7300 94, IC-705 A4, IC-9700 A2, IC-7610 98, IC-7100 88;
  Yaesu/Kenwood/rigctld none. An address of your own stays; one filled in by us follows the model. The box is now grey for every
  model without CI-V (before: everything whose name did not start with "Icom" - now Xiegu and other CI-V radios count too), and a
  grey box is no longer saved.

## v1.0.13

- **"Open the log folder" is now in the File menu** (PA5DJ: *"yes, to File"*), under "Open the chat log folder" - since v1.0.12
  Help holds only About.

## v1.0.12

- **The Help menu holds only About Bleeps** (PA5DJ: *"yes, only About"*): "Bleeps on the web" and "Open the log folder" are gone.

## v1.0.11

- **The links to www.ham4life.com and www.pa5dj.nl are only in About** (PA5DJ: *"in Help only a link to the About window"*):
  removed from the Help menu; About Bleeps, which shows them, stays there.

## v1.0.10

- **All comments in English** (PA5DJ: *"all comments, everything must be in English"*): the last Dutch comments
  (`WaterfallPanel.cs`: the colour curve and the palette stops) translated, and PA5DJ's quotes in comments (also in the csproj) now
  in English. Comments only - no code, the protocol is unchanged.
- **About mentions www.ham4life.com and www.pa5dj.nl** (PA5DJ: *"mention www.ham4life.com and www.pa5dj.nl in About"*): About is
  now a small window with the two as clickable links (was a MessageBox), and they are in the Help menu too.

## v1.0.9

- **Bleeps is a project of its own** (PA5DJ: *"make Z:\\PROJECT\\SOFTWARE\\Bleeps and https://github.com/PA5DJ/Bleeps a project
  standing on its own, separate from ShackAssistant"*). What used to be linked from `..\ShackAssistant` is now a copy OF ITS OWN
  in the repository: `src\Bleeps\Shared\` (the protocol in `Shared\Protocol\`: BleepsCode, BleepsModem, BleepsReceiver,
  BleepsEmoji; and BleepsFrequencies, Fft, SpectrumAnalyzer, HamlibNative, WaterfallPanel, BleepsPanel, BleepsSettingsForm,
  BleepsWaterfall), `src\Bleeps\Twemoji\` (122 pictures + LICENSE.txt, CC-BY 4.0) and `hamlib\` (the four Hamlib DLLs, LGPL, with
  COPYING.LIB.txt, COPYING.txt, LICENSE.txt and README.w64-bin.txt; COPYING.LIB also goes along next to the exe). The csproj no
  longer has a path to ShackAssistant. Copied from ShackAssistant v1.0.750 (with the Hamlib fix of v1.0.6).
  Measured: a fresh clone in a folder WITHOUT ShackAssistant next to it builds; the app starts.

## v1.0.8

- **More than one Bleeps side by side, like WSJT-X** (PA5DJ: *"I want to be able to start sessions side by side with a CLI flag,
  as in WSJT-X with --rig IC9700 etc. each then gets its own appdata"*): `Bleeps.exe --rig IC9700` (also `--rig=IC9700`,
  `--rig-name IC9700`, `-r IC9700`) has its own settings (`%APPDATA%\Bleeps - IC9700`), log and chat logs
  (`Documents\Bleeps - IC9700`), its own single-instance lock and the name in title and splash. Without the flag nothing changes.
  A name that is already running says so; the ordinary Bleeps mentions the flag at "already running". A new name starts with
  empty settings (as in WSJT-X).
  `Instance.cs` (reading + cleaning into a folder name), used in `Shims.AppSettings.Folder`, `Log.DataFolder`, the mutex and the
  titles. Measured: ordinary Bleeps + `--rig TESTRIG` run together, each with its own title and folders; a second
  `--rig=TESTRIG` refuses; the ordinary `bleeps.ini` back byte for byte, the test folders gone again.

## v1.0.7

- **Settings > Radio: what does not apply goes grey, label and box together** (PA5DJ: *"grey out items that do not apply to a
  choice such as CAT"*). Until now only the box went grey and the label stayed black (so PTT port with CAT did not stand out).
  Now: PTT CAT -> PTT port; VOX -> PTT port, TX delay and PTT tail (there is no PTT to wait for); no radio -> baud, data/stop
  bits, handshake, DTR, RTS, CI-V, Mode and Poll every. The values are kept.
  Measured on the real window (built separately, never OK): the four settings, each label follows its box, `bleeps.ini`
  unchanged.

## v1.0.6

- **Sometimes stopped right after the splash** (PA5DJ: *"it stops every time after the splash"*): exit code 1, without an error
  or a log line. Cause in Hamlib 4.7.2: `rig_register` does `exit(1)` ("Hash collision!!! Fatal error!!") when a backend registers
  twice - and that happened when the UI thread loaded the model list while the radio thread did `rig_init`. Fixed in the shared
  `HamlibNative.cs` of ShackAssistant (SA v1.0.750): the model list first, then `rig_init` under the same lock. Measured: 5 starts
  with the IC-9700 on COM3, all 5 keep running.

## v1.0.5

- The default frequency list comes from ShackAssistant's shared `BleepsFrequencies.cs` (Mode Master's band buttons, SA v1.0.748):
  one list for both programs.

## v1.0.4

- The waterfall is now ShackAssistant's SHARED `Forms/BleepsWaterfall.cs` (which Mode Master in Bleeps also uses since v1.0.747):
  the same picture and controls in both programs, one copy of the code.

## v1.0.3

- The splash stays on screen alone for 3 s; the main window appears only after it (PA5DJ: it disappeared at once - the main
  window opened on top of it).

## v1.0.2

- Waterfall controls like WSJT-X's Wide Graph (PA5DJ): Bins/Pixel (default 4 = 2.9 Hz/px), Start (Hz), N Avg (the speed: spectra
  per line, 5 = one line per 0.4 s), Gain (contrast) and Zero (brightness); range and Hz/px next to them, all saved in
  `[waterfall]`.

## v1.0.1

- Waterfall with the resolution of WSJT-X (PA5DJ): window 1.37 s (65536 points, 0.73 Hz per bin), a spectrum every 80 ms, 5
  averaged per line (N Avg), per pixel the power of all bins added up (Bins/Pixel), colours over 30 dB from the noise
  (`[waterfall] floor`/`span`/`avg` in the INI).

## v1.0.0

- First stand-alone version of Bleeps (PA5DJ): chat and waterfall from ShackAssistant (same protocol, linked sources), a radio bar
  like WSJT-X with band list, dial and band buttons, frequency management (Settings -> Frequencies), Hamlib with every model and
  rigctld/FLRig (one radio, no profiles), PTT via CAT/RTS/DTR/VOX, a splash with the Bleeps logo, an Inno installer and .bat
  files.
