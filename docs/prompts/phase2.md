# Aufgabe: Source Generator auf die reale ISO-15118-2-Schemawelt heben (Phase 2)

> **Update nach Phase 0 (2026-07-03):** Das EXI-Grammatik-Modell wurde bereits
> korrigiert. GrammarBuilder und CodecEmitter erzeugen jetzt die **nicht-strikte**
> schema-informed Grammatik von cbexigen/cbV2G (2-Bit-Dokument-Selektor; pro simplem
> Feld SE-, Value-Start- und EE-Event-Bit; 2-Bit-Loop/Optional-Codes; Enum-Index =
> XSD-Deklarationsreihenfolge; unsignedByte → nbit(8)). Details in
> `docs/roadmap.md`, README-Abschnitt „The wire model", und der Memory-Notiz
> `exi-grammar-model-nonstrict`. Der „echte Grammatikbau nach §8.5.4" in dieser
> Phase baut darauf auf, statt ihn neu zu entdecken — verifiziere weiterhin jedes
> Konstrukt gegen cbV2G.

## Kontext

Du arbeitest im Repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — eine .NET-10-Bibliothek,
die ISO 15118-2 und 15118-20 EXI-Nachrichten parsen und serialisieren soll. Architektur:

- `Vanaheimr.V2G.Exi.Prototype/` — EXI-Primitive (BitReader/BitWriter, ExiPrimitives),
  V2GTP-Header, handgeschriebener SupportedAppProtocol-Codec (bleibt unangetastet,
  dient als Referenz für Diff-Tests).
- `Vanaheimr.V2G.Exi.SourceGenerator/` — Roslyn `IIncrementalGenerator` (netstandard2.0):
  `Xsd/XsdReader.cs` (XSD-Parser), `Grammar/GrammarBuilder.cs` (XSD → Grammatikplan),
  `Emit/CodecEmitter.cs` (Plan → C#). Versteht heute nur die Mini-Teilmenge, die das
  AppProtocol-Schema braucht: globale Elemente, sequence, simpleType-Restriktionen,
  bounded Wiederholung. Philosophie: unbekannte Konstrukte führen zu lautem
  Build-Diagnostic, nie zu stillem Überspringen — beibehalten!
- `Vanaheimr.V2G.Exi.Tests/` — NUnit, vektorgetrieben (JSON + bitgenauer Hex-Diff).

Lies vor Beginn: `README.md`, den kompletten SourceGenerator, den handgeschriebenen
AppProtocol-Codec (die XML-Doc-Kommentare erklären das EXI-Grammatikmodell) und die
Testinfrastruktur.

## Vorbedingungen (zuerst prüfen)

1. **Phase 0**: Unter `tools/cbv2g-ref/` existiert ein CLI-Harness um libcbv2g
   (EVerest, gepinnter Commit) für differenzielle Vektoren.
2. **Phase 1**: `ExiPrimitives` beherrscht Signed Integer, Binary (hex/base64Binary),
   Boolean und String Value Tables (lokal/global) mit Stream-Kontext.

Fehlt eine der beiden Vorbedingungen ganz oder teilweise: **stoppe und melde es**,
statt sie nebenbei mitzubauen — sie sind eigene Arbeitspakete.

## Ziel

Der Generator übersetzt den vollständigen ISO-15118-2-Schemasatz
(`V2G_CI_MsgDef.xsd` + `V2G_CI_MsgHeader.xsd` + `V2G_CI_MsgBody.xsd` +
`V2G_CI_MsgDataTypes.xsd` + `xmldsig-core-schema.xsd`) in ein neues Assembly
`Vanaheimr.V2G.Exi.Iso15118_2`, und die ersten Nachrichten (mindestens
SessionSetupReq/Res, ServiceDiscoveryReq/Res) sind byte-genau gegen cbV2G validiert.
Vollständige Nachrichtenabdeckung und XMLDSig-Signaturberechnung sind Phase 3 —
aber der gesamte Schemasatz muss ohne Diagnostics durch den Generator laufen und
kompilieren.

## Schritte

### 1. Schemata beschaffen und Inventur machen

- Die -2-XSDs liegen mehreren OSS-Projekten bei (z. B. RISE-V2G unter
  `RISE-V2G-Shared/src/main/resources/schemas`, sowie im cbexigen-Umfeld).
  Lege sie unter `Vanaheimr.V2G.Exi.Iso15118_2/Schemas/` ab und dokumentiere
  Quelle + Commit in einer README daneben. Findest du sie nicht: stoppe und melde.
- Schreibe ein kleines Wegwerf-Analyse-Skript (darf ins Scratchpad), das alle in den
  fünf XSDs tatsächlich verwendeten XSD-Konstrukte und Facetten auflistet
  (import, choice, extension, abstract, substitutionGroup, attribute, unbounded,
  anonyme Typen, verwendete Built-ins …). Dieses Inventar ist deine verbindliche
  Anforderungsliste — implementiere genau das, nicht mehr. Lege das Ergebnis als
  `docs/xsd-inventory-15118-2.md` ab.

### 2. XsdReader erweitern

Mindestens (endgültig entscheidet die Inventur):
- `xs:import`/`xs:include` über mehrere Dateien und **mehrere Namespaces**.
  Achtung Generator-Architektur: heute wird pro AdditionalFile generiert; künftig
  müssen alle `.xsd` eines Satzes gesammelt (`Collect()`) und als EIN Schemaset
  aufgelöst werden (Zuordnung über targetNamespace; schemaLocation nur als Hinweis).
- `xs:attribute` (inkl. use=required/optional, xs:ID → string).
- `xs:choice` (auch mit Occurrence-Angaben, dsig nutzt choice+unbounded).
- `xs:complexContent`/`xs:extension` (Typvererbung — durchgängig in -2 genutzt,
  z. B. BodyBaseType als Basis aller Message-Bodies).
- Abstrakte Elemente + `substitutionGroup` (u. a. `BodyElement`, `TimeInterval`).
- `maxOccurs="unbounded"` und beliebige bounded-Werte an beliebiger Position
  (nicht mehr nur als Einzelkind).
- Anonyme innere complexTypes.

### 3. GrammarBuilder: echter EXI-Grammatikbau

Ersetze die Ad-hoc-Muster durch Grammatikkonstruktion nach W3C EXI 1.0
(Second Edition) §8.5.4 (schema-informed grammars), strict mode:
- Pro complexType: AT-Events zuerst (lexikografisch über QName sortiert),
  danach der Content nach Partikelmodell; EE-Platzierung und Event-Code-Bitbreiten
  exakt nach Spec (n Produktionen → ⌈log₂ n⌉ Bits, 1 Produktion → 0 Bits).
- Choice/Optionalität/Wiederholung als Produktionen mit korrekten Event-Codes.
- Substitution Groups: SE-Produktionen für alle Mitglieder am Ort der Kopf-Referenz.
- Strict mode: keine Built-in-Erweiterungen, kein xsi:type/xsi:nil (wird von den
  -2-Schemata nicht gebraucht).
- **Bei jeder Ordnungs-/Detailfrage (Sortierungen, Event-Code-Vergabe) ist der
  Byte-Output von cbV2G das Schiedsgericht** — baue dir früh Mini-Vektoren, statt
  lange gegen die Spec-Prosa zu argumentieren.
- Schreibe Grammatik-Unit-Tests auf synthetischen Mini-XSDs (je Konstrukt eines):
  erwartete Produktionstabellen und Event-Code-Breiten als Assertions. So bleibt
  der Grammatikbau unabhängig vom Emitter testbar.

### 4. CodecEmitter erweitern

- C#-Abbildung: complexType → record; Extension-Hierarchien und Substitution Groups
  brauchen Polymorphie (abstrakter Basis-Record + abgeleitete Records); choice →
  geschlossene Hierarchie oder Index-Wrapper — entscheide einheitlich und dokumentiere
  die Abbildungsregeln in `docs/xsd-to-csharp-mapping.md`.
- hexBinary/base64Binary → `byte[]`; signed Built-ins → sbyte/short/int/long.
- Value-Table-Kontext aus Phase 1 durch alle generierten Encode/Decode-Pfade fädeln.
- Output in mehrere Hint-Files splitten (pro Namespace oder Typgruppe) — der
  -2-Codec wird groß, ein einzelnes .g.cs wird unhandlich.
- AOT-freundlich bleiben: keine Reflection, kein LINQ in Hot Paths.

### 5. Neues Projekt + differenzielle Validierung

- Projekt `Vanaheimr.V2G.Exi.Iso15118_2` (net10.0) mit den fünf XSDs als
  `AdditionalFiles` und Generator-Referenz (OutputItemType="Analyzer").
- `tools/cbv2g-ref/` um den iso-2-Modul von libcbv2g erweitern
  (encode/decode für `iso2_exiDocument`).
- Neue Vektordatei `Iso15118_2.vectors.json` (gleiches Format, `referenceEncoder`
  gepinnt): SessionSetupReq (SessionID = 8×0x00 im Header, EVCCID), SessionSetupRes
  (ResponseCode, EVSEID, optional EVSETimeStamp — exerziert signed long + optional),
  ServiceDiscoveryReq (beide optionalen Felder × vorhanden/fehlt),
  ServiceDiscoveryRes. Jeweils encode, decode und roundtrip; dazu die
  Gegenrichtung: cbV2G-encodete Bytes → unser Decoder.
- Wichtig: alle -2-Nachrichten stecken im `V2G_Message`-Wrapper (Header + Body,
  Body-Inhalt via BodyElement-Substitution) — die Vektoren validieren also
  automatisch auch Document-Grammatik, Header (hexBinary-SessionID) und
  Substitution-Dispatch.

### 6. Dokumentation

- README: Architekturbild (Generator-Fähigkeiten, neues Assembly), Stand der
  -2-Abdeckung (welche Nachrichten validiert), "Next milestones" → Phase 3.

## Leitplanken

- Kein handgeschriebener -2-Codec — alles läuft durch den Generator. Der
  handgeschriebene AppProtocol-Codec und sämtliche Bestandstests
  (inkl. `GeneratedCodecDiffTests`) bleiben grün.
- Arbeite konstruktweise inkrementell: erst synthetisches Mini-XSD + Grammatik-Test
  + Emitter-Support, dann das nächste Konstrukt; das reale Schema ist der
  Integrationstest am Ende jeder Iteration.
- Fail-loud beibehalten: Was der Generator nicht kann, wird Build-Diagnostic.
- `dotnet test -c Release` bleibt ohne C-Toolchain/Java/Netzwerk lauffähig
  (Vektoren sind eingecheckt; das cbV2G-CLI dient nur der Regenerierung).
- Wire-Semantik nie spekulativ ändern — nur auf Basis eines konkreten Diffs.

## Definition of Done

1. Alle fünf -2-XSDs laufen ohne Diagnostics durch den Generator; das generierte
   Assembly kompiliert.
2. Grammatik-Unit-Tests für Attribute-Sortierung, choice, extension,
   substitutionGroup, unbounded und optionale Elemente sind grün.
3. SessionSetupReq/Res und ServiceDiscoveryReq/Res: encode/decode/roundtrip
   byte-genau gegen cbV2G@<sha> (beide Richtungen), Vektoren eingecheckt.
4. Sämtliche Bestandstests weiterhin grün.
5. `docs/xsd-inventory-15118-2.md` und `docs/xsd-to-csharp-mapping.md` existieren.
6. README aktualisiert.
7. Abschlussbericht: welche EXI-Grammatikdetails von der naiven Erwartung abwichen
   (Sortierungen, Event-Codes) und wie sie gegen cbV2G verifiziert wurden.
   