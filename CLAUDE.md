# Bleeps (los programma) - CLAUDE.md

PA5DJ, 19 november 2026: *"Bouw een los staande versie van bleeps"* - met Inno en .bat-bestanden, volledige Hamlib-verbinding ook
via rigctld (geen profielen), het Bleeps-logo als splash (`Bleeps-emoji-waves.png`), en frequentiebeheer + bandknoppen zoals WSJT-X.

Antwoord in het Nederlands. Pushen alleen op verzoek; remote: https://github.com/PA5DJ/Bleeps (branch `main`).

## Wat het is
WinForms, .NET 10 (`src\Bleeps\Bleeps.csproj`, `src\Bleeps.slnx`). Versie in `MainForm.Version` + `AssemblyVersion/FileVersion`
in de csproj + een sectie in `CHANGELOG.md`; bump bij elke wijziging en commit daarna.

## ZELFSTANDIG sinds v1.0.9 - los van ShackAssistant
PA5DJ, 19 november 2026: *"een zelf opzich staand project los van shackassistant"*. Alles wat nodig is staat in DEZE repository; de
csproj kent geen pad naar `..\ShackAssistant` meer, en een verse kloon bouwt zonder ShackAssistant ernaast.
- `src\Bleeps\Shared\` - de code die tot v1.0.8 uit ShackAssistant gelinkt werd, nu een EIGEN KOPIE (van SA v1.0.750):
  `Shared\Protocol\` = het protocol (`BleepsCode.cs`, `BleepsModem.cs`, `BleepsReceiver.cs`, `BleepsEmoji.cs`), en `BleepsFrequencies.cs`,
  `Fft.cs`, `SpectrumAnalyzer.cs`, `HamlibNative.cs`, `WaterfallPanel.cs`, `BleepsPanel.cs` (chat + TX-regel), `BleepsSettingsForm.cs`,
  `BleepsWaterfall.cs`. De namespaces (`ShackAssistant.Helpers...`) zijn gebleven zoals ze waren; wat ze van ShackAssistant aanriepen
  (`AppSettings`, `AppTheme`, `UITheme`) staat nagebouwd in `Shims.cs`.
- **Het PROTOCOL moet gelijk blijven aan dat van ShackAssistant**, anders decoderen de twee programma's elkaar niet meer. Het is
  openbaar beschreven (shackassistant.com/guides/bleeps.html#spec, versie 0, met testvectoren). Een wijziging die de lucht verandert =
  een nieuwe `Version` in de header, in BEIDE programma's, en de beschrijving + testvectoren bijwerken.
- `src\Bleeps\Twemoji\` - de emoji (Twemoji 72 x 72, CC-BY 4.0, `LICENSE.txt`); de vermelding (About, kiezer) mag niet weg.
- `hamlib\` - de Hamlib-DLL's (4.7.2, LGPL; `COPYING.LIB.txt` gaat mee naast de exe) - een nieuwe Hamlib = deze vier DLL's vervangen.
- **Hamlib sluit het proces af bij een dubbel geregistreerde backend** (`rig_register`: "Hash collision!!! Fatal error!!", `exit(1)`):
  `HamlibNative.rig_init` laadt daarom eerst de modellenlijst en init onder hetzelfde slot (v1.0.6). Roep `rig_load_all_backends`
  nergens anders aan.

## Eigen code (`src\Bleeps`)
- `MainForm.cs` - radiobalk zoals WSJT-X (bandlijst die ook een getypte frequentie/band neemt, dial "14.071 000", mode), bandknoppen
  (een per band uit de frequentielijst, meer frequenties op een band = een menuutje; de band van de radio oranje), chat, waterval met
  TX-drive (demping in dB, `[audio] tx_atten`). De ontvang/zend-lijm is die van `CwWindow.Bleeps.cs` van ShackAssistant
  (ontvanger op een eigen draad, wachtrij, chatlog `Documenten\Bleeps\chat\JJJJ-MM-DD.txt`, eigen echo bij full duplex).
- **De waterval zoals WSJT-X** (v1.0.1; sinds v1.0.4 staat de code in het gedeelde `BleepsWaterfall.cs`, INI hier `[waterfall]` zonder voorvoegsel, PA5DJ: *"het moet echt richting de resolutie van wsjtx"*): `MainForm.WaterfallStep`
  - venster 65536 op 48 kHz (= 16384 op 12 kHz, 0,73 Hz), elke `WfHop` (3840 = 80 ms) nieuwe audio een spectrum, `[waterfall] avg`
  (5) ervan in VERMOGEN gemiddeld per rij, per pixel alle bins opgeteld (nooit een bin kiezen). Kleur: `FloorDb` = ruis + 7 + `floor`
  (0), `CurveDb` = `span` (30) - de Skim-curve van `WaterfallPanel` begint 7 dB onder FloorDb; met de 11 dB van ShackAssistant werd
  elk station wit. CeilingDb doet in deze stand niets. Gemeten in scratchpad `bleepswf` (nep-band: -10/0/+10 dB Bleeps + draaggolf).
  - **Bediening onder de waterval (v1.0.2)** zoals WSJT-X: Bins/Pixel (`bpp`, 4), Start (`start`, 100), N Avg (`avg`, 5 = de
    SNELHEID), Gain (`gain`: span = 30 / 10^(0,015 x Gain) dB, WSJT-X's stap) en Zero (`zero`: vloer -0,2 dB per stap). Het bereik
    is Start .. Start + breedte x Bins/Pixel x 0,73 Hz (max 6 kHz, `ApplyWfRange`, ook bij Resize); Bins/Pixel en Start wissen het beeld.
- `Rig.cs` - `RigConfig` (EEN radio, `[radio]` in de INI) en `RigControl`: Hamlib op EEN eigen draad met een wachtrij (Hamlib is niet
  thread-safe). Model 2 = rigctld, 4 = FLRig (poort `host:port`); geen radio + PTT RTS/DTR = Hamlibs dummy-rig met alleen de PTT-poort.
  `SetPtt` WACHT tot het gedaan is (de audio start na PTT). Mode bij afstemmen: None / USB / Data (PKTUSB).
- `Frequencies.cs` - `FrequencyList` (standaard = het gedeelde `BleepsFrequencies.All` sinds v1.0.5) (`frequencies.json` in `%APPDATA%\Bleeps`), standaard de Bleeps-voorstellen (R1-bandplan, naast
  FT8/FT4, buiten CW/phone/RTTY; QO-100 als uplink 2400.060). `Bands` = bandtabel en frequentietekst ALTIJD met een punt.
- `SettingsForm.cs` - Station (call), Audio (in/uit), Radio (Hamlib-lijst, poort of host:port, seriele lijnen, CI-V, PTT
  VOX/CAT/RTS/DTR + eigen PTT-poort, mode, TX delay/tail, poll), Frequencies (raster zoals WSJT-X: Insert/Delete/Reset).
- `Audio.cs` - WinMM in/uit + `TxPlayer` (PTT altijd los in de finally), zonder de LAN-audio van ShackAssistant.
- `SplashForm.cs` (logo `Splash.png` = `Bleeps-emoji-waves.png`; sinds v1.0.3 staat hij `Program.SplashMs` (3 s) ALLEEN in beeld en verschijnt
  het hoofdvenster pas daarna - eerst opende dat er meteen overheen en leek de splash direct te sluiten), `Log.cs` (`Documenten\Bleeps\debug\Bleeps.log`), `Program.cs`.
- Instellingen: `%APPDATA%\Bleeps\bleeps.ini`.
- **`--rig NAAM`** (v1.0.8, zoals WSJT-X's `--rig-name`): `Instance.cs` - eigen `%APPDATA%\Bleeps - NAAM`, `Documenten\Bleeps - NAAM`,
  mutex en titel. Een nieuwe plek die een map of een vaste naam gebruikt: neem `Instance.FolderName` / `Instance.Suffix`, nooit
  "Bleeps" letterlijk, anders lopen de sessies door elkaar.

## Bouwen
- `INNO\build_bleeps.bat` (publish naar `INNO\build`), `build_bleeps_and_run.bat`, `build_bleeps_setup.bat` (publish + Inno
  `bleeps.iss` + zip + sha256). `ensure_dotnet_sdk.bat` / `ensure_inno_setup.bat` zijn die van ShackAssistant.
- Gemeten in de scratchpad-harnas `bleepsapp` (19): een echte `rigctld -m 1` op 127.0.0.1:45320 - verbinden, bandknop, getypte
  frequentie en band, dial volgt de radio, PTT aan/uit via CAT, een Bleeps-bericht als audio erin -> in de chat en het chatlog,
  de instellingen (305 Hamlib-modellen, 14 frequenties). Nooit de echte kaart of PTT; de harnas ruimt `%APPDATA%\Bleeps` en
  `Documenten\Bleeps` op (en weigert als die al bestaan).
