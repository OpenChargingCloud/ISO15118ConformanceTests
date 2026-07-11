# XSD-Inventur ISO 15118-20 (Phase 4)

## Quelle

Die acht -20-Schemata wurden von `https://standards.iso.org/iso/15118/-20/ed-1/en/` geladen —
dieselbe frei zugängliche ISO-Quelle, aus der auch die bereits eingecheckten -2-Schemata
stammen (siehe Header-Kommentar in `Vanaheimr.V2G.Exi.Iso15118_2/Schemas/V2G_CI_MsgDef.xsd`;
cbexigens `tools_config.py` nutzt exakt dieselbe URL-Struktur für seinen
`--auto-download-public-xsd`-Modus). Geladen am 2026-07-10:

`V2G_CI_AC.xsd`, `V2G_CI_ACDP.xsd`, `V2G_CI_AppProtocol.xsd`, `V2G_CI_CommonMessages.xsd`,
`V2G_CI_CommonTypes.xsd`, `V2G_CI_DC.xsd`, `V2G_CI_WPT.xsd`, `xmldsig-core-schema.xsd`.

Referenz-Encoder weiterhin cbV2G@03350be048b3 (`lib/cbv2g/iso_20/*`, im selben Checkout wie
für -2 bereits vorhanden — libcbv2g deckt -20 komplett ab: CommonMessages/AC/DC/WPT/ACDP je
mit eigenem `iso20_<Set>_{Datatypes,Encoder,Decoder}.c`).

## Architektur-Unterschied zu -2 (bestätigt)

- **Kein `V2G_Message`-Wrapper.** Jede Nachricht (`SessionSetupReq`, `AuthorizationReq`, …) ist
  ihr eigenes globales Element mit eigenem Typ, der `V2GRequestType`/`V2GResponseType`
  (abstrakt, in `CommonTypes`) erweitert. Der Header (`SessionID`, `TimeStamp`, optionale
  `Signature`) steckt direkt in `V2GMessageType` (Basis von `V2GRequestType`) — kein separates
  Body-Substitution-Group-Konstrukt wie -2s `BodyElement`.
- **Fünf unabhängige Schemasätze**: CommonMessages, AC, DC, WPT, ACDP — jedes importiert
  `CommonTypes` + `xmldsig-core-schema`. Ein generiertes Assembly pro Satz
  (`Vanaheimr.V2G.Exi.Iso15118_20.CommonMessages/.AC/.DC/.WPT/.ACDP`), `CommonTypes` wird
  bewusst pro Assembly dupliziert (wie cbV2G/cbexigen selbst). WPT und ACDP waren ursprünglich
  explizit außer Scope, wurden aber am 2026-07-11 nachträglich vervollständigt (siehe „WPT/ACDP
  — nachträglich vervollständigt" unten für die dabei gefundenen neuen Konstrukte).
- **`RationalNumberType`** (`CommonTypes`): `Exponent xs:byte, Value xs:short` — strukturell
  identisch zu -2s `PhysicalValueType` minus die Unit; braucht nur einen einfachen
  `RationalNumber.Of/.ToDecimal`-Helper (kein neues Codec-Feature).

## Neues Konstrukt #1 (zentral): `xs:choice` als letztes Partikel einer `xs:sequence`

Kommt in CommonMessages **neunmal** vor (`AuthorizationSetupResType`, `AuthorizationReqType`,
`ScheduleExchangeReqType`, `ScheduleExchangeResType` → `Dynamic_SEResControlModeType`,
`ChargingScheduleType`, `SignedInstallationDataType`, `SignedMeteringDataType`,
`EVPowerProfileType`) — der bei weitem häufigste neue Baustein. Beispiel:

```xml
<xs:extension base="v2gci_ct:V2GResponseType">
  <xs:sequence>
    <xs:element name="AuthorizationServices" type="authorizationType" maxOccurs="2"/>
    <xs:element name="CertificateInstallationService" type="xs:boolean"/>
    <xs:choice>
      <xs:element name="EIM_ASResAuthorizationMode" type="EIM_ASResAuthorizationModeType"/>
      <xs:element name="PnC_ASResAuthorizationMode" type="PnC_ASResAuthorizationModeType"/>
    </xs:choice>
  </xs:sequence>
</xs:extension>
```

**Byte-Diff gegen cbV2G (`encode_iso20_AuthorizationSetupResType`, Grammatik 272) zeigt die
Wire-Semantik eindeutig:**

```c
struct iso20_AuthorizationSetupResType {
    ...
    struct iso20_EIM_ASResAuthorizationModeType EIM_ASResAuthorizationMode;
    unsigned int EIM_ASResAuthorizationMode_isUsed:1;
    struct iso20_PnC_ASResAuthorizationModeType PnC_ASResAuthorizationMode;
    unsigned int PnC_ASResAuthorizationMode_isUsed:1;
};
// state 272; 2 bits (ceil(log2(2+1)), phantom-Regel wie gehabt); state selects the branch
// directly (SE(EIM…)=0, SE(PnC…)=1), KEIN eigenes Wrapper-SE; danach sofort element EE.
```

Das ist **nicht** das -2-Substitutionsgruppen-Muster (dort: EIN polymorphes Feld, `is TypeX`
Pattern-Matching). cbexigen modelliert eine inline `xs:choice` als **N eigenständige, parallele
optionale Felder** (`X_isUsed`-Bit pro Branch) — strukturell identisch zu unserem bereits
existierenden **Wurzel**-`xs:choice`-Pfad (`ParameterType`, `EmitEncodeChoice`: ein Feld pro
Branch, `msg.Field is not null`), nur dass die Branches hier zusätzlich in denselben
Optional-Run/Event-Code-Bereich wie die *vorangehenden* Sequenz-Partikel flattened werden
(bestätigt an `Dynamic_SEResControlModeType`: `DepartureTime?, MinimumSOC?, TargetSOC?,
choice(minOccurs=0){Absolute/PriceLevel}` — die Choice-Branches teilen sich den Event-Code-Raum
mit den vorangehenden Optionals in genau der Form, die unsere bestehende
`EmitEncodeOptionalRun`-Maschine für optionale Substitutionsverweise ohnehin schon
unterstützt).

Zwei Ausprägungen, beide durch die Praxis belegt:
1. **Choice required** (kein `minOccurs="0"` auf `<xs:choice>`): terminiert den Run wie ein
   required Substitutionsverweis (bereits unterstützter Terminator-Pfad) — nur ohne
   Abwesenheits-Produktion.
2. **Choice optional** (`minOccurs="0"`): reiht sich als gewöhnliches Optional in den Run ein,
   inkl. EE-Alternative — bestätigt an `ChargingScheduleType`
   (`PowerSchedule, choice(minOccurs=0){AbsolutePriceSchedule/PriceLevelSchedule}`) und
   `Dynamic_SEResControlModeType`.

Mind. eine Choice hat **3 Branches** mit **gemischten** Werttypen (`SignedInstallationDataType`:
`SECP521_EncryptedPrivateKey`/`X448_EncryptedPrivateKey` sind `base64Binary`-**simple**Types,
kein complexType!) — Reihenfolge der Event-Codes ist **Dokumentreihenfolge**, nicht
alphabetisch (bestätigt: SECP521=0, X448=1, TPM=2 in Schema-Reihenfolge; alphabetisch wäre
SECP521, TPM, X448 — differiert, damit disambiguiert).

**Generatorkonsequenz (umgesetzt):** Ein neuer `ValueEncoding`-Fall `InlineChoice` (Liste von
`InlineChoiceMember(ElementName, FieldName, CSharpType, ValueEncoding, IsCSharpNullable)`,
ohne abstrakten Kopf) statt Wiederverwendung von `SubstitutionChoice` (das ein polymorphes
Einzelfeld voraussetzt). Jedes Mitglied wird zu einem **eigenen** nullable Feld im Record —
mit seinem **eigenen natürlichen** `PascalCase(ElementName)`-Feldnamen (z. B. bleibt
`EIM_ASResAuthorizationMode` als Feldname exakt so erhalten); eine Synthese eines gemeinsamen
Namens ist **nicht** nötig, da (anders als bei Substitutionsgruppen) kein gemeinsamer
C#-Basistyp existieren muss. Der Wrapper-`ChildPlan` selbst trägt nur einen internen
Platzhalter-Feldnamen (nie dereferenziert). Inhalt pro Mitglied über
`EmitEncodeContent`/`EmitWriteValue` (deckt simple UND complex Branches einheitlich ab, wie
bereits für Wurzel-`xs:choice` (`ParameterType`) etabliert). `ProductionCount` zählt
`Members.Count`; die Optional-Run-Maschine (`EmitEncodeOptionalRun`/`EmitDecodeOptionalRun`)
benötigt keine Änderung — sie behandelt eine mehrgliedrige Produktion pro `ChildPlan` bereits
generisch (vgl. `ChildPlan.IsWildcardAny` aus Phase 3, das dasselbe Prinzip nutzt). Eine
Choice muss nicht das letzte Sequenz-Partikel sein (`EVPowerProfileType` hat eine gefolgt von
einer weiteren Pflichtliste) — `ParseParticles` fügt den Choice-Marker an seiner echten
Dokumentposition ein (`ElementsBeforeSelf`-Zählung), nicht pauschal ans Ende.

## Neues Konstrukt #1b: eine erforderliche Bounded-Repeating-Liste, nicht das letzte Partikel

`AuthorizationSetupResType.AuthorizationServices` (`maxOccurs="2"`, erforderlich) wird **nicht**
zuletzt referenziert — danach folgen `CertificateInstallationService` und die EIM/PnC-Choice.
cbV2Gs Grammatik faltet **nur das unmittelbar folgende Partikel** in die "weiter vs. weiter mit
nächstem"-Ereigniscodes der Liste (state 270: `{continue=0, CertificateInstallationService=1}`,
2 Bit; state 271, Liste am Maximum: `{CertificateInstallationService=0}`, 1 Bit,
bedingungslos) — alles danach (hier die Choice) wird unabhängig weiterverarbeitet.
`EmitEncodeRequiredRepeatingWithTail`/`EmitDecodeRequiredRepeatingWithTail` bilden das nach;
nur `maxOccurs=2` (bounded-unroll) ist unterstützt, `maxOccurs≥3` mit Tail ist eine
dokumentierte Lücke (kein Vorkommen in CommonMessages/AC/DC). **Fallstrick gefunden:** das
Tail-Partikel kann ein **required, nicht-nullable** Feld sein (z. B. `bool`) — ein
Presence-Check `is not null` kompiliert für einen solchen Werttyp nicht; das Tail wird daher
bedingungslos geschrieben, sofern es kein Choice/Substitutions-Mitglied ist.

## Weiterer Fallstrick: `abstract` auf dem TYP, nicht auf dem Element

`v2gci_ct:CLReqControlMode`/`CLResControlMode` (Substitutionsgruppen-Köpfe in `CommonTypes`)
sind selbst **nicht** `abstract="true"` — nur ihr referenzierter Typ
(`CLReqControlModeType`/`CLResControlModeType`) ist es. Der bisherige Wurzel-Filter
(„document root?“) prüfte nur das Element-Flag (`ge.IsAbstract`) und hätte diese Köpfe fälschlich
als Dokument-Wurzeln behandelt (führte zu `Encode_CLReqControlMode` ohne zugehörigen Codec).
Fix: zusätzlich prüfen, ob der aufgelöste Typ selbst `IsAbstract` ist.

## Neues Konstrukt #2: Substitutionsgruppen mit **konkretem** (nicht abstraktem) Kopf, teils **transitiv verkettet**

Nur in AC/DC (nicht in CommonMessages). Beispiel (DC):

```xml
<xs:element name="DC_CPDReqEnergyTransferMode" type="DC_CPDReqEnergyTransferModeType"/>  <!-- konkret, kein abstract -->
<xs:element name="BPT_DC_CPDReqEnergyTransferMode" type="BPT_DC_CPDReqEnergyTransferModeType"
            substitutionGroup="DC_CPDReqEnergyTransferMode"/>                              <!-- substituiert den KONKRETEN Kopf -->
```

und (DC, dreistufig):

```
v2gci_ct:CLReqControlMode (abstract, Wurzel)
  <- Scheduled_DC_CLReqControlMode (konkret, substitutionGroup=Wurzel)
       <- BPT_Scheduled_DC_CLReqControlMode (konkret, substitutionGroup=Scheduled_DC_CLReqControlMode)
  <- Dynamic_DC_CLReqControlMode (analog)
       <- BPT_Dynamic_DC_CLReqControlMode
```

**Umgesetzt.** `TryBuildSubstitution` traversiert jetzt per Breitensuche transitiv (Kopf → direkte
Mitglieder → deren Mitglieder → …), sortiert das GESAMTE flache Ergebnis alphabetisch nach
Elementname (bestätigt gegen cbV2Gs `iso20_dc_DC_ChargeLoopReqType`: 5 flache Produktionen,
`BPT_Dynamic=0, BPT_Scheduled=1, [CLReqControlMode=2, abstrakt, kein Case], Dynamic=3,
Scheduled=4`, 3 Bit). Ob eine Produktion einen echten Laufzeit-Case bekommt oder nur ihren
Event-Code-Slot reserviert, entscheidet jetzt der **Typ** (`IsAbstractHead` prüft
`schema.ComplexTypes[…].IsAbstract`), nicht mehr „ist dies wörtlich der benannte Kopf" — nötig,
weil `CLReqControlMode` als Element selbst nicht abstrakt ist, nur sein Typ.

**Zweiter Fallstrick dabei gefunden:** -20-Substitutionsmitglieder können sich **gegenseitig**
erweitern (nicht nur den gemeinsamen abstrakten Kopf) — z. B. `BPT_AC_CPDReqEnergyTransferModeType
: AC_CPDReqEnergyTransferModeType` (beide konkret). Da C#s Typ-Pattern-Matching (`case BaseType v`)
auch abgeleitete Instanzen erfasst, macht ein zuerst emittierter Basis-Case den abgeleiteten Case
unerreichbar (`CS8120`) — bei -2 kam das nie vor (dort erweitern alle Mitglieder nur den
gemeinsamen abstrakten Kopf, nie sich gegenseitig). Fix: `EmitEncodeSubstitution` und
`EmitEncodeRunParticle` emittieren die `case`/`if`-Zweige jetzt **am stärksten abgeleiteten
Typ zuerst** (`InheritanceDepth` läuft die `BaseRecordName`-Kette hoch); der **Draht-Event-Code**
bleibt dabei an die ursprüngliche (alphabetische) Position gebunden, unabhängig von der
Emissionsreihenfolge. Der Decoder braucht keine Änderung (numerischer `switch`, kein
Typ-Pattern-Matching, keine Schatten-Gefahr).

## Sonst: keine neuen Grundkonstrukte

`maxOccurs` bis 2048 (weiterhin `<4096`, n-bit-Regel aus Phase 2 greift unverändert; kein
`unbounded` in den drei Sets gefunden), Attribute (`xs:ID`, required/optional, auch in
Kombination mit der neuen Choice — z. B. `SignedInstallationDataType` hat **beides**:
Pflicht-`Id` **und** eine Required-Choice als Terminator; da die Choice bereits als normaler
Run-Terminator läuft, fügt sich das attribut-als-führendes-Optional-Muster aus Phase 2
unverändert davor ein), `xs:string`/`xs:boolean`/`xs:byte`/`xs:short`/`xs:unsignedInt`/
`xs:unsignedLong`/`xs:unsignedShort`/`xs:unsignedByte`/`xs:base64Binary`/`xs:hexBinary` — alle
bereits unterstützt. Kein `xs:any`/`mixed` außerhalb des ohnehin opaken `xmldsig`-Namespace.

## Diff gegen -2-Inventar (docs/xsd-inventory-15118-2.md)

| Konstrukt | -2 | -20 |
|---|---|---|
| Body-Dispatch | `V2G_Message`-Wrapper + Substitution über `BodyElement` | jede Nachricht eigenes globales Element, Header inline |
| `xs:choice` (Wurzel, ganzer Typinhalt) | ja (`ParameterType`, `TransformType`) | ja (unverändert) |
| `xs:choice` (Sequenz-Terminator, gemischt mit anderen Partikeln) | — | **neu**, 9× in CommonMessages |
| Substitutionsgruppe, abstrakter Kopf | ja | ja (`BodyElement`-Analogon existiert hier nicht; aber `CLReqControlMode` etc.) |
| Substitutionsgruppe, konkreter/verketteter Kopf | — | **neu**, AC/DC |
| `RationalNumberType` | — (dort `PhysicalValueType` mit Unit) | neu, aber trivial (Analogon zu `PhysicalValueType`) |
| XMLDSig-Signatur | ECDSA P-256/SHA-256 | **ECDSA secp521r1/SHA-512** (stärkere Suite It.
  Spec) + optional Ed448 (von .NET nicht unterstützt — bewusste Lücke, siehe phase4.md) |

## Nachrichtenüberblick (für Vektor-Priorisierung)

- **CommonMessages** (17 Paare): SessionSetup, AuthorizationSetup, Authorization (EIM/PnC),
  ServiceDiscovery, ServiceDetail, ServiceSelection, ScheduleExchange (Scheduled/Dynamic),
  PowerDelivery, MeteringConfirmation, SessionStop, CertificateInstallation, VehicleCheckIn,
  VehicleCheckOut. `ScheduleExchangeRes` (Dynamic-Zweig mit optionaler
  Absolute-/PriceLevel-Choice) und `SignedInstallationDataType`/`SignedMeteringDataType`
  (Pflicht-Choice + Pflicht-Attribut kombiniert) sind die komplexesten Fälle — zuerst
  angehen, sie decken die meisten Lücken ab (Empfehlung aus phase4.md bestätigt).
- **DC** (5 Paare): ChargeParameterDiscovery, CableCheck, PreCharge, ChargeLoop
  (Scheduled/Dynamic/BPT via dreistufiger Substitution), WeldingDetection.
- **AC** (2 Paare): ChargeParameterDiscovery, ChargeLoop (gleiches Substitutionsmuster wie DC).

## Umsetzungsreihenfolge (dieser Session)

1. ✅ `InlineChoice`-Konstrukt (Generator + Mini-XSD-Tests) — blockierte praktisch jede
   CommonMessages-Nachricht.
2. ✅ `Vanaheimr.V2G.Exi.Iso15118_20.CommonMessages`-Projekt: vollständiges Schema generiert +
   kompiliert. Unterwegs zwei weitere Konstrukte gefunden und geschlossen: die
   Bounded-Repeating-Liste-mit-Tail (`AuthorizationServices`) und der abstrakt-auf-Typ-statt-
   Element-Fallstrick (`CLReqControlMode`/`CLResControlMode`).
3. ✅ `Vanaheimr.V2G.Exi.Iso15118_20.DC`/`.AC`-Projekte: beide generieren + kompilieren.
   Transitive/konkrete Substitution implementiert (bestätigt gegen cbV2Gs
   `iso20_dc_DC_ChargeLoopReqType`, 5 flache Produktionen, 3 Bit); dabei den
   Pattern-Matching-Schatten-Fallstrick gefunden und behoben (dritter neuer Fund dieser Session).
4. ✅ Byte-Vektoren für CommonMessages/DC/AC gegen cbV2G, V2GTP-Dispatcher,
   secp521r1/SHA-512-Signatur-Suite (CommonMessages/DC/AC), `RationalNumber`-Helper.
5. ✅ WPT und ACDP (2026-07-11, ursprünglich außer Scope) — siehe Abschnitt unten.

## WPT/ACDP — nachträglich vervollständigt (2026-07-11)

ACDP generierte und kompilierte sofort ohne Generator-Änderungen. WPT brachte zwei neue
EXI-Grammatik-Konstrukte zutage, die keines der anderen vier Sets zeigt.

### Neues Konstrukt: optionale Bounded-Repeating-Liste mitten in der Sequenz

`VendorSpecificDataContainer{0,16}` gefolgt von einem weiteren optionalen Element
(`WPT_LF_DataPackageList?`), z. B. in `WPT_FinePositioningReqType`. Bisher war eine
optionale Bounded-Repeating-Liste nur als *letztes* Element eines optionalen Runs
unterstützt (echter Selbst-Loop). Byte-für-Byte aus cbV2Gs generiertem C
(`iso20_WPT_Encoder.c`, Zustände 178–180) rekonstruiert — ein bestätigter cbexigen-
Sonderfall:

- Der „noch keine Elemente"-Zustand bietet nur *[erstes Element schreiben]* oder
  *[Element-EE]* — das nachfolgende optionale Element ist an dieser Stelle unerreichbar.
- Die Liste ist an dieser Position hart auf **2 Elemente** gedeckelt, unabhängig vom
  Schema-`maxOccurs` (16 hier) — cbexigen entrollt nur zwei Positionen, bevor es an die
  Folge-Partikel übergeben muss.

Generator-seitig in `EmitEncodeOptionalRunWithMidList`/`EmitDecodeOptionalRunWithMidList`
(`CodecEmitter.cs`) umgesetzt. Byte-verifiziert für den leeren Fall (Baseline-Vektoren);
der Fall mit Listen-Inhalt + folgendem Element ist nur selbstkonsistent getestet
(`Iso15118_20WptSelfConsistencyTests`), da er nicht Teil der Baseline-Vektoren ist.

### Neues Konstrukt: erforderliche Liste jenseits des alten `maxOccurs=2`-Limits, mit optionalem Tail

`WPT_LF_TransmitterDataType.TxSpecData` (`minOccurs=2, maxOccurs=255`) gefolgt von
`TxPackageSpecData?`. Das bestehende Konstrukt #14 (`AuthorizationServices` →
`CertificateInstallationService`) unterstützte nur `maxOccurs=2` (entrollt) mit
*erforderlichem* Tail.

**Hier gibt es keine funktionierende cbV2G-Referenz**: ein eigens aufgesetzter
Standalone-Build von libcbv2g (gcc/cmake in WSL, siehe `tools/cbv2g-ref/`) zeigt, dass
cbV2Gs eigener generierter Encoder für `WPT_LF_TransmitterDataType` mit
`EXI_ERROR__UNKNOWN_EVENT_CODE` fehlschlägt — und zwar bereits beim
Schema-Minimum von 2 `TxSpecData`-Elementen. Der generierte Zustand nach dem zweiten
Element hat schlicht keine Loop-Option mehr kodiert. Das ist ein echter cbexigen-Bug für
diese Konstruktion, kein Missverständnis unsererseits (verifiziert durch direkten Aufruf
von `encode_iso20_wpt_exiDocument` mit einer schema-validen Instanz).

Ohne Referenz zum Byte-Diff wurde eine eigenständige, spec-konforme Grammatik entworfen
(generalisiert in `EmitEncodeRequiredRepeatingWithTail`/`EmitDecodeRequiredRepeatingWithTail`):
ein echter Selbst-Loop, der bei jeder Iteration `[loop, tail-start, element EE]` anbietet.
Nur selbstkonsistent getestet, nicht gegen cbV2G verifizierbar.

### ACDP: Document-Index-Gruppierung bei geteilten Typen

`ACDP_DisconnectReq`/`Res` verwenden bewusst dieselben Typen wie `ACDP_ConnectReq`/`ResType`
(`type="ACDP_ConnectReqType"` usw.). cbV2Gs Dokument-Grammatik (`encode_iso20_acdp_exiDocument`)
weist Elemente, die sich einen Typ teilen, direkt aufeinanderfolgende Indizes zu — gruppiert
nach dem alphabetisch ersten Element dieses Typs (`ConnectReq=0, DisconnectReq=1,
ConnectRes=2, DisconnectRes=3`), NICHT rein alphabetisch nach Elementname (das hätte
`ConnectRes` vor `DisconnectReq` einsortiert). `GrammarBuilder.Build` erkennt das jetzt
gezielt (`sharedTypeGroups`); alle anderen Sets haben ein 1:1-Element/Typ-Namensmuster und
bleiben von der Änderung unberührt (durch den vollen Testlauf nach der Änderung bestätigt).

**Payload-Types**: `PayloadType_Iso20WPT = 0x8006` neu ergänzt (aus libcbv2gs
`include/cbv2g/exi_v2gtp.h`, `V2GTP20_WPT_MAINSTREAM_PAYLOAD_ID`); `PayloadType_Iso20ACDP
= 0x8005` war schon korrekt vorhanden.

**XMLDSig**: Weder WPT noch ACDP haben in cbV2G irgendein `exiFragment`/Signatur-Konstrukt
(keine Fragment-Structs, keine `EncodeFragment`/`DecodeFragment`-Funktionen in den
generierten Headern) — bestätigt per Volltextsuche in `iso20_{WPT,ACDP}_Datatypes.h` und
den zugehörigen `_Encoder.h`. Nichts zu implementieren.
