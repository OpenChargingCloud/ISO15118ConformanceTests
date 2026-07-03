# Aufgabe: ISO 15118-20 — Multi-Schema-Codecs + V2GTP-Dispatch (Phase 4)

## Kontext

Du arbeitest im Repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — eine .NET-10-Bibliothek
für ISO 15118 EXI. Stand nach Phase 0–3:

- `Vanaheimr.V2G.Exi.Prototype/` — EXI-Primitive (inkl. String Value Tables, Signed
  Integer, Binary), V2GTP-Header, handgeschriebener AppProtocol-Codec (Referenz).
- `Vanaheimr.V2G.Exi.SourceGenerator/` — Roslyn-Generator: sammelt alle `.xsd` eines
  Projekts als EIN Schemaset, beherrscht import/choice/extension/substitutionGroup/
  Attribute/unbounded, emittiert Dokument- UND Fragment-Codecs.
- `Vanaheimr.V2G.Exi.Iso15118_2/` — generierter -2-Codec, alle 17 Nachrichtenpaare
  gegen cbV2G validiert; XMLDSig-Signaturen (EXI-Fragmente, ECDSA P-256/SHA-256)
  mit `V2GSignatureBuilder`/`V2GSignatureVerifier`.
- `Vanaheimr.V2G.Exi.Tests/` — NUnit, vektorgetrieben; `tools/cbv2g-ref/` CLI-Harness
  um libcbv2g (gepinnter Commit) mit appHand- und iso-2-Modulen.
- Docs: `docs/xsd-inventory-15118-2.md`, `docs/xsd-to-csharp-mapping.md`.

Lies vor Beginn: `README.md`, beide docs, die Generator-Architektur und wie
`Vanaheimr.V2G.Exi.Iso15118_2` die XSDs als AdditionalFiles einbindet.

## Vorbedingungen (zuerst prüfen)

Phase 2 und 3 abgeschlossen (voller -2-Codec inkl. Fragment-Maschinerie,
cbv2g-ref-Harness baut). Fehlt etwas: stoppe und melde.

## Fachlicher Hintergrund: was bei -20 anders ist

- **Kein V2G_Message-Wrapper.** Jede Nachricht ist ein eigenes globales Element;
  der Header (SessionID hexBinary, TimeStamp unsignedLong, optionale Signature)
  steckt IN der Nachricht.
- **Mehrere unabhängige Schemasets**, je eines pro Namespace:
  CommonMessages, AC, DC, WPT, ACDP — alle importieren CommonTypes + xmldsig.
  Jedes Set hat seine EIGENE EXI-Dokumentgrammatik. Der Empfänger erkennt das
  Set am **V2GTP-Payload-Type** (pro Message-Set eine eigene ID; die konkreten
  Werte aus der Spec bzw. libcbv2gs `exi_v2gtp.h` übernehmen — nicht raten).
- Neue Datentyp-Muster, u. a. `RationalNumberType` (Exponent+Value, Pendant zu
  PhysicalValueType) und deutlich größere/verschachteltere Nachrichten
  (ChargeLoop, ScheduleExchange).

## Ziel

Die Schemasets **CommonMessages, DC und AC** sind vollständig generiert und
vektorvalidiert; ein V2GTP-Dispatcher wählt anhand des Payload-Types den
richtigen Decoder. **WPT und ACDP sind explizit außer Scope** (Architektur muss
sie aber ohne Umbau aufnehmen können — der Beweis ist, dass ein weiteres
Schemaset nur ein neues csproj + Vektoren bedeutet).

## Schritte

### 1. Schemata beschaffen + Inventur

- Die -20-XSDs (V2G_CI_CommonMessages, V2G_CI_CommonTypes, V2G_CI_AC, V2G_CI_DC
  + xmldsig) liegen im OSS-Umfeld vor (cbexigen-Repo bzw. EVerest libiso15118);
  Quelle + Commit dokumentieren. Nicht auffindbar → stoppe und melde.
- Inventur-Analyse wie in Phase 2: alle tatsächlich verwendeten XSD-Konstrukte
  und Facetten der -20-Schemata auflisten → `docs/xsd-inventory-15118-20.md`.
  Der Diff gegen das -2-Inventar ist deine Arbeitsliste für Generator-Lücken.

### 2. Generator-Lücken schließen (konstruktweise)

- Für jedes Konstrukt aus dem Inventar-Diff: synthetisches Mini-XSD +
  Grammatik-Unit-Test + Emitter-Support, dann weiter. Fail-loud-Philosophie
  beibehalten (unbekanntes Konstrukt = Build-Diagnostic).
- Erwartbare Kandidaten (verbindlich ist die Inventur): tiefere choice-Schachtelung,
  große maxOccurs-Werte, zusätzliche Built-ins. Nichts auf Vorrat implementieren.

### 3. Projektstruktur: ein Assembly pro Message-Set

- Neue Projekte `Vanaheimr.V2G.Exi.Iso15118_20.CommonMessages`, `….DC`, `….AC`
  (net10.0), jeweils mit eigenem XSD-Satz (Set-XSD + CommonTypes + xmldsig) als
  AdditionalFiles + Generator-Referenz.
- Bewusster Tradeoff: Die CommonTypes-Typen werden dadurch pro Assembly dupliziert
  (cbV2G macht dasselbe). Das ist ok — dokumentiere es in
  `docs/xsd-to-csharp-mapping.md`. KEINE geteilte CommonTypes-Assembly bauen;
  die Grammatiken sind pro Set eigenständig, geteilter Code schafft nur
  Versionierungsprobleme.

### 4. V2GTP-Dispatcher

- `V2GTP`-Layer erweitern: Payload-Type-Tabelle (SAP, -2, -20-CommonMessages,
  -20-AC, -20-DC; Werte aus Spec/libcbv2g), `TryDecode` liefert das typisierte
  Nachrichtenobjekt + Set-Kennung; Encode-Seite setzt den Payload-Type passend
  zum übergebenen Nachrichtentyp.
- Tests: korrektes Mapping pro Set, sauberer Fehler bei unbekanntem Payload-Type,
  Längenfeld-Validierung.

### 5. Vektorvalidierung gegen cbV2G

- `tools/cbv2g-ref/` um die iso-20-Module erweitern (libcbv2g hat pro Set
  eigene encoder/decoder).
- Vektordateien pro Set (`Iso15118_20.CommonMessages.vectors.json`, …), Muster
  wie gehabt (referenceEncoder gepinnt, encode-Diff, decode von cbV2G-Bytes,
  Roundtrip).
- Abdeckung CommonMessages: SessionSetup, AuthorizationSetup, Authorization
  (EIM- und PnC-Variante), ServiceDiscovery, ServiceDetail, ServiceSelection,
  ScheduleExchange (Scheduled + Dynamic Mode!), PowerDelivery, SessionStop —
  plus die restlichen Paare des Schemas. DC: ChargeParameterDiscovery,
  CableCheck, PreCharge, ChargeLoop, WeldingDetection. AC: ChargeParameterDiscovery,
  ChargeLoop. Pro Nachricht: Happy Path + optionale-Feld-Varianten + Grenzwerte.
- Die komplexen zuerst (ScheduleExchangeRes Dynamic/Scheduled,
  DC_ChargeLoop mit DisplayParameters) — sie finden die meisten Lücken.

### 6. Signaturen auf -20 heben

- Fragment-Encoder für das CommonMessages-Set generieren (Maschinerie aus
  Phase 3 wiederverwenden); Fragment-Bytes gegen EXIficient diffen.
- Signatur-Suite: -20 verwendet stärkere Suiten als -2. Implementiere die
  ECDSA-Variante, die .NET nativ kann (secp521r1/SHA-512); falls die Spec
  zusätzlich Ed448 vorsieht: NICHT implementieren, sondern als bekannte Lücke
  dokumentieren (.NET hat kein Ed448).
- `RationalNumberType`-Helper analog PhysicalValueType (decimal-Konvertierung,
  Rundungstests).

### 7. Dokumentation

- README: Architekturbild (Assemblies pro Set, Dispatcher), Abdeckungsmatrix
  -20 (Nachricht × validiert-gegen), bekannte Lücken (WPT/ACDP, ggf. Ed448),
  "Next milestones" → Phase 5 (Simulation).

## Leitplanken

- Wire-Semantik nur auf Basis konkreter Diffs gegen cbV2G/EXIficient ändern.
- Kein handgeschriebener Codec-Code für -20 — alles durch den Generator;
  Generator-Fixes immer mit Mini-XSD-Grammatik-Test.
- `dotnet test -c Release` bleibt ohne C-Toolchain/Java/Netzwerk grün.
- Sämtliche Bestandstests (-2, AppProtocol, Grammatik-Tests) bleiben grün.
- Achte auf Buildzeit: der generierte Code wird groß; Output weiter in mehrere
  Hint-Files splitten, Generator-Pipeline inkrementell sauber halten
  (keine unnötigen Neuberechnungen pro Edit).
- Kleine Commits, nur bei grünem Build.

## Definition of Done

1. `docs/xsd-inventory-15118-20.md` existiert; Generator läuft ohne Diagnostics
   über CommonMessages-, DC- und AC-Set; drei Assemblies kompilieren.
2. Alle Nachrichtenpaare der drei Sets: encode/decode/roundtrip gegen
   cbV2G@<sha>, beide Richtungen, Vektoren eingecheckt.
3. V2GTP-Dispatcher mit Payload-Type-Tests (inkl. Fehlerfälle).
4. Fragment-Bytes (CommonMessages) byte-gleich mit EXIficient@<version>;
   secp521r1/SHA-512-Signatur erzeugen + verifizieren getestet.
5. RationalNumber-Helper mit Rundungstests.
6. Bestandstests grün; README + docs aktualisiert.
7. Abschlussbericht: Generator-Lücken aus dem Inventar-Diff, Orakel-Entscheidungen,
   dokumentierte bewusste Lücken (WPT/ACDP, ggf. Ed448).
   