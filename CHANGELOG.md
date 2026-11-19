# Bleeps Changelog

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
