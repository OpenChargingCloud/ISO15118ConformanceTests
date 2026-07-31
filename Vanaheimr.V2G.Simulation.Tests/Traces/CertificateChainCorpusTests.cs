using System.Text;
using System.Text.Json;

using NUnit.Framework;

using Org.BouncyCastle.Security;

using cloud.charging.open.protocols.ISO15118.PKI;
using cloud.charging.open.protocols.ISO15118.PKI.Evil;

namespace Vanaheimr.V2G.Simulation.Tests.Traces;

/// <summary>
/// A corpus of contract certificate chains and what a validator should make of them.
/// </summary>
/// <remarks>
/// <para>
/// The app has to decide whether a scanned contract chain is usable, and that decision is two
/// questions, not one. <b>Is the chain sound</b> — does it build a path to a trusted root, do the
/// signatures verify, are the certificates in date, is every issuer a CA? That has one right answer
/// and no room for a user's opinion. <b>Does the leaf match the ISO 15118 profile</b> — a contract
/// certificate carrying <c>serverAuth</c>, say? The chain is fine and the certificate is not what it
/// claims to be, which is something to *report*, in the same spirit as the pairing payload's warnings.
/// </para>
/// <para>
/// The negatives come from <c>WWCP_ISO15118_PKI</c>'s evil-certificate factory, whose own comment
/// says why it is the right source: <i>"These reuse the good Sub-CAs as issuers wherever it makes
/// sense, so a EVCC/SECC checking only the chain's signature math will still accept them — the bug
/// lies in policy/EKU/timing/algorithm checks."</i> A validator that does PKIX and stops will pass
/// the good case and fail this corpus.
/// </para>
/// <para>
/// Exported for the ports, like every other corpus here: the Swift wallet is held to what C#'s PKI
/// says, not to its own opinion of its own certificates.
/// </para>
/// </remarks>
[TestFixture]
public class CertificateChainCorpusTests
{

    private const string FileName = "Certificate.chain.vectors.json";

    private static string VectorPath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName);

    private static string SourcePath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vanaheimr.V2G.Simulation.Tests.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");
        var vectors = Path.Combine(dir!.FullName, "Vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, FileName);
    }

    private static string Hex(Org.BouncyCastle.X509.X509Certificate certificate) =>
        Convert.ToHexString(certificate.GetEncoded()).ToLowerInvariant();


    /// <summary>
    /// Builds the hierarchies and writes the corpus. <see cref="ExplicitAttribute"/> like every other
    /// generator here: it is an oracle for another language and must change deliberately.
    /// </summary>
    /// <remarks>
    /// Not reproducible run to run — <c>SecureRandom</c> makes fresh keys, so every regeneration is a
    /// wholly new file. Unlike the session traces that is tolerable, because nothing compares these
    /// bytes to anything: the corpus asserts *verdicts*, and a verdict about a fresh hierarchy is the
    /// same verdict. Said out loud so nobody diffs a regeneration expecting signal.
    /// </remarks>
    [Test, Explicit("Regenerates Vectors/Certificate.chain.vectors.json — run deliberately")]
    public void RegenerateTheCorpus()
    {

        var random    = new SecureRandom();
        var options   = new V2GProfileOptions(V2GProfileFlavor.Strict15118_2, V2GAlgorithm.EcdsaP256,
                                              V2GPolicySet.FromArc("1.3.6.1.4.1.99999.1"));

        var good      = V2GHierarchy.Build(V2GAlgorithm.EcdsaP256, random, V2GProfileOptions: options);
        var stranger  = V2GHierarchy.Build(V2GAlgorithm.EcdsaP256, random, "Other",
                                           V2GProfileOptions: options);
        var evil      = V2GEvilFactory.Build(good, random);

        var withServerAuth = evil.Single(v => v.Name == "contract_with_serverauth");

        var cases = new object[]
        {
            new
            {
                name  = "good",
                what  = "The contract chain from the hierarchy: leaf, MO Sub-CA 2, MO Sub-CA 1.",
                chain = new[] { Hex(good.ContractLeaf.Certificate),
                                Hex(good.MoSubCa2.Certificate),
                                Hex(good.MoSubCa1.Certificate) },
                trusted  = true,
                findings = Array.Empty<string>(),
            },
            new
            {
                name  = "contract_with_serverauth",
                what  = "Sound chain, wrong certificate: the contract leaf also carries serverAuth, so "
                      + "it could be presented as a station. PKIX alone accepts this — the profile does not.",
                chain = new[] { Hex(withServerAuth.Issued.Certificate),
                                Hex(good.MoSubCa2.Certificate),
                                Hex(good.MoSubCa1.Certificate) },
                trusted  = true,
                findings = new[] { "serverAuthOnContractCertificate" },
            },
            new
            {
                name  = "untrusted_root",
                what  = "A complete, internally consistent chain from a different hierarchy. Every "
                      + "signature verifies; it simply does not reach the root we trust.",
                chain = new[] { Hex(stranger.ContractLeaf.Certificate),
                                Hex(stranger.MoSubCa2.Certificate),
                                Hex(stranger.MoSubCa1.Certificate) },
                trusted  = false,
                findings = Array.Empty<string>(),
            },
            new
            {
                name  = "missing_intermediate",
                what  = "The good leaf with MO Sub-CA 2 left out. No path can be built, even though "
                      + "every certificate present is genuine — the failure mode of a truncated bundle.",
                chain = new[] { Hex(good.ContractLeaf.Certificate),
                                Hex(good.MoSubCa1.Certificate) },
                trusted  = false,
                findings = Array.Empty<string>(),
            },
            new
            {
                name  = "chain_out_of_order",
                what  = "Every certificate the good case has, leaf last. Refused — not because a path "
                      + "cannot be built, but because the order is what states WHICH certificate is the "
                      + "contract one. A validator that reorders and picks a plausible leaf is guessing "
                      + "at an identity; this corpus caught exactly that, by accepting a sub-CA as the "
                      + "leaf and calling it trusted.",
                chain = new[] { Hex(good.MoSubCa1.Certificate),
                                Hex(good.MoSubCa2.Certificate),
                                Hex(good.ContractLeaf.Certificate) },
                trusted  = false,
                findings = Array.Empty<string>(),
            },
        };

        var json = JsonSerializer.Serialize(new
        {
            note = "Contract certificate chains and the verdict a validator should reach, generated by "
                 + "WWCP_ISO15118_PKI. 'trusted' is the chain question — path, signatures, dates, CA "
                 + "flags — and has one right answer. 'findings' are ISO 15118 profile deviations: the "
                 + "chain is sound and the certificate is not what it claims, which is reported rather "
                 + "than decided. Regenerating produces entirely new keys; nothing compares these bytes "
                 + "to anything, only the verdicts.",
            root  = Hex(good.Root.Certificate),
            cases,
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(SourcePath(), json, new UTF8Encoding(false));
        TestContext.Out.WriteLine($"wrote {cases.Length} cases to {SourcePath()}");

    }


    /// <summary>The corpus still says what it was built to say — the shapes, not the bytes.</summary>
    [Test]
    public void TheCorpusCoversTheCasesItWasBuiltFor()
    {

        Assert.That(File.Exists(VectorPath), Is.True, $"corpus missing: {VectorPath} — run RegenerateTheCorpus");

        var corpus = JsonDocument.Parse(File.ReadAllText(VectorPath)).RootElement;
        var names  = corpus.GetProperty("cases").EnumerateArray()
                           .Select(c => c.GetProperty("name").GetString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(corpus.GetProperty("root").GetString(), Is.Not.Empty);
            Assert.That(names, Does.Contain("good"), "without a positive case the rest proves nothing");
            Assert.That(names, Does.Contain("untrusted_root"));
            Assert.That(names, Does.Contain("missing_intermediate"));

            // The case that separates a real validator from one that only does path maths.
            Assert.That(names, Does.Contain("contract_with_serverauth"));

            var profileCase = corpus.GetProperty("cases").EnumerateArray()
                                    .Single(c => c.GetProperty("name").GetString() == "contract_with_serverauth");

            Assert.That(profileCase.GetProperty("trusted").GetBoolean(), Is.True,
                        "the chain is sound — that is exactly why the finding has to carry it");
            Assert.That(profileCase.GetProperty("findings").EnumerateArray().Count(), Is.EqualTo(1));
        });

    }

}
