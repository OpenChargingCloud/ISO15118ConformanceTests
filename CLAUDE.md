# Vanaheimr.V2G.Exi

.NET-10-Bibliothek für ISO 15118 EXI: Parsen und Serialisieren von 15118-2- und
15118-20-Nachrichten, Endziel EV↔EVSE-Simulation.

## Orientierung

- **Gesamtplan / Standortbestimmung:** `docs/roadmap.md`
- **Phasen-Prompts für Agent-Läufe (Phase 0–5):** `docs/prompts/` (Index: `docs/prompts/README.md`)
- Projektstruktur und aktueller Prototyp-Stand: `README.md`

## Bauen & Testen

```
dotnet test -c Release
```

Muss ohne C-Toolchain, Java oder Netzwerk grün laufen — externe Referenz-Encoder
(cbV2G, EXIficient) dienen nur der Vektor-(Re-)Generierung, nie dem Testlauf selbst.

## Grundregeln

- Wire-Semantik nie spekulativ ändern — nur auf Basis eines konkreten Byte-Diffs
  gegen einen Referenz-Encoder (Vektordateien unter `Vanaheimr.V2G.Exi.Tests/Vectors/`).
- Source Generator: Fail-loud-Philosophie — unbekannte XSD-Konstrukte erzeugen
  Build-Diagnostics, werden nie still übersprungen.
- Kein handgeschriebener Codec-Code für -2/-20; alles läuft durch den Generator.
  Der handgeschriebene AppProtocol-Codec bleibt als Diff-Referenz bestehen.
