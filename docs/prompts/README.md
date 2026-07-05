# Phasen-Prompts für die ISO-15118-EXI-Umsetzung

Einsatzfertige, selbsterklärende Prompts für autonome Agent-Läufe (Opus/Claude Code).
Jeder Prompt ist eigenständig lesbar, prüft seine Vorbedingungen selbst und definiert
eine Definition of Done. Die Phasen bauen strikt aufeinander auf — in Reihenfolge
abarbeiten. Gesamtplan und Standortbestimmung: [../roadmap.md](../roadmap.md).

| Phase | Datei | Inhalt | Status |
|---|---|---|---|
| 0 | [phase0.md](phase0.md) | SAP-Seed-Vektoren durch cbV2G-Referenzoutput ersetzen (Wire-Konformität) | **erledigt @2026-07-03** |
| 1 | [phase1.md](phase1.md) | EXI-Primitivschicht vervollständigen (String Value Tables, Signed Integer, Binary, Boolean) | **erledigt @2026-07-03** (Value Tables: miss-only encode + lenient decode, cbV2G nutzt keine Tables) |
| 2 | [phase2.md](phase2.md) | Source Generator auf den realen ISO-15118-2-Schemasatz heben (import/choice/extension/substitutionGroup/Attribute) | **erledigt @2026-07-05** (ganzer Satz generiert + kompiliert; SessionSetup/ServiceDiscovery byte-exakt gegen cbV2G) |
| 3 | [phase3.md](phase3.md) | ISO 15118-2 komplettieren (alle 17 Nachrichtenpaare) + XMLDSig über EXI-Fragmente | offen |
| 4 | [phase4.md](phase4.md) | ISO 15118-20: Multi-Schema-Codecs (CommonMessages/DC/AC) + V2GTP-Dispatch | offen |
| 5 | [phase5.md](phase5.md) | EV↔EVSE-Simulation: SDP, TCP/TLS, Zustandsmaschinen, Interop gegen Josev | offen |

Nach Abschluss einer Phase: Status-Spalte hier aktualisieren (z. B. „erledigt @<commit/datum>")
und dabei prüfen, ob die Kontext-Abschnitte der Folge-Prompts noch zum tatsächlichen
Repo-Stand passen — sie beschreiben den erwarteten Zustand nach der Vorphase.
