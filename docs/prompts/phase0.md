# Aufgabe: SAP-Seed-Vektoren durch cbV2G-Referenzoutput ersetzen (Phase 0)

## Kontext

Du arbeitest im Repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — eine .NET-10-Bibliothek,
die langfristig ISO 15118-2 und 15118-20 EXI-Nachrichten parsen und serialisieren soll.
Aktueller Stand:

- `Vanaheimr.V2G.Exi.Prototype/` — BitReader/BitWriter, ExiPrimitives, V2GTP-Header und ein
  handgeschriebener Codec für SupportedAppProtocolReq/Res (SAP).
- `Vanaheimr.V2G.Exi.SourceGenerator/` — Roslyn-Generator (XSD → Codec), per Diff-Test gegen
  den handgeschriebenen Codec abgesichert.
- `Vanaheimr.V2G.Exi.Tests/` — NUnit, vektorgetrieben, 71 Tests grün (`dotnet test -c Release`).

**Das Problem, das du löst:** Die Vektoren in
`Vanaheimr.V2G.Exi.Tests/Vectors/AppProtocol.vectors.json` wurden vom Codec selbst erzeugt.
Grün beweist nur interne Konsistenz — nicht, dass die Bytes dem entsprechen, was eine echte
EVSE auf dem Draht erwartet. Der geplante Workflow steht bereits in
`Vanaheimr.V2G.Exi.Tests/Vectors/REPLACING_SEED_VECTORS.md` — lies ihn zuerst, er ist
verbindlich. Lies außerdem: `README.md`, `AppProtocol/SupportedAppProtocolCodec.cs`,
`Tests/Infrastructure/` und die Vektordatei selbst.

## Ziel

Alle SAP-Vektoren tragen `expectedHex` aus **libcbv2g** (EVerest, Apache-2.0,
https://github.com/EVerest/libcbv2g) mit gepinntem Commit. Danach ist jeder Testfehlschlag
ein echter Konformitätsbefund gegen eine produktiv eingesetzte Referenzimplementierung.

## Schritte

### 1. Referenz-CLI bauen (`tools/cbv2g-ref/`)

- Klone libcbv2g, pinne einen konkreten Commit (SHA notieren).
- Schreibe ein kleines C-Programm, das libcbv2gs App-Handshake-Codec
  (Modul `app_handshake`, Funktionen wie `encode_appHand_exiDocument` /
  `decode_appHand_exiDocument` — exakte API im Repo nachschlagen) kapselt:
  - `encode`: liest das `input`-JSON-Format unserer Vektordatei von stdin
    (bevorzugt; alternativ eine bewusst simple Zeilen-/Argumentform, wenn JSON-Parsing
    in C unverhältnismäßig ist — dann übernimmt das Treiberskript die Übersetzung),
    schreibt den EXI-Stream als Hex auf stdout.
  - `decode`: liest Hex von stdin, schreibt die Feldwerte als JSON auf stdout.
- Wichtig: Unsere Vektoren enthalten den kompletten EXI-Stream **inklusive Header-Byte
  0x80**, aber ohne V2GTP-Header. Prüfe, was libcbv2g liefert, und gleiche die Konvention an.
- Build: CMake. Auf diesem Windows-11-Rechner sind Git Bash und PowerShell verfügbar;
  nimm den Weg des geringsten Widerstands (MSVC Build Tools, MSYS2 oder WSL — was
  vorhanden ist und funktioniert). Der Test-Suite-Lauf selbst bleibt davon unabhängig:
  Die Tests lesen nur die eingecheckte JSON-Datei, das CLI wird ausschließlich zur
  (Re-)Generierung gebraucht.
- Lege unter `tools/cbv2g-ref/README.md` Build- und Nutzungsanleitung ab.

### 2. Treiberskript für die Regenerierung

- Skript (PowerShell oder Python) unter `tools/`, das jeden Vektor aus
  `AppProtocol.vectors.json` durch das CLI schickt und `expectedHex` in der Datei ersetzt.
- Schreibe das `referenceEncoder`-Feld in die Vektordatei, exakt wie in
  `REPLACING_SEED_VECTORS.MD` vorgeschlagen (name, repo, commit, buildFlags).
- Entferne die `generatorNote`-Selbstkodierungs-Warnung, setze `generator` auf `cbV2G@<sha>`.

### 3. Vektorlücken schließen (vor der Regenerierung anlegen)

Ergänze die in `REPLACING_SEED_VECTORS.md` aufgelisteten Fälle:
- alle drei ResponseCode-Werte × {SchemaID vorhanden, fehlt},
- Request mit 20 Einträgen (Req_20-Pfad: 0-Bit-Terminator),
- Request mit genau 1 Eintrag,
- Priority an den Grenzen 1, 2, 19, 20,
- ProtocolNamespace nahe maxLength=100,
- Non-ASCII-Zeichen im Namespace (Umlaute + mindestens ein Nicht-BMP-Codepoint,
  z. B. ein Emoji — exerziert Mehrbyte-Runen).

### 4. Abgleich und Fehlerbehebung

- `dotnet test -c Release`. Jeder Mismatch ist jetzt bedeutungsvoll: vermutlich ein Bug
  im C#-Codec relativ zur Referenz. Analysiere mit dem Bit-Diff aus `HexUtil`,
  behebe die Ursache im C#-Code (handgeschriebener Codec UND Source-Generator-Emitter
  müssen konsistent bleiben — die `GeneratedCodecDiffTests` sichern das ab).
- **Melde jeden gefundenen Divergenzfall explizit im Abschlussbericht** (was war falsch,
  in welchem Bitfeld, wie behoben). Wenn es keine Divergenzen gibt, ist das ein
  ausgezeichnetes Ergebnis — behaupte es aber nur nach vollständigem Lauf.
- Bidirektional prüfen: Unsere encodeten Bytes müssen von cbV2G dekodierbar sein
  (Stichprobe über das `decode`-CLI, im Regenerierungsskript mit abdecken).

### 5. Dokumentation nachziehen

- `README.md`: Abschnitt "What green means today" umschreiben — grün heißt jetzt
  Wire-Konformität gegen cbV2G@<sha>. "Next milestones" aktualisieren.
- `REPLACING_SEED_VECTORS.md`: von "geplant" auf "durchgeführt" umstellen,
  Regenerierungs-Anleitung (ein Befehl) dokumentieren.
- Falls ein Python-Simulator im Repo liegt: löschen (laut Doku war er nur Bootstrap).
  Falls nicht im Repo: Hinweis im README entsprechend anpassen.

## Leitplanken

- Ändere die Wire-Semantik des C#-Codecs nur auf Basis eines konkreten Diffs gegen
  cbV2G-Output, nie spekulativ.
- Keine neuen Pflicht-Abhängigkeiten für den normalen Testlauf: `dotnet test` muss
  weiterhin ohne C-Toolchain, Java oder Netzwerk funktionieren.
- Externer Code (libcbv2g-Klon) wird NICHT ins Repo eingecheckt — nur das CLI-Harness,
  das Skript und der gepinnte SHA.
- Code-Stil des Repos übernehmen (ausführliche erklärende Kommentare, records).
- Committe in kleinen Schritten, nur bei grünem Build.

## Definition of Done

1. `tools/cbv2g-ref/` baut lokal und ist dokumentiert; Commit-SHA gepinnt.
2. Alle Vektoren (Bestand + neue Fälle aus Schritt 3) tragen cbV2G-generierte
   `expectedHex`-Werte und das `referenceEncoder`-Feld; `generatorNote` entfernt.
3. `dotnet test -c Release` vollständig grün — inklusive der regenerierten Vektoren.
4. Bidirektionale Stichprobe (unser Encode → cbV2G-Decode) dokumentiert erfolgreich.
5. Abschlussbericht listet alle gefundenen und behobenen Divergenzen (oder bestätigt: keine).
6. README + Vektor-Doku aktualisiert.
