# Bleeps (los programma) - CLAUDE.md

PA5DJ, 19 november 2026: *"Bouw een los staande versie van bleeps"* - met Inno en .bat-bestanden, volledige Hamlib-verbinding ook
via rigctld (geen profielen), het Bleeps-logo als splash (`Bleeps-emoji-waves.png`), en frequentiebeheer + bandknoppen zoals WSJT-X.

Antwoord in het Nederlands. Pushen alleen op verzoek (er is nog geen remote).

## Wat het is
WinForms, .NET 10 (`src\Bleeps\Bleeps.csproj`, `src\Bleeps.slnx`). Versie in `MainForm.Version` + `AssemblyVersion/FileVersion`
in de csproj + een sectie in `CHANGELOG.md`; bump bij elke wijziging en commit daarna.

## GEDEELD met ShackAssistant - NIET gekopieerd
De csproj LINKT deze bestanden uit `..\ShackAssistant\src\ShackAssistant\` (`$(SaSrc)`):
- het protocol: `Helpers\Audio\Bleeps\BleepsCode.cs`, `BleepsModem.cs`, `BleepsReceiver.cs`, `BleepsEmoji.cs` (+ de Twemoji-PNG's);
- `Helpers\Audio\Fft.cs`, `SpectrumAnalyzer.cs`, `Helpers\HamlibNative.cs`;
- `Forms\WaterfallPanel.cs`, `Forms\BleepsPanel.cs` (chat + TX-regel), `Forms\BleepsSettingsForm.cs`.
EEN kopie van het protocol: anders lopen de twee programma's uit elkaar en decoderen ze elkaar niet meer.
**Een wijziging in die bestanden in ShackAssistant moet hier ook bouwen.** Wat ze van ShackAssistant aanroepen
(`AppSettings`, `AppTheme`, `UITheme`) staat nagebouwd in `Shims.cs` (zelfde namen en leden, namespace `ShackAssistant.Helpers`).
Hamlib-DLL's komen uit `..\ShackAssistant\hamlib-w64\bin` en staan naast de exe in `hamlib\` (HamlibNative kijkt daar eerst).

## Eigen code (`src\Bleeps`)
- `MainForm.cs` - radiobalk zoals WSJT-X (bandlijst die ook een getypte frequentie/band neemt, dial "14.071 000", mode), bandknoppen
  (een per band uit de frequentielijst, meer frequenties op een band = een menuutje; de band van de radio oranje), chat, waterval met
  TX-drive (demping in dB, `[audio] tx_atten`). De ontvang/zend-lijm is die van `CwWindow.Bleeps.cs` van ShackAssistant
  (ontvanger op een eigen draad, wachtrij, chatlog `Documenten\Bleeps\chat\JJJJ-MM-DD.txt`, eigen echo bij full duplex).
- `Rig.cs` - `RigConfig` (EEN radio, `[radio]` in de INI) en `RigControl`: Hamlib op EEN eigen draad met een wachtrij (Hamlib is niet
  thread-safe). Model 2 = rigctld, 4 = FLRig (poort `host:port`); geen radio + PTT RTS/DTR = Hamlibs dummy-rig met alleen de PTT-poort.
  `SetPtt` WACHT tot het gedaan is (de audio start na PTT). Mode bij afstemmen: None / USB / Data (PKTUSB).
- `Frequencies.cs` - `FrequencyList` (`frequencies.json` in `%APPDATA%\Bleeps`), standaard de Bleeps-voorstellen (R1-bandplan, naast
  FT8/FT4, buiten CW/phone/RTTY; QO-100 als uplink 2400.060). `Bands` = bandtabel en frequentietekst ALTIJD met een punt.
- `SettingsForm.cs` - Station (call), Audio (in/uit), Radio (Hamlib-lijst, poort of host:port, seriele lijnen, CI-V, PTT
  VOX/CAT/RTS/DTR + eigen PTT-poort, mode, TX delay/tail, poll), Frequencies (raster zoals WSJT-X: Insert/Delete/Reset).
- `Audio.cs` - WinMM in/uit + `TxPlayer` (PTT altijd los in de finally), zonder de LAN-audio van ShackAssistant.
- `SplashForm.cs` (logo `Splash.png` = `Bleeps-emoji-waves.png`), `Log.cs` (`Documenten\Bleeps\debug\Bleeps.log`), `Program.cs`.
- Instellingen: `%APPDATA%\Bleeps\bleeps.ini`.

## Bouwen
- `INNO\build_bleeps.bat` (publish naar `INNO\build`), `build_bleeps_and_run.bat`, `build_bleeps_setup.bat` (publish + Inno
  `bleeps.iss` + zip + sha256). `ensure_dotnet_sdk.bat` / `ensure_inno_setup.bat` zijn die van ShackAssistant.
- Gemeten in de scratchpad-harnas `bleepsapp` (19): een echte `rigctld -m 1` op 127.0.0.1:45320 - verbinden, bandknop, getypte
  frequentie en band, dial volgt de radio, PTT aan/uit via CAT, een Bleeps-bericht als audio erin -> in de chat en het chatlog,
  de instellingen (305 Hamlib-modellen, 14 frequenties). Nooit de echte kaart of PTT; de harnas ruimt `%APPDATA%\Bleeps` en
  `Documenten\Bleeps` op (en weigert als die al bestaan).
