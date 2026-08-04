/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

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

        // Root rotation: the four shapes a scanned root can take relative to one already trusted.
        // Built with System.Security.Cryptography rather than the PKI builder, because what matters
        // here is the *relationship* between two certificates, not an ISO 15118 role profile.
        using var rootKey = System.Security.Cryptography.ECDsa.Create(
                                System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var rotation = RootRotationMaterial(rootKey);

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
            rootRotation = rotation,
            revocation   = RevocationMaterial(good),
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(SourcePath(), json, new UTF8Encoding(false));
        TestContext.Out.WriteLine($"wrote {cases.Length} cases to {SourcePath()}");

    }


    /// <summary>
    /// The four shapes a scanned root can take against one already trusted: itself, the same CA
    /// re-issued, a stranger under the same name, and a successor the trusted root vouched for.
    /// </summary>
    /// <remarks>
    /// Exported rather than built in each port because the *relationships* are the thing being
    /// tested, and three languages constructing their own certificates would be three languages
    /// agreeing with themselves. The one shape that cannot be exported is a compromised key, which
    /// is the same bytes as an honest rotation — that limit is asserted in the ports directly.
    /// </remarks>
    private static object RootRotationMaterial(System.Security.Cryptography.ECDsa rootKey)
    {

        var notBefore = DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(-1);
        var notAfter  = DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(10);

        static System.Security.Cryptography.X509Certificates.X509Certificate2 SelfSigned(
            string commonName, System.Security.Cryptography.ECDsa key,
            DateTimeOffset from, DateTimeOffset to)
        {
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                              $"CN={commonName}", key,
                              System.Security.Cryptography.HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(
                new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(
                    certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 1,
                    critical: true));
            return request.CreateSelfSigned(from, to);
        }

        using var original = SelfSigned("MO Root A", rootKey, notBefore, notAfter);

        // Same key, later dates: the CA re-issued itself.
        using var renewed = SelfSigned("MO Root A", rootKey, notBefore.AddDays(1), notAfter.AddYears(5));

        // Same name, different key, nobody vouching: a stranger wearing a known name.
        using var strangerKey = System.Security.Cryptography.ECDsa.Create(
                                    System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var stranger = SelfSigned("MO Root A", strangerKey, notBefore, notAfter);

        // A successor introduced by the trusted root — the friendly rotation, and not self-signed.
        using var successorKey = System.Security.Cryptography.ECDsa.Create(
                                     System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var successorRequest = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                                   "CN=MO Root A 2031", successorKey,
                                   System.Security.Cryptography.HashAlgorithmName.SHA256);
        successorRequest.CertificateExtensions.Add(
            new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(
                certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 1,
                critical: true));
        using var successor = successorRequest.Create(original, notBefore, notAfter,
                                                      [0x01, 0x02, 0x03, 0x04]);

        static string H(System.Security.Cryptography.X509Certificates.X509Certificate2 c) =>
            Convert.ToHexString(c.RawData).ToLowerInvariant();

        return new
        {
            what      = "A trusted root and three candidates: the same CA re-issued (same key), a "
                      + "stranger under the same name (different key, nobody vouching), and a "
                      + "successor the trusted root signed (a link certificate, not self-signed).",
            trusted   = H(original),
            renewal   = H(renewed),
            stranger  = H(stranger),
            vouched   = H(successor),
        };
    }


    /// <summary>
    /// A CRL and two leaves under it: one revoked, one not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Revocation has <b>three</b> answers, not two, and that is the whole reason this material
    /// exists. "Not on the list" and "no usable list" look identical to a naive check and are not the
    /// same thing at all — the second is the classic soft-fail hole, where an attacker who wants a
    /// revoked credential accepted simply arranges for the list to be unavailable, or supplies an
    /// empty one.
    /// </para>
    /// <para>
    /// So the corpus carries a real CRL, signed by the issuing CA, plus a leaf it revokes and a leaf
    /// it does not — and a second CRL from an <i>unrelated</i> CA, which a validator must refuse to
    /// believe rather than read as "nothing revoked here".
    /// </para>
    /// </remarks>
    private static object RevocationMaterial(V2GHierarchy hierarchy)
    {

        var issuer  = hierarchy.MoSubCa2;               // the CA that signs contract leaves
        var revoked = hierarchy.ContractLeaf;

        static byte[] Crl(V2GIssued signingCa, Org.BouncyCastle.Math.BigInteger[] revokedSerials,
                          DateTime thisUpdate, DateTime nextUpdate)
        {
            var generator = new Org.BouncyCastle.X509.X509V2CrlGenerator();
            generator.SetIssuerDN(signingCa.Certificate.SubjectDN);
            generator.SetThisUpdate(thisUpdate);
            generator.SetNextUpdate(nextUpdate);

            foreach (var serial in revokedSerials)
                generator.AddCrlEntry(serial, thisUpdate,
                                      Org.BouncyCastle.Asn1.X509.CrlReason.KeyCompromise);

            var factory = new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory(
                              V2GCertificateBuilder.SignatureAlgorithmName(signingCa.Algorithm),
                              signingCa.KeyPair.Private);

            return generator.Generate(factory).GetEncoded();
        }

        var now = DateTime.UtcNow.AddMinutes(-5);

        return new
        {
            what = "A CRL from the MO Sub-CA 2 revoking the contract leaf, the leaf it revokes, a "
                 + "sibling leaf it does not, an expired CRL, and a CRL from an unrelated CA. The "
                 + "last two must both come back as UNKNOWN rather than as 'not revoked' — that "
                 + "distinction is the whole point.",
            issuer          = Hex(issuer.Certificate),
            revokedLeaf     = Hex(revoked.Certificate),
            unrevokedLeaf   = Hex(hierarchy.VehicleLeaf.Certificate),
            crl             = Convert.ToHexString(
                                  Crl(issuer, [revoked.Certificate.SerialNumber],
                                      now, now.AddDays(7))).ToLowerInvariant(),
            expiredCrl      = Convert.ToHexString(
                                  Crl(issuer, [revoked.Certificate.SerialNumber],
                                      now.AddDays(-30), now.AddDays(-23))).ToLowerInvariant(),
            crlFromStranger = Convert.ToHexString(
                                  Crl(hierarchy.CpoSubCa2, [], now, now.AddDays(7))).ToLowerInvariant(),
        };
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
