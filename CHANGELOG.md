# Bleeps Changelog

## v1.0.3

- De splash blijft 3 s alleen in beeld; het hoofdvenster verschijnt pas daarna (PA5DJ: hij verdween meteen - het hoofdvenster opende eroverheen).

## v1.0.2

- Waterval-bediening zoals de Wide Graph van WSJT-X (PA5DJ): Bins/Pixel (standaard 4 = 2,9 Hz/px), Start (Hz), N Avg (de snelheid: spectra per regel, 5 = een regel per 0,4 s), Gain (contrast) en Zero (helderheid); bereik en Hz/px ernaast, alles bewaard in `[waterfall]`.

## v1.0.1

- Waterval met de resolutie van WSJT-X (PA5DJ): venster 1,37 s (65536 punten, 0,73 Hz per bin), elke 80 ms een spectrum, 5 gemiddeld per rij (N Avg), per pixel het vermogen van alle bins opgeteld (Bins/Pixel), kleuren over 30 dB vanaf de ruis (`[waterfall] floor`/`span`/`avg` in de INI).

## v1.0.0

- Eerste losse versie van Bleeps (PA5DJ): chat en waterval van ShackAssistant (zelfde protocol, gelinkte bronnen), radiobalk zoals WSJT-X met bandlijst, dial en bandknoppen, frequentiebeheer (Settings -> Frequencies), Hamlib met elk model en rigctld/FLRig (een radio, geen profielen), PTT via CAT/RTS/DTR/VOX, splash met het Bleeps-logo, Inno-installer en .bat-bestanden.
