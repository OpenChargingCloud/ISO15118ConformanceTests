# Aufgabe: ISO 15118-2 komplettieren + XMLDSig über EXI-Fragmente (Phase 3)

## Kontext

Du arbeitest im Repo `D:\Coding\OpenChargingCloud\Vanaheimr.V2G.Exi` — eine .NET-10-Bibliothek
für ISO 15118-2/-20 EXI. Architektur:

- `Vanaheimr.V2G.Exi.Prototype/` — EXI-Primitive inkl. String Value Tables, Signed
  Integer, Binary; V2GTP; handgeschriebener AppProtocol-Codec (Referenz, unangetastet).
- `Vanaheimr.V2G.Exi.SourceGenerator/` — Roslyn-Generator: fünf -2-XSDs
  (MsgDef/MsgHeader/MsgBody/MsgDataTypes/xmldsig) → `Vanaheimr.V2G.Exi.Iso15118_2`.
  Beherrscht import/choice/extension/substitutionGroup/Attribute/unbounded.
- `Vanaheimr.V2G.Exi.Tests/` — NUnit, vektorgetrieben; `tools/cbv2g-ref/` ist ein
  CLI-Harness um libcbv2g (gepinnter Commit) mit appHand- und iso-2-Modul.
- Docs: `docs/xsd-inventory-15118-2.md`, `docs/xsd-to-csharp-mapping.md`.

Lies vor Beginn: `README.md`, beide docs, die Vektor-Testinfrastruktur und den
generierten -2-Code (Shape verstehen, bevor du erweiterst).

## Vorbedingungen (zuerst prüfen)

- Phase 2 ist abgeschlossen: -2-Schemasatz generiert ohne Diagnostics,
  SessionSetupReq/Res + ServiceDiscoveryReq/Res byte-genau gegen cbV2G validiert.
- `tools/cbv2g-ref/` baut und kann iso-2 encode/decode.

Fehlt etwas davon: stoppe und melde es — nicht nebenbei nachbauen.

## Ziel

1. **Alle 17 Request/Response-Paare** von ISO 15118-2 (2013) sind mit Vektoren gegen
   cbV2G validiert: SessionSetup, ServiceDiscovery, ServiceDetail,
   PaymentServiceSelection, PaymentDetails, Authorization, ChargeParameterDiscovery,
   PowerDelivery, MeteringReceipt, SessionStop, CertificateInstallation,
   CertificateUpdate (Common); ChargingStatus (AC); CableCheck, PreCharge,
   CurrentDemand, WeldingDetection (DC).
2. **XMLDSig-Signaturen** können erzeugt und verifiziert werden: EXI-Fragment-Kodierung
   der referenzierten Elemente, EXI-Kodierung der SignedInfo, ECDSA secp256r1/SHA-256.

## Teil A — Nachrichtenabdeckung

### A1. Vektorkorpus systematisch aufbauen

- Pro Nachricht mindestens: Happy Path, jede optionale-Feld-Kombination
  (vorhanden/fehlt), Grenzwerte bei bounded Integers und Enums, leere vs. maximal
  gefüllte Listen (z. B. SAScheduleList, ParameterSets, MeterInfo).
- Komplexeste Kandidaten zuerst, weil sie die meisten Generator-Bugs finden:
  ChargeParameterDiscoveryReq/Res (Substitution abstrakter EVChargeParameter/
  EVSEChargeParameter, SalesTariff), PowerDeliveryReq (ChargingProfile),
  CertificateInstallationRes (verschachtelte dsig-Typen, base64-lastig),
  CurrentDemandReq/Res (viele PhysicalValues, optionale Felder).
- Jeder Vektor: encode-Diff gegen cbV2G, decode von cbV2G-Bytes, Roundtrip.
- Erwarte Generator-Lücken: Konstrukte, die SessionSetup nicht exerziert hat
  (tief verschachtelte choice, dsig-Typen als Felder). Fixe sie im Generator,
  nie durch handgeschriebene Sonderfälle im generierten Code.

### A2. Ergonomie-Schicht (klein halten)

- `PhysicalValueType`-Helper: Konstruktion aus decimal + Unit, Rückrechnung
  Multiplier/Value → decimal, Rundungsverhalten dokumentiert und getestet.
- Keine weiteren Convenience-APIs in dieser Phase — die Simulationsschicht (Phase 5)
  definiert, was wirklich gebraucht wird.

## Teil B — XMLDSig über EXI-Fragmente

Fachlicher Hintergrund (ISO 15118-2 §7.10 / Annex J): Signierte Elemente werden
NICHT als XML kanonisiert, sondern als **EXI-Fragment** (schema-informed, strict,
bit-packed) kodiert; darüber SHA-256 → DigestValue der Reference. Die SignedInfo
wird ihrerseits als EXI-Fragment mit dem **xmldsig-Schema** kodiert; diese Bytes
werden mit ECDSA (secp256r1, SHA-256) signiert. Signierte Elemente in -2:
AuthorizationReq und MeteringReceiptReq (je 1 Reference, via Id-Attribut),
SalesTariff (in ChargeParameterDiscoveryRes) sowie in CertificateInstallationRes/
CertificateUpdateRes ein **Multi-Reference-Fall** (ContractSignatureCertChain,
ContractSignatureEncryptedPrivateKey, DHpublickey, eMAID in EINER Signatur).

### B1. Fragment-Grammatiken im Generator

- EXI-Spec §8.5.3: Fragment-Grammatik = FragmentContent mit SE-Produktionen für
  die globalen Elementdeklarationen (lexikografisch sortiert) + ED.
- Erweitere den Generator, sodass er pro Schemasatz zusätzlich einen
  Fragment-Encoder/-Decoder emittiert (`EncodeFragment(element)` /
  `DecodeFragment(bytes)`), mindestens für die oben genannten signierbaren
  Elemente und für SignedInfo (xmldsig-Schemasatz).
- Kläre am Orakel (nicht durch Spekulation): Header-Byte des Fragments,
  Value-Table-Zustand (frisch pro Fragment) und ob der Digest über den Stream
  inklusive Header läuft. EXIficient kann Fragmente
  (`-fragment -schema … -strict`) — nutze es als Referenz.

### B2. Krypto-Anbindung

- Nur `System.Security.Cryptography` (ECDsa, P-256, SHA-256) — keine
  Drittanbieter-Krypto.
- API-Skizze: `V2GSignatureBuilder` (nimmt signierbare Elemente mit Id, erzeugt
  SignatureType mit References + SignatureValue) und `V2GSignatureVerifier`
  (prüft Digests + Signatur gegen einen public key). SignatureValue-Format
  (r‖s-Konkatenation, je 32 Bytes) gegen das Orakel verifizieren.
- Testschlüssel: einmalig generiertes P-256-Paar als PEM unter
  `Tests/TestData/` einchecken, klar als "test only" markiert.

### B3. Validierung gegen unabhängige Stacks

- **Fragment-Bytes**: Diff gegen EXIficient (CLI, gepinnte Version) für jedes
  signierbare Element; Vektoren einchecken wie gehabt (`referenceEncoder`-Feld).
- **Signatur-Gesamtfluss**: Erzeuge mit unserem Code eine signierte
  AuthorizationReq und verifiziere sie mit einem unabhängigen Stack — praktikabel
  ist ein kleines Python-Skript gegen Josev (`iso15118`-Repo, dessen
  Signatur-Utilities) oder RISE-V2Gs SecurityUtils. Umgekehrt: von dort erzeugte
  Signatur mit unserem Verifier prüfen. Beide Richtungen je einmal reichen.
- Der Multi-Reference-Fall (CertificateInstallationRes) muss explizit getestet sein.

## Leitplanken

- Wire-Semantik nur auf Basis konkreter Diffs gegen cbV2G/EXIficient ändern.
- Verschlüsselung (ECDH/AES für ContractSignatureEncryptedPrivateKey) und
  PKI-/Zertifikatsketten-Validierung sind AUSSER Scope — es geht um Kodierung
  und Signatur, nicht um das volle PnC-Schlüsselmanagement.
- `dotnet test -c Release` bleibt ohne C-Toolchain/Java/Python/Netzwerk grün;
  externe Orakel nur für Vektor-(Re-)Generierung.
- Bestandstests (AppProtocol, GeneratedCodecDiffTests, Grammatik-Unit-Tests)
  bleiben grün; Generator-Fixes konstruktweise mit Mini-XSD-Tests absichern.
- Kleine Commits, nur bei grünem Build.

## Definition of Done

1. Alle 17 Nachrichtenpaare: encode/decode/roundtrip gegen cbV2G@<sha>,
   beide Richtungen, Vektoren eingecheckt.
2. Fragment-Encoder generiert; Fragment-Bytes für alle signierbaren Elemente
   byte-gleich mit EXIficient@<version>.
3. Signieren + Verifizieren funktioniert; Kreuzvalidierung mit mindestens einem
   unabhängigen Stack in beiden Richtungen dokumentiert; Multi-Reference-Fall
   getestet.
4. PhysicalValueType-Helper mit Rundungstests.
5. Sämtliche Bestandstests grün; README + docs aktualisiert
   (Abdeckungsmatrix: Nachricht × validiert-gegen).
6. Abschlussbericht: gefundene Generator-Lücken, EXI-Detailfragen und wie sie
   am Orakel entschieden wurden.
   