# Aufgabe: EXI-Primitivschicht vervollständigen (Phase 1)

## Kontext

Du arbeitest im Repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — eine .NET-10-Bibliothek,
die langfristig ISO 15118-2 und 15118-20 EXI-Nachrichten parsen und serialisieren soll
(Ziel: EV↔EVSE-Simulation). Aktueller Stand:

- `Vanaheimr.V2G.Exi.Prototype/Exi/` — `BitReader`, `BitWriter` (bit-packed, MSB-first)
  und `ExiPrimitives` (Unsigned Integer, n-Bit Unsigned, String nur im "Miss"-Fall).
- `Vanaheimr.V2G.Exi.Prototype/AppProtocol/` — handgeschriebener SupportedAppProtocol-Codec.
- `Vanaheimr.V2G.Exi.SourceGenerator/` — Roslyn IIncrementalGenerator (XSD → Codec),
  per Diff-Test gegen den handgeschriebenen Codec abgesichert. In dieser Phase NICHT anfassen,
  außer eine API-Änderung an den Primitiven erzwingt es.
- `Vanaheimr.V2G.Exi.Tests/` — NUnit, vektorgetrieben, 71 Tests grün.
  `dotnet test -c Release` muss am Ende weiterhin vollständig grün sein.

Lies vor Beginn: `README.md`, `Exi/ExiPrimitives.cs`, `Exi/BitReader.cs`, `Exi/BitWriter.cs`,
`AppProtocol/SupportedAppProtocolCodec.cs` und die Testinfrastruktur unter `Tests/Infrastructure/`.

## Ziel dieser Phase

Die schemalose EXI-Primitivschicht so vervollständigen, dass sie als Fundament für
ISO 15118-2/-20-Codecs taugt. Maßgeblich ist die W3C-Spezifikation
"Efficient XML Interchange (EXI) Format 1.0 (Second Edition)".
Relevante EXI-Optionen der 15118-Welt: bit-packed, schema-informed strict,
kein Options-Dokument (Header = 0x80), `valueMaxLength` und `valuePartitionCapacity`
unbeschränkt — d. h. String Value Tables sind AKTIV und normativ Pflicht.

### 1. String Value Tables (Kernstück)

Implementiere die Value-Partitionen nach EXI-Spec §7.3.3:

- **Lokale Partition** pro QName (in unserem Kontext: pro Element, identifiziert über einen
  vom Aufrufer gelieferten Schlüssel, z. B. int-Handle — die Grammatikschicht kennt die QNames)
  und **globale Partition** pro Stream.
- Kodierung eines String-Values:
  - **Local hit:** `UnsignedInteger(0)`, dann Compact-ID als n-Bit Unsigned mit
    n = ⌈log₂(m)⌉, m = aktuelle Größe der lokalen Partition.
  - **Global hit:** `UnsignedInteger(1)`, dann Compact-ID mit n = ⌈log₂(g)⌉,
    g = aktuelle Größe der globalen Partition.
  - **Miss:** `UnsignedInteger(len + 2)` + Codepoints (wie heute implementiert);
    danach den Wert in BEIDE Partitionen aufnehmen (auch beim Dekodieren!).
  - Beachte die EXI-Konvention: Partition der Größe 1 → Compact-ID mit 0 Bits.
- Dafür braucht es ein Stream-Kontext-Objekt (z. B. `ExiStringTable` oder
  `ExiEncoderContext`/`ExiDecoderContext`), das per `ref`/Instanz neben
  `BitReader`/`BitWriter` durch die Codec-Aufrufe gereicht wird. Entwirf die API so,
  dass der Source Generator sie später mechanisch aufrufen kann.
- Der bestehende AppProtocol-Codec muss auf die neue API migriert werden und
  **byte-identischen Output** zu heute liefern (AppProtocol enthält keine
  String-Wiederholungen, daher ändert sich die Wire-Form nicht — die bestehenden
  Vektortests beweisen das).

### 2. Fehlende EXI-Datentypen

Implementiere in `ExiPrimitives` (jeweils Encode + Decode, mit XML-Doc-Kommentar,
der die Spec-Stelle und das Bitlayout erklärt — gleicher Stil wie im Bestand):

- **Signed Integer** (§7.1.5): 1 Vorzeichenbit + Unsigned Integer;
  negativ: Betrag = value − (−1), d. h. `(-v) - 1`.
- **Binary** (§7.1.1): `UnsignedInteger(byteCount)` + rohe Bytes
  (deckt xs:hexBinary und xs:base64Binary ab — die Unterscheidung ist nur lexikalisch,
  auf dem Draht identisch).
- **Boolean** (§7.1.2): 1 Bit (ohne pattern-Facette; die 2-Bit-Variante mit
  Facette wird von den 15118-Schemata nicht gebraucht).
- **Float, Decimal, DateTime: NICHT implementieren.** Die 15118-2/-20-Schemata nutzen
  sie nicht (physikalische Werte sind als Multiplier/Value-Integer-Paare modelliert).
  Hinterlasse stattdessen einen kurzen Hinweis im Code/README, dass sie bewusst fehlen.

### 3. Tests (der eigentliche Wertbeweis)

- **Handgerechnete Vektoren:** Für jeden neuen Datentyp Testfälle mit von Hand
  hergeleiteten Bitfolgen (Grenzwerte: 0, ±1, 7-Bit-Grenzen 127/128, ulong.MaxValue,
  long.MinValue, leeres Binary, leerer String).
- **Value-Table-Szenarien:** gleicher String zweimal im selben Element (local hit),
  in verschiedenen Elementen (global hit), Interleaving von Hits und Misses,
  Compact-ID-Bitbreiten-Wachstum (1→2→4 Einträge), Encode→Decode-Roundtrip,
  und: Decoder wirft bei Hit-Index außerhalb der Partition einen sauberen
  `InvalidDataException` (kein Crash, keine Endlosschleife).
- **Property-based Roundtrips:** Füge CsCheck (oder FsCheck) als Test-Dependency hinzu:
  beliebige Werte → encode → decode → identisch; für Strings inkl. Non-BMP-Codepoints
  (Surrogatpaare), für Signed Integer volle ±-Range.
- **Differenz-Orakel EXIficient (vorbereiten, nicht blockieren):** Lege unter
  `Tests/Vectors/` eine `Primitives.vectors.json` im Stil der bestehenden Vektordatei an,
  mit einem `referenceEncoder`-Feld analog `REPLACING_SEED_VECTORS.md`. Die Werte dürfen
  zunächst aus der eigenen Implementierung stammen und sind als solche zu kennzeichnen
  (`generatorNote`), plus eine kurze Anleitung (Markdown neben der Datei), wie sie mit
  EXIficient/V2Gdecoder regeneriert werden. Kein Java-Setup in dieser Phase erzwingen.
- Alle 71 Bestandstests bleiben grün, insbesondere `GeneratedCodecDiffTests` und
  die AppProtocol-Vektortests (Wire-Format darf sich nicht ändern).

## Leitplanken

- .NET 10, AOT-freundlich: keine Reflection, Allokationen minimieren
  (Value Tables dürfen naturgemäß allozieren; Dictionary/List sind ok).
- Code-Stil des Repos übernehmen: records, ausführliche XML-Doc-Kommentare,
  die das EXI-Bitlayout und Spec-Referenzen erklären; deutsche Commit-Sprache nicht nötig.
- Arbeite test-first, wo sinnvoll; kleine, nachvollziehbare Commits sind willkommen,
  aber committe nur, wenn der Build grün ist.
- Aktualisiere am Ende `README.md`: Abschnitt "What this prototype still does NOT do"
  und "Next milestones" an den neuen Stand anpassen.

## Definition of Done

1. `dotnet test -c Release` vollständig grün (Bestand + neue Tests).
2. Value Tables: Hit/Miss beidseitig implementiert, durch die oben genannten
   Szenario-Tests abgedeckt.
3. Signed Integer, Binary, Boolean implementiert und mit handgerechneten Vektoren belegt.
4. AppProtocol-Wire-Format byte-identisch zu vorher.
5. Property-based Roundtrip-Tests laufen in der normalen Testsuite.
6. README + Vektor-Doku aktualisiert.
