using NUnit.Framework;
using Vanaheimr.V2G.Exi.Tests.Infrastructure;

namespace Vanaheimr.V2G.Exi.Tests;

/// <summary>
/// Construct-by-construct grammar/emit tests driven through the source generator on
/// synthetic mini-XSDs (see <see cref="GeneratorHarness"/>). Each XSD construct gets a
/// focused test before it is used against the real ISO 15118-2 schema set.
/// </summary>
[TestFixture]
public class GeneratorGrammarTests
{
    // ---- baseline: the single-file path still works -----------------------

    private const string SingleSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns="urn:test:a" targetNamespace="urn:test:a">
          <xs:element name="root" type="RootType"/>
          <xs:complexType name="RootType">
            <xs:sequence>
              <xs:element name="Count" type="xs:unsignedInt"/>
            </xs:sequence>
          </xs:complexType>
        </xs:schema>
        """;

    [Test]
    public void SingleFile_Generates_WithoutDiagnostics()
    {
        var r = GeneratorHarness.Run(("a.xsd", SingleSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        Assert.That(r.GeneratedSource, Does.Contain("record Root"));
        // Non-strict document grammar: a single global element still uses a >=1-bit selector.
        Assert.That(r.GeneratedSource, Does.Contain("Encode_Root"));
    }

    // ---- construct #1: multi-file import, cross-namespace type reference ---

    private const string Importer = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns:b="urn:test:b" targetNamespace="urn:test:a">
          <xs:import namespace="urn:test:b" schemaLocation="b.xsd"/>
          <xs:element name="root" type="b:FooType"/>
        </xs:schema>
        """;

    private const string Imported = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns="urn:test:b" targetNamespace="urn:test:b">
          <xs:complexType name="FooType">
            <xs:sequence>
              <xs:element name="X" type="xs:unsignedInt"/>
              <xs:element name="Y" type="xs:unsignedInt"/>
            </xs:sequence>
          </xs:complexType>
        </xs:schema>
        """;

    [Test]
    public void MultiFile_Import_ResolvesCrossNamespaceTypeRef()
    {
        // The importer's global element references a complexType in the imported file via a
        // prefix (b:FooType); the collected set must resolve it and emit the record + codec.
        var r = GeneratorHarness.Run(("a.xsd", Importer), ("b.xsd", Imported));

        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        Assert.That(r.GeneratedSource, Does.Contain("record Foo"));
        Assert.That(r.GeneratedSource, Does.Contain("Encode_Foo"));
    }

    [Test]
    public void ImportOrder_Independent()
    {
        // Same set, imported file first: merging must not depend on file order.
        var r = GeneratorHarness.Run(("b.xsd", Imported), ("a.xsd", Importer));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        Assert.That(r.GeneratedSource, Does.Contain("Encode_Foo"));
    }

    // ---- construct #2: complexContent / extension -------------------------

    [Test]
    public void Extension_MergesBaseThenDerivedParticles()
    {
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:e" targetNamespace="urn:test:e">
              <xs:element name="root" type="DerivedType"/>
              <xs:complexType name="BaseType">
                <xs:sequence><xs:element name="BaseField" type="xs:unsignedInt"/></xs:sequence>
              </xs:complexType>
              <xs:complexType name="DerivedType">
                <xs:complexContent>
                  <xs:extension base="BaseType">
                    <xs:sequence><xs:element name="DerivedField" type="xs:unsignedInt"/></xs:sequence>
                  </xs:extension>
                </xs:complexContent>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("e.xsd", xsd));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);

        // The record carries both fields, and the base field is encoded first.
        Assert.That(r.GeneratedSource, Does.Contain("BaseField"));
        Assert.That(r.GeneratedSource, Does.Contain("DerivedField"));
        int enc = r.GeneratedSource.IndexOf("Encode_Derived", StringComparison.Ordinal);
        int baseAt = r.GeneratedSource.IndexOf("msg.BaseField", enc, StringComparison.Ordinal);
        int derivedAt = r.GeneratedSource.IndexOf("msg.DerivedField", enc, StringComparison.Ordinal);
        Assert.That(baseAt, Is.GreaterThan(-1).And.LessThan(derivedAt),
            "base particle must be encoded before the derived particle");
    }

    [Test]
    public void Extension_OfEmptyAbstractBase_YieldsOwnParticlesOnly()
    {
        // Mirrors the ISO 15118-2 shape: an abstract empty BodyBaseType extended by a body.
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:e" targetNamespace="urn:test:e">
              <xs:element name="root" type="MsgType"/>
              <xs:complexType name="BaseType" abstract="true"/>
              <xs:complexType name="MsgType">
                <xs:complexContent>
                  <xs:extension base="BaseType">
                    <xs:sequence><xs:element name="Only" type="xs:unsignedInt"/></xs:sequence>
                  </xs:extension>
                </xs:complexContent>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("e.xsd", xsd));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        Assert.That(r.GeneratedSource, Does.Contain("Only"));
    }

    // ---- construct #3: substitutionGroup + abstract head + element ref ----

    private const string SubstSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns="urn:test:s" targetNamespace="urn:test:s">
          <xs:element name="root" type="ContainerType"/>
          <xs:complexType name="ContainerType">
            <xs:sequence><xs:element ref="Head"/></xs:sequence>
          </xs:complexType>

          <xs:element name="Head" type="HeadBaseType" abstract="true"/>
          <xs:complexType name="HeadBaseType" abstract="true"/>

          <xs:element name="Alpha" type="AlphaType" substitutionGroup="Head"/>
          <xs:complexType name="AlphaType">
            <xs:complexContent><xs:extension base="HeadBaseType">
              <xs:sequence><xs:element name="A" type="xs:unsignedInt"/></xs:sequence>
            </xs:extension></xs:complexContent>
          </xs:complexType>

          <xs:element name="Beta" type="BetaType" substitutionGroup="Head"/>
          <xs:complexType name="BetaType">
            <xs:complexContent><xs:extension base="HeadBaseType">
              <xs:sequence><xs:element name="B" type="xs:unsignedInt"/></xs:sequence>
            </xs:extension></xs:complexContent>
          </xs:complexType>
        </xs:schema>
        """;

    [Test]
    public void SubstitutionGroup_EmitsAbstractBase_AndPolymorphicDispatch()
    {
        var r = GeneratorHarness.Run(("s.xsd", SubstSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // Abstract base record + members inheriting from it.
        Assert.That(src, Does.Contain("abstract record HeadBaseType"));
        Assert.That(src, Does.Contain(": HeadBaseType"));
        // Polymorphic encode: a case per concrete member (not the abstract head).
        Assert.That(src, Does.Contain("case AlphaType v:"));
        Assert.That(src, Does.Contain("case BetaType v:"));
    }

    [Test]
    public void SubstitutionGroup_IncludesAbstractHead_InEventCodeWidth()
    {
        // Members sorted by element name: Alpha(0), Beta(1), Head(2). Including the abstract
        // head makes 3 productions -> 2-bit event code (2 members alone would be 1 bit).
        var r = GeneratorHarness.Run(("s.xsd", SubstSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        Assert.That(src, Does.Contain("w.WriteBits(0, 2)"), "Alpha at index 0, width 2");
        Assert.That(src, Does.Contain("w.WriteBits(1, 2)"), "Beta at index 1, width 2");
        // Decode reads the same 2-bit selector and rejects the abstract head slot (index 2).
        Assert.That(src, Does.Contain("r.ReadBits(2)"));
        Assert.That(src, Does.Contain("abstract substitution head cannot be decoded"));
    }

    // ---- construct: additional built-in datatypes -------------------------

    [Test]
    public void Builtins_BinaryAndSigned_MapToPrimitives()
    {
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:b" targetNamespace="urn:test:b">
              <xs:element name="root" type="T"/>
              <xs:complexType name="T">
                <xs:sequence>
                  <xs:element name="Bin"   type="xs:hexBinary"/>
                  <xs:element name="Key"   type="xs:base64Binary"/>
                  <xs:element name="Stamp" type="xs:long"/>
                  <xs:element name="Delta" type="xs:int"/>
                </xs:sequence>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("b.xsd", xsd));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        Assert.That(src, Does.Contain("byte[] Bin"));
        Assert.That(src, Does.Contain("byte[] Key"));
        Assert.That(src, Does.Contain("long Stamp"));
        Assert.That(src, Does.Contain("int Delta"));
        Assert.That(src, Does.Contain("ExiPrimitives.WriteBinary"));
        Assert.That(src, Does.Contain("ExiPrimitives.WriteSignedInteger"));
        Assert.That(src, Does.Contain("ExiPrimitives.ReadBinary"));
    }

    // ---- construct #4: optional attribute (AT event) ---------------------

    [Test]
    public void OptionalAttribute_EmittedAsMergedInitialState()
    {
        // Mirrors CertificateChainType: optional Id attribute + a required first content
        // element + a trailing optional element.
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:a" targetNamespace="urn:test:a">
              <xs:element name="root" type="ChainType"/>
              <xs:complexType name="ChainType">
                <xs:sequence>
                  <xs:element name="Certificate" type="xs:base64Binary"/>
                  <xs:element name="Extra" type="xs:unsignedInt" minOccurs="0"/>
                </xs:sequence>
                <xs:attribute name="Id" type="xs:ID"/>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("a.xsd", xsd));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // Nullable attribute parameter, encoded as a string value with a 2-bit AT/SE selector.
        Assert.That(src, Does.Contain("string? Id"));
        Assert.That(src, Does.Contain("if (msg.Id is not null)"));
        Assert.That(src, Does.Contain("w.WriteBits(0, 2)"), "AT(Id) event code");
        Assert.That(src, Does.Contain("w.WriteBits(1, 2)"), "SE(first content) when attribute absent");
        // Decode reads the same 2-bit selector.
        Assert.That(src, Does.Contain("r.ReadBits(2)"));
        Assert.That(src, Does.Contain("_Id = ExiPrimitives.ReadStringValue"));
    }

    // ---- construct: repeating element within a sequence -------------------

    [Test]
    public void RepeatingElement_AsLastChild_EmittedAsList()
    {
        // Mirrors ParameterSetType: a leading scalar followed by a bounded-repeating element.
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:r" targetNamespace="urn:test:r">
              <xs:element name="root" type="SetType"/>
              <xs:complexType name="SetType">
                <xs:sequence>
                  <xs:element name="SetID" type="xs:short"/>
                  <xs:element name="Item"  type="ItemType" maxOccurs="16"/>
                </xs:sequence>
              </xs:complexType>
              <xs:complexType name="ItemType">
                <xs:sequence><xs:element name="V" type="xs:unsignedInt"/></xs:sequence>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("r.xsd", xsd));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        Assert.That(src, Does.Contain("short SetID"));
        Assert.That(src, Does.Contain("IReadOnlyList<ItemType> Item"));
        // The scalar is encoded before the list, and the list uses the 1-bit-first / 2-bit-loop
        // event codes with a 2-bit terminator.
        Assert.That(src, Does.Contain("w.WriteBits(0, i == 0 ? 1 : 2)"));
        Assert.That(src, Does.Contain("w.WriteBits(1, 2)"));
        int enc = src.IndexOf("Encode_SetType", StringComparison.Ordinal);
        int setId = src.IndexOf("msg.SetID", enc, StringComparison.Ordinal);
        int loop  = src.IndexOf("Item_list", enc, StringComparison.Ordinal);
        Assert.That(setId, Is.GreaterThan(-1).And.LessThan(loop));
    }

    // ---- construct #6: xs:choice + required attribute ---------------------

    [Test]
    public void ChoiceWithRequiredAttribute_EmitsSelectorAndPrefix()
    {
        // Mirrors ParameterType: a required Name attribute followed by a choice of typed values.
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:c" targetNamespace="urn:test:c">
              <xs:element name="root" type="ParamType"/>
              <xs:complexType name="ParamType">
                <xs:choice>
                  <xs:element name="boolValue"   type="xs:boolean"/>
                  <xs:element name="intValue"    type="xs:int"/>
                  <xs:element name="stringValue" type="xs:string"/>
                </xs:choice>
                <xs:attribute name="Name" type="xs:string" use="required"/>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("c.xsd", xsd));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // Record: required attr + mutually-exclusive nullable alternatives (field names are
        // PascalCased by the generator).
        Assert.That(src, Does.Contain("string? Name"));
        Assert.That(src, Does.Contain("bool? BoolValue"));
        Assert.That(src, Does.Contain("int? IntValue"));
        Assert.That(src, Does.Contain("string? StringValue"));
        // Required-attribute prefix (1-bit AT) then a 2-bit choice selector (3 alts -> 2 bits).
        Assert.That(src, Does.Contain("w.WriteBits(0, 1);   // AT(required attribute)"));
        Assert.That(src, Does.Contain("if (msg.BoolValue is not null)"));
        Assert.That(src, Does.Contain("w.WriteBits(1, 2)"), "IntValue at choice index 1");
        Assert.That(src, Does.Contain("switch (r.ReadBits(2))"));
    }

    // ---- construct #7: xs:simpleContent extension -------------------------

    [Test]
    public void SimpleContent_ValuePlusRequiredAttribute()
    {
        // Mirrors ContractSignatureEncryptedPrivateKeyType: a base64 value with a required Id.
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:sc" targetNamespace="urn:test:sc">
              <xs:element name="root" type="EncKeyType"/>
              <xs:complexType name="EncKeyType">
                <xs:simpleContent>
                  <xs:extension base="keyType">
                    <xs:attribute name="Id" type="xs:ID" use="required"/>
                  </xs:extension>
                </xs:simpleContent>
              </xs:complexType>
              <xs:simpleType name="keyType">
                <xs:restriction base="xs:base64Binary"><xs:maxLength value="48"/></xs:restriction>
              </xs:simpleType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("sc.xsd", xsd));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        Assert.That(src, Does.Contain("string? Id"));
        Assert.That(src, Does.Contain("byte[] Value"));
        Assert.That(src, Does.Contain("w.WriteBits(0, 1);   // AT(required attribute)"));
        Assert.That(src, Does.Contain("w.WriteBits(0, 1);   // CONTENT event"));
        Assert.That(src, Does.Contain("ExiPrimitives.WriteBinary(ref w, msg.Value)"));
        Assert.That(src, Does.Contain("var _Value = ExiPrimitives.ReadBinary(ref r)"));
    }

    // ---- construct #8: opaque XMLDSig reference + runs of trailing optionals ----

    private const string DsigSchema = """
        <schema xmlns="http://www.w3.org/2001/XMLSchema" xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                targetNamespace="http://www.w3.org/2000/09/xmldsig#" elementFormDefault="qualified">
          <!-- Opaque signature subtree: xs:any / refs, never modelled. -->
          <element name="Signature" type="ds:SignatureType"/>
          <complexType name="SignatureType">
            <sequence><any processContents="lax"/></sequence>
            <attribute name="Id" type="ID"/>
          </complexType>
          <!-- A self-contained data type genuinely referenced by the main schema (like
               X509IssuerSerialType): unprefixed built-in field types resolve via the default
               XSD namespace. -->
          <complexType name="X509IssuerSerialType">
            <sequence>
              <element name="X509IssuerName" type="string"/>
              <element name="X509SerialNumber" type="integer"/>
            </sequence>
          </complexType>
        </schema>
        """;

    private const string HeaderSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                   xmlns="urn:test:h" targetNamespace="urn:test:h" elementFormDefault="qualified">
          <xs:import namespace="http://www.w3.org/2000/09/xmldsig#" schemaLocation="dsig.xsd"/>
          <xs:element name="root" type="HeaderType"/>
          <xs:complexType name="HeaderType">
            <xs:sequence>
              <xs:element name="SessionID" type="xs:hexBinary"/>
              <xs:element name="Note" type="xs:unsignedInt" minOccurs="0"/>
              <xs:element ref="ds:Signature" minOccurs="0"/>
              <xs:element name="CertId" type="ds:X509IssuerSerialType" minOccurs="0"/>
            </xs:sequence>
          </xs:complexType>
        </xs:schema>
        """;

    [Test]
    public void OpaqueReference_ModelledAsAbsentPlaceholder()
    {
        var r = GeneratorHarness.Run(("h.xsd", HeaderSchema), ("dsig.xsd", DsigSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // The opaque Signature element becomes an empty placeholder record and a nullable field;
        // encoding/decoding a present instance fails loud (deferred to Phase 3).
        Assert.That(src, Does.Contain("public sealed record Signature();"));
        Assert.That(src, Does.Contain("Signature? Signature"));
        Assert.That(src, Does.Contain("(XMLDSig) is deferred to Phase 3"));
        // The self-contained data type from the opaque namespace IS modelled (unprefixed built-ins
        // resolved via the default XSD namespace: string -> string, integer -> long/EXI Integer).
        Assert.That(src, Does.Contain("record X509IssuerSerialType"));
        Assert.That(src, Does.Contain("string X509IssuerName"));
        Assert.That(src, Does.Contain("long X509SerialNumber"));
        Assert.That(src, Does.Contain("ExiPrimitives.WriteSignedInteger"));
    }

    [Test]
    public void TrailingOptionalRun_UsesCbV2GEventCodeWidths()
    {
        // SessionID (required) + a run of trailing optionals (Note, Signature, CertId) ending in
        // the element EE — the ISO 15118-2 message-header shape. cbexigen widths each state at
        // ceil(log2(productions+1)): 3 optionals + EE = 4 productions -> 3 bits, and the terminating
        // EE for the all-absent path takes the highest event code at that width.
        var r = GeneratorHarness.Run(("h.xsd", HeaderSchema), ("dsig.xsd", DsigSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // State 0 (Note, Signature, CertId, EE): 4 productions -> 3-bit codes; all-absent EE = code 3.
        Assert.That(src, Does.Contain("w.WriteBits(0, 3);   // Note"));
        Assert.That(src, Does.Contain("w.WriteBits(3, 3);   // element EE"));
        // A later state (Signature, CertId, EE): 3 productions -> 2-bit codes.
        Assert.That(src, Does.Contain("w.WriteBits(2, 2);   // element EE"));
        // Final state after the last optional (CertId) present: 1-bit EE.
        Assert.That(src, Does.Contain("w.WriteBits(0, 1);   // element EE"));
        // Decode reads the same widths.
        Assert.That(src, Does.Contain("r.ReadBits(3)"));
        Assert.That(src, Does.Contain("r.ReadBits(2)"));
    }

    [Test]
    public void OptionalRunAndOpaque_GeneratedCodeCompiles()
    {
        // The multi-optional-run, opaque-placeholder, and complex-terminator paths are not
        // exercised by the checked-in AppProtocol codec — compile the generated source directly
        // (against the Prototype's BitWriter/ExiPrimitives) to prove it builds.
        var r = GeneratorHarness.Run(("h.xsd", HeaderSchema), ("dsig.xsd", DsigSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);

        var errors = GeneratorHarness.CompileErrors(r.GeneratedSource, typeof(Vanaheimr.V2G.Exi.ExiPrimitives));
        Assert.That(errors, Is.Empty,
            r.GeneratedSource + "\n\n" + string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Test]
    public void OptionalRunBeforeRequired_FoldsTerminatorSeIntoEventCode()
    {
        // A run of optionals terminated by a required element (CurrentDemandResType shape): the
        // required element's SE is folded into the run's event codes. 2 optionals + the required
        // terminator + EE-phantom = width ceil(log2(3+1)) = 2 bits at the first state; the
        // terminator takes the highest code when all optionals are absent.
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:o" targetNamespace="urn:test:o">
              <xs:element name="root" type="T"/>
              <xs:complexType name="T">
                <xs:sequence>
                  <xs:element name="A" type="xs:unsignedInt"/>
                  <xs:element name="Opt1" type="xs:unsignedInt" minOccurs="0"/>
                  <xs:element name="Opt2" type="xs:unsignedInt" minOccurs="0"/>
                  <xs:element name="Req"  type="xs:unsignedInt"/>
                </xs:sequence>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("o.xsd", xsd));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // State 0 (Opt1, Opt2, Req): 3 productions -> 2 bits; Req (all optionals absent) = code 2.
        Assert.That(src, Does.Contain("w.WriteBits(0, 2);   // Opt1"));
        Assert.That(src, Does.Contain("w.WriteBits(2, 2);   // SE(Req)"));
        // Reached via the last optional present, Req is at its own 1-bit SE state.
        Assert.That(src, Does.Contain("w.WriteBits(0, 1);   // SE(Req)"));
        // The required terminator's content is emitted (not skipped) and the element still ends
        // with its own EE afterwards.
        Assert.That(src, Does.Contain("w.WriteBits(0, 1);   // element EE"));
    }

    // ---- construct #9: optional attribute + optional content ----

    private const string AuthReqSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns="urn:test:auth" targetNamespace="urn:test:auth">
          <xs:element name="root" type="AuthReqType"/>
          <xs:complexType name="AuthReqType">
            <xs:sequence>
              <xs:element name="GenChallenge" type="xs:base64Binary" minOccurs="0"/>
            </xs:sequence>
            <xs:attribute name="Id" type="xs:ID"/>
          </xs:complexType>
        </xs:schema>
        """;

    [Test]
    public void OptionalAttributeWithOptionalContent_FoldsAtIntoContentRun()
    {
        // AuthorizationReqType shape: optional Id attribute + optional GenChallenge element. cbV2G
        // grammar 222/223: the AT event is the first production of the content's initial state, so
        // {Id, GenChallenge, EE} is a 3-production (2-bit) state — the attribute is just the leading
        // optional of the run. This used to fail loud ("first content child must be required").
        var r = GeneratorHarness.Run(("auth.xsd", AuthReqSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // Record: attribute first (nullable), then the optional element.
        Assert.That(src, Does.Contain("string? Id"));
        Assert.That(src, Does.Contain("byte[]? GenChallenge"));
        // State 0 {Id, GenChallenge, EE}: Id at code 0, all-absent EE at code 2, both 2-bit.
        Assert.That(src, Does.Contain("w.WriteBits(0, 2);   // Id"));
        Assert.That(src, Does.Contain("w.WriteBits(2, 2);   // element EE"));
        // The AT value is a bare string — no value-start bit, unlike an element value.
        Assert.That(src, Does.Contain("ExiPrimitives.WriteStringValue(ref w, msg.Id!);"));
        Assert.That(src, Does.Contain("_Id = ExiPrimitives.ReadStringValue(ref r);"));
    }

    [Test]
    public void OptionalAttributeWithOptionalContent_GeneratedCodeCompiles()
    {
        var r = GeneratorHarness.Run(("auth.xsd", AuthReqSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var errors = GeneratorHarness.CompileErrors(r.GeneratedSource, typeof(Vanaheimr.V2G.Exi.ExiPrimitives));
        Assert.That(errors, Is.Empty,
            r.GeneratedSource + "\n\n" + string.Join("\n", errors.Select(e => e.ToString())));
    }

    // ---- construct #10: substitution references flattened into optional runs ----

    private const string OptionalSubstSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns="urn:test:pd" targetNamespace="urn:test:pd">
          <xs:element name="root" type="PdType"/>
          <xs:complexType name="PdType">
            <xs:sequence>
              <xs:element name="Req1" type="xs:unsignedInt"/>
              <xs:element name="Opt1" type="xs:unsignedInt" minOccurs="0"/>
              <xs:element ref="EVParam" minOccurs="0"/>
            </xs:sequence>
          </xs:complexType>
          <xs:element name="EVParam" type="EVParamBase" abstract="true"/>
          <xs:complexType name="EVParamBase" abstract="true"/>
          <xs:element name="DC_EVParam" type="DCEVParamType" substitutionGroup="EVParam"/>
          <xs:complexType name="DCEVParamType">
            <xs:complexContent><xs:extension base="EVParamBase">
              <xs:sequence><xs:element name="X" type="xs:unsignedInt"/></xs:sequence>
            </xs:extension></xs:complexContent>
          </xs:complexType>
        </xs:schema>
        """;

    [Test]
    public void OptionalSubstitutionInRun_FlattensMembersAsProductions()
    {
        // PowerDeliveryReqType shape: an optional element (Opt1) then an optional substitution
        // reference (EVParam, member DC_EVParam + abstract head), ending in EE. cbV2G grammar
        // 199/200: the members are individual productions in the run's grammar state alongside the
        // sibling optional and the EE — {Opt1, DC_EVParam, head, EE} is one 3-bit state.
        var r = GeneratorHarness.Run(("pd.xsd", OptionalSubstSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // State 0 {Opt1, DC_EVParam, head, EE} = 4 productions -> 3 bits.
        Assert.That(src, Does.Contain("w.WriteBits(0, 3);   // Opt1"));
        Assert.That(src, Does.Contain("w.WriteBits(1, 3);   // DC_EVParam"));
        Assert.That(src, Does.Contain("w.WriteBits(3, 3);   // element EE"));
        // State 1 (Opt1 consumed) {DC_EVParam, head, EE} = 3 productions -> 2 bits.
        Assert.That(src, Does.Contain("w.WriteBits(0, 2);   // DC_EVParam"));
        Assert.That(src, Does.Contain("w.WriteBits(2, 2);   // element EE"));
        // Dispatch is by runtime type; the abstract head reserves its slot but has no branch.
        Assert.That(src, Does.Contain("msg.EVParam is DCEVParamType"));
    }

    private const string RequiredSubstTerminatorSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns="urn:test:cpd" targetNamespace="urn:test:cpd">
          <xs:element name="root" type="CpdType"/>
          <xs:complexType name="CpdType">
            <xs:sequence>
              <xs:element name="Req1" type="xs:unsignedInt"/>
              <xs:element ref="SASch" minOccurs="0"/>
              <xs:element ref="EVSEParam"/>
            </xs:sequence>
          </xs:complexType>
          <xs:element name="SASch" type="SASchBase" abstract="true"/>
          <xs:complexType name="SASchBase" abstract="true"/>
          <xs:element name="SAList" type="SAListType" substitutionGroup="SASch"/>
          <xs:complexType name="SAListType"><xs:complexContent><xs:extension base="SASchBase">
            <xs:sequence><xs:element name="Y" type="xs:unsignedInt"/></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
          <xs:element name="EVSEParam" type="EVSEParamBase" abstract="true"/>
          <xs:complexType name="EVSEParamBase" abstract="true"/>
          <xs:element name="AC_EVSEParam" type="ACEVSEParamType" substitutionGroup="EVSEParam"/>
          <xs:complexType name="ACEVSEParamType"><xs:complexContent><xs:extension base="EVSEParamBase">
            <xs:sequence><xs:element name="A" type="xs:unsignedInt"/></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
          <xs:element name="DC_EVSEParam" type="DCEVSEParamType" substitutionGroup="EVSEParam"/>
          <xs:complexType name="DCEVSEParamType"><xs:complexContent><xs:extension base="EVSEParamBase">
            <xs:sequence><xs:element name="D" type="xs:unsignedInt"/></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
        </xs:schema>
        """;

    [Test]
    public void RequiredSubstitutionTerminatesRun_FoldsMembersIntoState()
    {
        // ChargeParameterDiscoveryResType shape: an optional substitution reference (SASch) followed
        // by a required substitution reference (EVSEParam). cbV2G grammar 284/285: both expansions
        // share the state — {SAList, SASch-head, AC, DC, EVSEParam-head} = 5 productions -> 3 bits;
        // once SASch is consumed, only the required terminator's members remain (3 -> 2 bits).
        var r = GeneratorHarness.Run(("cpd.xsd", RequiredSubstTerminatorSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        // State 0: SAList(0), SASch-head(1, reserved), AC(2), DC(3), EVSEParam-head(4, reserved).
        Assert.That(src, Does.Contain("w.WriteBits(0, 3);   // SAList"));
        Assert.That(src, Does.Contain("w.WriteBits(2, 3);   // AC_EVSEParam"));
        Assert.That(src, Does.Contain("w.WriteBits(3, 3);   // DC_EVSEParam"));
        // State 1 (SASch consumed): the required terminator's members AC(0), DC(1) at 2 bits.
        Assert.That(src, Does.Contain("w.WriteBits(0, 2);   // AC_EVSEParam"));
        Assert.That(src, Does.Contain("w.WriteBits(1, 2);   // DC_EVSEParam"));
    }

    [Test]
    public void SubstitutionInRuns_GeneratedCodeCompiles()
    {
        foreach (var (name, xsd) in new[] { ("pd.xsd", OptionalSubstSchema), ("cpd.xsd", RequiredSubstTerminatorSchema) })
        {
            var r = GeneratorHarness.Run((name, xsd));
            Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
            var errors = GeneratorHarness.CompileErrors(r.GeneratedSource, typeof(Vanaheimr.V2G.Exi.ExiPrimitives));
            Assert.That(errors, Is.Empty,
                r.GeneratedSource + "\n\n" + string.Join("\n", errors.Select(e => e.ToString())));
        }
    }

    // ---- construct #11: an optional bounded-repeating element inside an optional run ----

    private const string OptionalRepeatingSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns="urn:test:st" targetNamespace="urn:test:st">
          <xs:element name="root" type="EntryT"/>
          <xs:complexType name="EntryT">
            <xs:sequence>
              <xs:element name="Req1" type="xs:unsignedInt"/>
              <xs:element name="EPrice" type="xs:unsignedByte" minOccurs="0"/>
              <xs:element name="Cost" type="CostT" minOccurs="0" maxOccurs="3"/>
            </xs:sequence>
          </xs:complexType>
          <xs:complexType name="CostT">
            <xs:sequence><xs:element name="V" type="xs:unsignedInt"/></xs:sequence>
          </xs:complexType>
        </xs:schema>
        """;

    [Test]
    public void OptionalRepeatingInRun_FirstItemIsAStateProduction_RestLoop()
    {
        // SalesTariffEntryType shape: an optional element (EPrice) then an optional bounded-repeating
        // element (Cost, maxOccurs=3), ending in EE. cbV2G grammar 39-42: the FIRST Cost item is a
        // production of the run's grammar state {EPrice, Cost, EE}; further items and the terminating
        // EE use the 2-bit loop {item=0, EE=1}. The bound is enforced by the array, not the grammar.
        var r = GeneratorHarness.Run(("st.xsd", OptionalRepeatingSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var src = r.GeneratedSource;

        Assert.That(src, Does.Contain("IReadOnlyList<CostT> Cost"));
        // State 0 {EPrice(0), Cost-first(1), EE(2)} = 3 productions -> 2 bits.
        Assert.That(src, Does.Contain("w.WriteBits(0, 2);   // EPrice"));
        Assert.That(src, Does.Contain("w.WriteBits(1, 2);   // Cost"));
        Assert.That(src, Does.Contain("w.WriteBits(2, 2);   // element EE"));
        // The loop: further items at code 0, the list-terminating EE at code 1 (both 2-bit).
        Assert.That(src, Does.Contain("w.WriteBits(1, 2);   // element EE (list end)"));
        Assert.That(src, Does.Contain("for (int ci = 1; ci < msg.Cost.Count; ci++)"));
        // Decode reads the first item then loops until the EE.
        Assert.That(src, Does.Contain("if (lc == 1) break;   // element EE (list end)"));
    }

    [Test]
    public void OptionalRepeatingInRun_GeneratedCodeCompiles()
    {
        var r = GeneratorHarness.Run(("st.xsd", OptionalRepeatingSchema));
        Assert.That(r.Diagnostics, Is.Empty, r.GeneratedSource);
        var errors = GeneratorHarness.CompileErrors(r.GeneratedSource, typeof(Vanaheimr.V2G.Exi.ExiPrimitives));
        Assert.That(errors, Is.Empty,
            r.GeneratedSource + "\n\n" + string.Join("\n", errors.Select(e => e.ToString())));
    }

    // ---- fail-loud: an unknown construct must still raise a diagnostic ----

    [Test]
    public void UnknownConstruct_RaisesDiagnostic()
    {
        const string withAll = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:c" targetNamespace="urn:test:c">
              <xs:element name="root" type="RootType"/>
              <xs:complexType name="RootType">
                <xs:all>
                  <xs:element name="A" type="xs:unsignedInt"/>
                  <xs:element name="B" type="xs:unsignedInt"/>
                </xs:all>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("c.xsd", withAll));
        // xs:all is not implemented — the generator must fail loud, not silently skip.
        Assert.That(r.Diagnostics, Is.Not.Empty);
    }
}
