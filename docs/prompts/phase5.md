# Aufgabe: EV↔EVSE-Simulation — SDP, TCP/TLS, Zustandsmaschinen, Interop (Phase 5)

## Kontext

Du arbeitest im Repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — eine .NET-10-Bibliothek
für ISO 15118 EXI. Stand nach Phase 0–4:

- EXI-Primitive komplett (Value Tables, Signed/Binary/Boolean).
- Generierte, gegen cbV2G byte-validierte Codecs: AppProtocol (SAP),
  `Vanaheimr.V2G.Exi.Iso15118_2` (alle 17 Nachrichtenpaare),
  `Vanaheimr.V2G.Exi.Iso15118_20.{CommonMessages,DC,AC}`.
- XMLDSig über EXI-Fragmente (-2: P-256/SHA-256; -20: secp521r1/SHA-512).
- V2GTP-Layer mit Payload-Type-Dispatcher (SAP / -2 / -20-Sets).
- `tools/cbv2g-ref/` (libcbv2g-Harness, gepinnt), vektorgetriebene NUnit-Suite.

Lies vor Beginn: `README.md`, `docs/`, den V2GTP-Dispatcher und die öffentlichen
APIs der generierten Codec-Assemblies.

## Vorbedingungen (zuerst prüfen)

Phase 0–4 abgeschlossen (insbesondere: -2 und -20 Codecs vektorvalidiert,
Dispatcher vorhanden). Fehlt etwas: stoppe und melde.

## Ziel

Eine simulierte Ladesession zwischen einem EVCC (EV-Seite) und einem SECC
(EVSE-Seite) läuft vollständig durch — erst unsere beiden Seiten gegeneinander,
dann im Interop gegen einen unabhängigen Stack (Josev). Damit ist bewiesen,
dass Codec, V2GTP, Discovery, Sequenzen und Timing zusammen funktionieren.

**Scope-Grenzen (verbindlich):**
- KEIN SLAC/PLC (Schicht darunter; Simulation läuft über normales TCP/IP).
- Identifikation: EIM (External Identification Means). Plug&Charge-Vertragszertifikate
  sind Stretch-Goal, kein DoD-Kriterium.
- Kein Pause/Resume, keine Renegotiation, kein Smart-Charging-Detail — Happy Path.

## Schritte

### 1. Neues Projekt `Vanaheimr.V2G.Simulation` (+ CLI)

- Library mit drei Schichten, sauber getrennt und einzeln testbar:
  a) **Transport**: SDP-Client/-Server (UDP), TCP-Listener/-Client, optional TLS
     (`SslStream`), V2GTP-Framing (Dispatcher aus Phase 4 nutzen).
  b) **Session**: Request/Response-Abwicklung, SessionID-Verwaltung,
     Sequenz-Timeouts (Defaults aus den Spec-Timing-Tabellen, konfigurierbar),
     ResponseCode-Auswertung.
  c) **Zustandsmaschinen**: EVCC und SECC als explizite Zustands-Enums mit
     Übergangstabellen (kein implizites async-Spaghetti) — jede Transition
     einzeln unit-testbar.
- Dazu `Vanaheimr.V2G.Simulation.Cli` mit Subcommands `evcc` und `secc`
  (Parameter: Interface/Adresse, Protokollwahl -2/-20, AC/DC, TLS an/aus,
  Log-Verzeichnis).
- „Ladephysik" hinter Interfaces mocken (`IEvBattery`, `IEvsePowerSupply`):
  PreCharge/CableCheck/ChargeLoop müssen nach n Iterationen konvergieren,
  damit Tests deterministisch terminieren.

### 2. SDP (SECC Discovery Protocol)

- UDP, IPv6; SDP-Request/-Response als V2GTP-Frames mit den SDP-Payload-Types.
  Exakte Payload-Type-IDs, Ports und das Security/TransportProtocol-Byte-Layout
  aus der Spec bzw. libcbv2g/Josev übernehmen — nicht raten.
- Für Tests: konfigurierbares Interface + Port; Loopback muss funktionieren.
  Link-Local-Multicast unter Windows ist zickig (Interface-Index nötig) —
  implementieren, aber Tests dürfen auf Loopback/Unicast ausweichen.

### 3. Session-Ablauf (Happy Paths)

- **-2 AC (EIM):** SAP → SessionSetup → ServiceDiscovery → PaymentServiceSelection
  → Authorization (Polling bis OK) → ChargeParameterDiscovery → PowerDelivery(Start)
  → ChargingStatus-Loop → PowerDelivery(Stop) → SessionStop.
- **-2 DC (EIM):** … → ChargeParameterDiscovery → CableCheck-Loop → PreCharge-Loop
  → PowerDelivery(Start) → CurrentDemand-Loop → PowerDelivery(Stop)
  → WeldingDetection → SessionStop.
- **-20 DC:** SAP → SessionSetup → AuthorizationSetup → Authorization →
  ServiceDiscovery → ServiceDetail → ServiceSelection → DC_ChargeParameterDiscovery
  → ScheduleExchange → DC_CableCheck → DC_PreCharge → PowerDelivery(Start)
  → DC_ChargeLoop → PowerDelivery(Stop) → DC_WeldingDetection → SessionStop.
- **-20 AC** analog mit AC_ChargeParameterDiscovery/AC_ChargeLoop.
- Timeout-/Fehlerpfade minimal: Sequence-Timeout bricht Session sauber ab,
  FAILED-ResponseCode beendet mit klarer Diagnose. Mehr nicht.

### 4. TLS

- -2: TLS optional (Session auch ohne TLS lauffähig — Josev kann das für Tests).
- -20: TLS vorgesehen; implementiere Server-seitiges TLS mit selbstsignierten
  Testzertifikaten (einchecken unter `Tests/TestData/`, klar "test only").
  Mutual TLS: dokumentierte Lücke, kein DoD-Kriterium.
- Cipher-Suite-Anforderungen der Specs dokumentieren; was Schannel/.NET nicht
  hergibt, als bekannte Abweichung festhalten statt erzwingen.

### 5. Logging + Capture als Vektorquelle

- Jeder gesendete/empfangene Frame wird strukturiert geloggt: Hex + dekodierte
  Nachricht + Zeitstempel.
- **Record-Mode**: empfangene EXI-Streams werden als Vektor-Kandidaten unter
  `Tests/Vectors/captured/` abgelegt (gleiches JSON-Format, Quelle vermerkt).
  Frames von Josev sind unabhängig erzeugte Konformitätsvektoren — die wertvollsten
  überhaupt. Kuratierte Übernahme in die regulären Vektordateien vorbereiten.

### 6. Tests in zwei Stufen

- **Stufe 1 (CI, Standard-`dotnet test`):** In-Process-E2E — unser EVCC gegen
  unser SECC über Loopback-TCP (oder In-Memory-Duplex-Stream): alle vier Happy
  Paths (-2 AC, -2 DC, -20 AC, -20 DC) laufen bis SessionStop durch;
  Assertions auf Zustandsfolge und finale ResponseCodes. Zusätzlich
  Unit-Tests für SDP-Framing und einzelne Zustandsübergänge (inkl. Timeout).
- **Stufe 2 (opt-in, per Env-Var/Testkategorie `Interop`):** gegen
  **Josev** (SwitchEV/iso15118 bzw. der EVerest-Fork ext-switchev-iso15118):
  - unser EVCC ↔ Josev-SECC und Josev-EVCC ↔ unser SECC,
  - zuerst -2 AC EIM ohne TLS, dann -2 DC, dann -20.
  - Setup: WSL2 oder Docker (Josev braucht Python + JRE für seinen EXI-Codec);
    lege unter `tools/interop-josev/` Setup-Skripte + README mit gepinnter
    Josev-Version ab. Diese Tests laufen NICHT im Standard-CI-Lauf.
  - Jeder erfolgreiche Interop-Lauf: vollständiges Frame-Log als Artefakt
    einchecken (`docs/interop-runs/<datum>-<szenario>/`).

## Leitplanken

- Codec-/Generator-Code wird in dieser Phase nur angefasst, wenn ein
  Interop-Lauf einen konkreten Wire-Diff nachweist — dann gilt wie immer:
  Diff analysieren, Ursache fixen, Vektor als Regressionstest einchecken.
- `dotnet test -c Release` (Stufe 1) bleibt ohne Python/Java/Docker/Netzwerk
  jenseits von Loopback lauffähig.
- Zustandsmaschinen synchron testbar halten: Zeit über eine injizierbare
  Uhr/Timer-Abstraktion, nie `Task.Delay` fest verdrahtet.
- Alle Bestandstests bleiben grün. Kleine Commits, nur bei grünem Build.
- Sicherheit: selbstsignierte Testzertifikate und Testschlüssel niemals als
  produktionstauglich darstellen; keine echten Zertifikate einchecken.

## Definition of Done

1. Vier In-Process-E2E-Happy-Paths (-2 AC/DC, -20 AC/DC) grün im Standard-Testlauf.
2. SDP-Discovery funktioniert (Unit-Tests + im E2E-Pfad verwendet).
3. TLS-Variante für -2 und -20 mit Testzertifikaten lauffähig (mind. ein
   E2E-Test pro Protokoll mit TLS).
4. Interop dokumentiert: mindestens -2 AC EIM in BEIDEN Richtungen gegen
   Josev@<version> erfolgreich, Frame-Logs als Artefakte eingecheckt;
   -2 DC und -20 Interop-Ergebnisse (auch Teilerfolge) ehrlich dokumentiert.
5. Record-Mode liefert Josev-Frames als Vektor-Kandidaten; mindestens eine
   kuratierte Übernahme in die regulären Vektordateien.
6. CLI (`evcc`/`secc`) dokumentiert im README; Architekturkapitel Simulation.
7. Abschlussbericht: gefundene Codec-/Sequenz-Diskrepanzen, Timing-Erkenntnisse,
   bekannte Lücken (mutual TLS, PnC, WPT/ACDP).
   