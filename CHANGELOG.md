# Bleeps Changelog

## v1.0.11

- **De links naar www.ham4life.com en www.pa5dj.nl staan alleen in About** (PA5DJ: *"in help alleen link naar about venster"*): uit het
  Help-menu gehaald; daar staat About Bleeps, dat ze toont.

## v1.0.10

- **Alle comments in het Engels** (PA5DJ: *"alle comment alles moet in het engels zijn"*): de laatste Nederlandse comments
  (`WaterfallPanel.cs`: de kleurkromme en de paletstops) vertaald, en de citaten van PA5DJ in comments (ook in de csproj) nu in het
  Engels. Alleen comments - geen code, het protocol is ongewijzigd.
- **About noemt www.ham4life.com en www.pa5dj.nl** (PA5DJ: *"vermeld www.ham4life.com en www.pa5dj.nl en in about"*): About is nu een
  klein venster met de twee als klikbare links (was een MessageBox), en ze staan ook in het Help-menu.

## v1.0.9

- **Bleeps is een zelfstandig project** (PA5DJ: *"maak van Z:\\PROJECT\\SOFTWARE\\Bleeps en https://github.com/PA5DJ/Bleeps een zelf
  opzich staand project los van shackassistant"*). Wat tot nu toe uit `..\ShackAssistant` gelinkt werd staat nu als EIGEN kopie in de
  repository: `src\Bleeps\Shared\` (het protocol in `Shared\Protocol\`: BleepsCode, BleepsModem, BleepsReceiver, BleepsEmoji; en
  BleepsFrequencies, Fft, SpectrumAnalyzer, HamlibNative, WaterfallPanel, BleepsPanel, BleepsSettingsForm, BleepsWaterfall),
  `src\Bleeps\Twemoji\` (122 plaatjes + LICENSE.txt, CC-BY 4.0) en `hamlib\` (de vier Hamlib-DLL's, LGPL, met COPYING.LIB.txt,
  COPYING.txt, LICENSE.txt en README.w64-bin.txt; COPYING.LIB gaat ook mee naast de exe). De csproj kent geen pad naar ShackAssistant
  meer. Gekopieerd van ShackAssistant v1.0.750 (met de Hamlib-reparatie van v1.0.6).
  Gemeten: een verse kloon in een map ZONDER ShackAssistant ernaast bouwt; de app start.

## v1.0.8

- **Meer Bleeps naast elkaar, zoals WSJT-X** (PA5DJ: *"met een flag op cli sessies naast elkaar willen kunnen starten zoals bij wsjtx
  met --rig IC9700 etc. krijg dan zijn eigen appdata"*): `Bleeps.exe --rig IC9700` (ook `--rig=IC9700`, `--rig-name IC9700`,
  `-r IC9700`) heeft zijn eigen instellingen (`%APPDATA%\Bleeps - IC9700`), log en chatlogs (`Documenten\Bleeps - IC9700`), een eigen
  single-instance-slot en de naam in titel en splash. Zonder de vlag verandert er niets. Een naam die al draait zegt dat; de gewone
  Bleeps noemt bij "already running" de vlag. Een nieuwe naam begint met lege instellingen (zoals WSJT-X).
  `Instance.cs` (lezen + opschonen tot een mapnaam), gebruikt in `Shims.AppSettings.Folder`, `Log.DataFolder`, de mutex en de titels.
  Gemeten: gewone Bleeps + `--rig TESTRIG` draaien samen, elk met eigen titel en mappen; een tweede `--rig=TESTRIG` weigert; de gewone
  `bleeps.ini` byte voor byte terug, de testmappen weer weg.

## v1.0.7

- **Settings > Radio: wat niet van toepassing is wordt grijs, opschrift en vak samen** (PA5DJ: *"items die niet van toepassing bij een
  select zoals CAT, grijs maken"*). Tot nu werd alleen het vak grijs en bleef het opschrift zwart (PTT port bij CAT viel daardoor niet
  op). Nu: PTT CAT -> PTT port; VOX -> PTT port, TX delay en PTT tail (er is geen PTT om op te wachten); geen radio -> baud, data/stop
  bits, handshake, DTR, RTS, CI-V, Mode en Poll every. De waarden blijven bewaard.
  Gemeten op het echte venster (los gebouwd, nooit OK): de vier standen, elk opschrift volgt zijn vak, `bleeps.ini` ongewijzigd.

## v1.0.6

- **Stopte soms direct na de splash** (PA5DJ: *"stopt iedere keer na splash"*): exitcode 1, zonder foutmelding of logregel. Oorzaak in
  Hamlib 4.7.2: `rig_register` doet `exit(1)` ("Hash collision!!! Fatal error!!") als een backend twee keer registreert - en dat
  gebeurde als de UI-draad de modellenlijst laadde terwijl de radiodraad `rig_init` deed. Gerepareerd in het gedeelde `HamlibNative.cs`
  van ShackAssistant (SA v1.0.750): eerst de modellenlijst, dan `rig_init` onder hetzelfde slot. Gemeten: 5 starts met de IC-9700 op
  COM3, alle 5 blijven draaien.

## v1.0.5

- De standaard-frequentielijst komt uit het gedeelde `BleepsFrequencies.cs` van ShackAssistant (Mode Master's bandknoppen, SA v1.0.748): een lijst voor beide programma's.

## v1.0.4

- De waterval is nu het GEDEELDE `Forms/BleepsWaterfall.cs` van ShackAssistant (dat Mode Master in Bleeps sinds v1.0.747 ook gebruikt): zelfde beeld en bediening in beide programma's, een kopie van de code.

## v1.0.3

- De splash blijft 3 s alleen in beeld; het hoofdvenster verschijnt pas daarna (PA5DJ: hij verdween meteen - het hoofdvenster opende eroverheen).

## v1.0.2

- Waterval-bediening zoals de Wide Graph van WSJT-X (PA5DJ): Bins/Pixel (standaard 4 = 2,9 Hz/px), Start (Hz), N Avg (de snelheid: spectra per regel, 5 = een regel per 0,4 s), Gain (contrast) en Zero (helderheid); bereik en Hz/px ernaast, alles bewaard in `[waterfall]`.

## v1.0.1

- Waterval met de resolutie van WSJT-X (PA5DJ): venster 1,37 s (65536 punten, 0,73 Hz per bin), elke 80 ms een spectrum, 5 gemiddeld per rij (N Avg), per pixel het vermogen van alle bins opgeteld (Bins/Pixel), kleuren over 30 dB vanaf de ruis (`[waterfall] floor`/`span`/`avg` in de INI).

## v1.0.0

- Eerste losse versie van Bleeps (PA5DJ): chat en waterval van ShackAssistant (zelfde protocol, gelinkte bronnen), radiobalk zoals WSJT-X met bandlijst, dial en bandknoppen, frequentiebeheer (Settings -> Frequencies), Hamlib met elk model en rigctld/FLRig (een radio, geen profielen), PTT via CAT/RTS/DTR/VOX, splash met het Bleeps-logo, Inno-installer en .bat-bestanden.
