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

    // ---- fail-loud: an unknown construct must still raise a diagnostic ----

    [Test]
    public void UnknownConstruct_RaisesDiagnostic()
    {
        const string withChoice = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns="urn:test:c" targetNamespace="urn:test:c">
              <xs:element name="root" type="RootType"/>
              <xs:complexType name="RootType">
                <xs:choice>
                  <xs:element name="A" type="xs:unsignedInt"/>
                  <xs:element name="B" type="xs:unsignedInt"/>
                </xs:choice>
              </xs:complexType>
            </xs:schema>
            """;
        var r = GeneratorHarness.Run(("c.xsd", withChoice));
        // xs:choice is not implemented yet — the generator must fail loud, not silently skip.
        Assert.That(r.Diagnostics, Is.Not.Empty);
    }
}
