using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using Vanaheimr.V2G.Exi.Tests.Infrastructure;
using Vanaheimr.V2G.Iso15118_20.CommonMessages;
using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Interop
{
    /// <summary>
    /// Documents (does not "fix") the Plug &amp; Charge signature-verification finding from the live -20 PnC
    /// interop run (Josev EVCC → our SECC, <c>docs/interop-runs/2026-07-21-iso20-dc-pnc-tls/</c>).
    /// <para>
    /// Decoding Josev's real signed PnC <c>AuthorizationReq</c> and verifying it offline reproduces the live
    /// result exactly, and pins down that this is <b>not a codec bug</b>:
    /// </para>
    /// <list type="bullet">
    ///   <item>The <b>reference digest verifies</b> (asserted below): SHA-256 of our re-encoded
    ///     <c>PnC_AReqAuthorizationMode</c> fragment equals Josev's <c>DigestValue</c> byte-for-byte — our
    ///     signed-element fragment codec is <b>byte-exact vs Josev/EXIficient</b>.</item>
    ///   <item>The <b>SignedInfo signature does not verify</b> (asserted below) against our cbV2G-matched
    ///     fragment — and, per <c>tools/exificient-ref</c> (see that finding), also not against EXIficient's
    ///     default namespace-preserving fragment (245 B) nor its Canonical EXI / W3C exi-c14n form (246 B).</item>
    /// </list>
    /// The crypto is sound (P-256 contract leaf <c>CN=UKSWI123456791A</c>, 64-byte r‖s signature). So Josev
    /// signs a <c>SignedInfo</c> octet form that no standard EXI encoder we have reproduces — an interop
    /// question about Josev's specific signing canonicalization, <b>not</b> a bug in our byte-exact codec, which
    /// per the project's ground rule must not be changed speculatively based on a non-reference stack.
    /// </summary>
    [TestFixture]
    public class JosevPnCSignatureDiag
    {
        [Test]
        public void ReferenceDigestIsByteExact_ButJosevSignedInfoSignatureIsANonReproducibleForm()
        {
            var bytes = HexUtil.Parse(JosevCapturedFrames20Tests.SignedAuthorizationReqHex);
            var req = (AuthorizationReq)CommonMessagesCodec.DecodeAny(bytes, out _);
            var pnc = req.PnC_AReqAuthorizationMode!;
            var sig = req.Header.Signature!;
            var reference = sig.SignedInfo.Reference[0];
            var hashName = sig.SignedInfo.SignatureMethod.Algorithm.Contains("sha256") ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA512;

            // 1. Reference digest over our re-encoded signed-element fragment matches Josev's byte-for-byte —
            //    proving our fragment codec is byte-exact (this is the strong conformance result).
            var frag = new byte[8192];
            Assert.That(CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(pnc, frag, out int fn), Is.True);
            var digest = reference.DigestMethod.Algorithm.Contains("sha256") ? SHA256.HashData(frag.AsSpan(0, fn)) : SHA512.HashData(frag.AsSpan(0, fn));
            Assert.That(digest.AsSpan().SequenceEqual(reference.DigestValue), Is.True,
                "reference digest must match — our signed-element fragment codec is byte-exact vs Josev/EXIficient");

            // Crypto is well-formed: P-256 contract leaf, 64-byte r‖s signature.
            using var contract = X509CertificateLoader.LoadCertificate(pnc.ContractCertificateChain.Certificate);
            Assert.That(contract.GetECDsaPublicKey()!.KeySize, Is.EqualTo(256));
            Assert.That(sig.SignatureValue.Value.Length, Is.EqualTo(64));

            // 2. Josev's SignedInfo signature does NOT verify against our (byte-exact) fragment encoding — Josev
            //    signs a SignedInfo octet form that no standard EXI encoder reproduces (see the class summary).
            //    If a future canonicalization effort makes this verify, flip the assertion and celebrate.
            using var ecdsa = contract.GetECDsaPublicKey()!;
            bool sigOk = ecdsa.VerifyData(V2GSignature.SignedInfoFragment(sig.SignedInfo), sig.SignatureValue.Value,
                hashName, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            Assert.That(sigOk, Is.False, "documents the open interop finding — see the class summary");
        }
    }
}
