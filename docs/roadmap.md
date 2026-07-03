# Standortbestimmung

Die Solution baut sauber, alle **71 Tests sind grün** (`dotnet test -c Release`). Was heute existiert:

| Baustein | Stand |
|---|---|
| [BitReader/BitWriter](Vanaheimr.V2G.Exi.Prototype/Exi/BitReader.cs) | Bit-gepackte Streams, MSB-first — solide Basis |
| [ExiPrimitives.cs](Vanaheimr.V2G.Exi.Prototype/Exi/ExiPrimitives.cs) | Unsigned Integer, n-Bit Unsigned, String nur im „Miss"-Fall — **keine Value Tables** |
| [V2GTP.cs](Vanaheimr.V2G.Exi.Prototype/V2GTP/V2GTP.cs) | 8-Byte-Transportheader |
| [SupportedAppProtocolCodec.cs](Vanaheimr.V2G.Exi.Prototype/AppProtocol/SupportedAppProtocolCodec.cs) | Handgeschriebener SAP-Codec mit sauber dokumentierter Grammatik |
| [SourceGenerator](Vanaheimr.V2G.Exi.SourceGenerator/ExiCodecGenerator.cs) | `IIncrementalGenerator`: XSD → Grammatikplan → C#-Codec; per Diff-Test gegen den handgeschriebenen Codec abgesichert |
| Testinfrastruktur | Vektor-getrieben (JSON), Bit-genauer Diff bei Fehlschlägen — genau die richtige Architektur |

Die entscheidende Schwäche steht ehrlich im README: Die Seed-Vektoren sind **selbst-kodiert**. Grün beweist nur interne Konsistenz, nicht Wire-Konformität. Außerdem versteht der [XsdReader](Vanaheimr.V2G.Exi.SourceGenerator/Xsd/XsdReader.cs) nur die winzige XSD-Teilmenge, die AppProtocol braucht — die echten 15118-Schemata sprengen das sofort.

# Was -2 und -20 zusätzlich verlangen

**ISO 15118-2** (ein Schema-Satz: `V2G_CI_MsgDef` + MsgHeader/MsgBody/MsgDataTypes + XMLDSig):
- Alle ~36 Nachrichten stecken in einem `V2G_Message`-Wrapper; der Body ist eine **Substitution Group** über ein abstraktes `BodyElement` — das kann der Generator heute nicht.
- **Attribute** (AT-Events, z. B. `Id` für Signaturen), **xs:choice**, abstrakte Typen (`EntryType`/`IntervalType`), `maxOccurs="unbounded"`.
- Datentypen: `hexBinary` (SessionID), `base64Binary` (XMLDSig), **signed** Integers (EXI-Kodierung: Vorzeichenbit + Unsigned), `short`/`byte` für `PhysicalValueType`.
- **XMLDSig über EXI-Fragment-Grammatiken**: Für Plug & Charge (AuthorizationReq, MeteringReceiptReq) muss das referenzierte Body-Element als EXI-*Fragment* kanonisch kodiert, gehasht und die `SignedInfo` ihrerseits EXI-kodiert signiert werden. Das ist der notorisch schwierigste Teil von 15118 — und in eurem Zielbild (EV↔EVSE-Simulation mit PnC) unvermeidbar.
- EXI-Optionen sind fix (bit-packed, strict, schema-informed, Header `0x80`), aber `valuePartitionCapacity` ist unbeschränkt → **String Value Tables (lokal + global) sind normativ Pflicht**, auch wenn Strings in der Praxis selten wiederholen. Ein konformer Decoder muss Hits lesen können.

**ISO 15118-20** (mehrere Schema-Sätze: CommonMessages, AC, DC, WPT, ACDP + CommonTypes + XMLDSig):
- Kein `V2G_Message`-Wrapper mehr; jede Nachricht ist ein globales Element mit eigenem Header (SessionID, TimeStamp, optional Signature).
- **Ein EXI-Grammatik-Satz pro Namespace** — der Decoder wählt die Grammatik über den V2GTP-Payload-Type (pro Message-Set eigene Payload-Type-IDs) bzw. die per SAP ausgehandelte SchemaID. Architektonisch heißt das: ein generiertes Codec-Assembly pro Schema-Satz, plus Dispatcher.
- Mehr Nachrichten, tiefere Verschachtelung, `RationalNumberType`, Multi-Signaturen, strengere Krypto-Suiten. Bidirektionales Laden (Scheduled/Dynamic Mode) macht die Zustandsmaschine größer, nicht aber den Codec komplizierter — die Codec-Anforderung ist „gesamte XSD-Teilmenge korrekt", nicht „neue EXI-Features".

Der Fahrplan im README (Vektoren ersetzen → Value Tables → Generator ausbauen → -20) ist im Kern richtig; unten konkretisiere und erweitere ich ihn, v. a. um Fragment-Grammatiken und die Multi-Schema-Architektur.

# Plan der nächsten Schritte

**Phase 0 — Fundament beweisen (SAP wire-konform machen)**
1. `libcbv2g` als kleines CLI bauen (JSON rein → EXI-Hex raus und umgekehrt), Commit pinnen, alle SAP-Seed-Vektoren regenerieren — exakt der Workflow aus [REPLACING_SEED_VECTORS.md](Vanaheimr.V2G.Exi.Tests/Vectors/REPLACING_SEED_VECTORS.md). Erst danach ist „grün" ein Konformitätsbeweis.
2. Vektorlücken schließen (Priority-Grenzen, 20-Einträge-Fall, Non-ASCII-Namespaces — die Liste steht schon im Repo).

**Phase 1 — EXI-Primitivschicht vervollständigen**
3. **String Value Tables** (lokale + globale Partition) mit Stream-Kontext-Objekt; Kompakt-ID-Bitbreiten nach Partitionsgröße. Decoder-seitig Pflicht, Encoder-seitig für Kanonik.
4. Restliche EXI-Datentypen: Signed Integer, Binary (hex/base64), Boolean, Enumeration generisch; Float/Decimal/DateTime nur falls die Schemata sie tatsächlich referenzieren (nach XSD-Inventur entscheiden, nicht auf Vorrat).
5. Primitives gegen die **W3C-EXI-Testsuite** bzw. EXIficient-Output absichern.

**Phase 2 — Generator auf 15118-Schema-Realität heben**
6. XsdReader/GrammarBuilder erweitern: `xs:import`/`include` über mehrere Dateien und Namespaces, Attribute (AT-Events, lexikografische Sortierung), `xs:choice`, Substitution Groups + abstrakte Elemente, `unbounded`, anonyme Typen. Grammatikbau nach EXI-Spec §8.5.4 statt der heutigen Ad-hoc-Muster.
7. Zielbild: **ein generiertes Assembly pro Schema-Satz** (`…Exi.AppProtocol`, `…Exi.Iso15118_2`, `…Exi.Iso15118_20.CommonMessages`, `.DC`, `.AC`; DIN 70121 fällt fast gratis mit ab und ist für Feld-Interop wertvoll).
8. Als Meilenstein-Test: `V2G_CI_MsgDef.xsd` (-2) komplett generieren, `SessionSetupReq/Res` als erste echte Nachricht differenziell gegen cbV2G testen, dann Nachricht für Nachricht hochziehen.

**Phase 3 — 15118-2 komplett + XMLDSig**
9. Alle -2-Nachrichten mit Vektorabdeckung; `PhysicalValueType`-Helpers.
10. **Fragment-Grammatik-Kodierung** für signierte Body-Elemente + EXI-kodierte `SignedInfo`; Anbindung an .NET-Krypto (ECDSA P-256/SHA-256 für -2). Validierung gegen Signatur-Beispiele aus RISE-V2G/Josev.

**Phase 4 — 15118-20**
11. CommonMessages zuerst (SessionSetup → ServiceDiscovery → Authorization → ScheduleExchange), dann DC, dann AC; WPT/ACDP nach Bedarf. Payload-Type-Dispatcher im V2GTP-Layer.

**Phase 5 — EV↔EVSE-Simulation**
12. SDP (UDP-Discovery), TCP/TLS-Session-Loop, minimale EVCC- und SECC-Zustandsmaschinen (Happy Path AC + DC). Abschlusstest: eure EVCC-Simulation gegen den **Josev-SECC** und umgekehrt — das validiert Codec, V2GTP, Sequenzen und Timing in einem.

# Referenzbibliotheken für automatisierte Tests

Ich würde bewusst **drei Klassen von Orakeln** kombinieren, weil sie unabhängige Fehlerquellen haben:

1. **[EVerest/libcbv2g](https://github.com/EVerest/libcbv2g)** + Generator **[cbexigen](https://github.com/EVerest/cbexigen)** (C, Apache-2.0) — *primäres Differenz-Orakel*. Deckt DIN 70121, -2 und -20 ab, ist aktiv gepflegt und läuft produktiv im EVerest-Stack. Als CLI/Docker-Harness: gleiche Eingabe → Byte-Diff. Schnell genug für CI bei jedem Commit. Die XSDs liegen dem cbexigen-Repo bei — löst auch euer Schema-Beschaffungsproblem.
2. **[EXIficient](https://github.com/EXIficient/exificient)** (Java, generischer W3C-EXI-Prozessor) — *Spec-Orakel*. Wichtig als Gegenprobe, weil cbV2G ein spezialisierter Codegenerator mit eigenen Vereinfachungen ist: Wo beide unabhängig dasselbe Byte liefern, ist die Konfidenz hoch. EXIficient beherrscht zudem Value Tables und **Fragment-Kodierung** vollständig — für Phase 1 und 3 das einzige brauchbare Referenzwerkzeug. Praktischer Wrapper: [FlUxIuS/V2Gdecoder](https://github.com/FlUxIuS/V2Gdecoder) (Hex ↔ XML als CLI). Die alten Python-Stacks nutzen intern genau diesen Codec (`EXICodec.jar`), d. h. „gegen Josev testen" testet codec-seitig EXIficient.
3. **[SwitchEV/iso15118 (Josev Community)](https://github.com/SwitchEV/iso15118)** (Python, Apache-2.0, -2 und -20) — *Session-Level-Orakel*. Langsam, aber als Gegenstelle für die End-to-End-Simulation (Phase 5) ideal: kompletter SECC/EVCC inkl. SDP und TLS. Der EVerest-Fork [ext-switchev-iso15118](https://github.com/EVerest/ext-switchev-iso15118) ist die aktiver gepflegte Variante.

Ergänzend, mit klar begrenzter Rolle:
- **[OpenV2G](https://github.com/Martin-P/OpenV2G)** (C, LGPL) — historische Referenz für DIN/-2, gut als drittes Byte-Level-Votum bei Streitfällen; kein -20, praktisch eingefroren.
- **RISE-V2G** (Java, archiviert, nur -2) — Fundgrube für PnC-Signatur-Testdaten und ein zweiter voller -2-Stack.
- **[EVerest/libiso15118](https://github.com/EVerest/libiso15118)** (C++, -20-fokussiert) — als zweite -20-Gegenstelle für die Simulation.

**Teststrategie dazu:** Referenz-Encoder in Docker kapseln, generierte Vektoren mit gepinntem Commit als JSON einchecken (CI läuft offline gegen die Vektoren; ein separater, manuell getriggerter Job regeneriert sie). Dazu intern Property-based Roundtrip-Tests (z. B. CsCheck: beliebige Message → encode → decode → gleich) und Fuzzing des Decoders mit Zufallsbytes (saubere Fehler statt Crashes) — das fangen die Referenz-Orakel nämlich nicht ab.

Der größte Einzelaufwand im Plan ist Phase 2 (echter Grammatikbau nach EXI-Spec); das größte Risiko ist Phase 3 (Fragment-Signaturen). Beides lässt sich durch frühes differentielles Testen gegen zwei unabhängige Orakel gut beherrschen — die Testinfrastruktur, die ihr dafür braucht, existiert in diesem Repo im Kern schon.

Sources: [EVerest/libcbv2g](https://github.com/EVerest/libcbv2g), [EVerest/cbexigen](https://github.com/EVerest/cbexigen), [EVerest/libiso15118](https://github.com/EVerest/libiso15118), [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118), [EVerest/ext-switchev-iso15118](https://github.com/EVerest/ext-switchev-iso15118), [chargebyte zu cbexigen](https://chargebyte.com/artikel/bidirectional-charging-chargebyte-overcomes-exi-hurdle-with-release-of-own-open-source-software)
