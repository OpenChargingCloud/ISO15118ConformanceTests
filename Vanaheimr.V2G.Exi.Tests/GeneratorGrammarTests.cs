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
