/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ISO15118ConformanceTests
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

// -2 unqualified, -20 behind an alias. The two protocols name the same things — ContractProvisioning,
// V2GSignature, CertificateChainType, ResponseCode, Processing — and every one of those pairs is a
// different type with different rules. Importing both namespaces would make each use ambiguous, and
// resolving the ambiguity case by case is exactly the kind of thing that goes wrong silently.
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;

using Iso20             = cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using Iso20Sig          = cloud.charging.open.protocols.ISO15118_20.CommonMessages.V2GSignature;
using Iso20Check        = cloud.charging.open.protocols.ISO15118.StateMachines.Iso20.Iso20ContractCheck;
using Iso20Provisioning = cloud.charging.open.protocols.ISO15118.StateMachines.Iso20.ContractProvisioning;

namespace ISO15118ConformanceTests.Simulation.Traces;

/// <summary>
/// A corpus of contract-provisioning responses and the verdict an EVCC should reach about each —
/// ISO 15118-<b>2</b> §7.9.2.4 (Installation and Update) and ISO 15118-<b>20</b> CertificateInstallation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a corpus and not a recorded session.</b> Two separate reasons here, where the tariff and
/// price-schedule corpora had one.
/// </para>
/// <para>
/// The first is the familiar one: the verdict never reaches the wire. A car checks the response's
/// signature and installs — or does not — and tells the station nothing either way. Replaying a recorded
/// provisioning session proves a port can <i>parse</i> the answer; it cannot prove the port <i>judges</i>
/// it, and a verifier that always says yes is indistinguishable on a happy path from one that works.
/// </para>
/// <para>
/// The second is specific to provisioning, and stronger. What the car ends up holding is a <b>private
/// key it never saw</b>: the station derives an ECDH secret, runs a KDF, and encrypts the contract's raw
/// scalar. Whether a port's KDF agrees with everyone else's is not observable from any message — the
/// recovered scalar is never transmitted, never echoed, never acknowledged. The only way to state the
/// property at all is to write down the ciphertext, the receiving key, and the exact scalar that must
/// come out. That is what <c>recoveredKeyD</c> is, and it is the single most load-bearing field here.
/// </para>
/// <para>
/// <b>The two protocols share no code and the corpus keeps them apart.</b> -2 is P-256, an ANSI X9.63
/// KDF (counter <i>after</i> Z), AES-128-<b>CBC</b>, and four signature references. -20 is P-521, a
/// ConcatKDF (counter <i>before</i> Z), AES-256-<b>GCM</b>, and one reference. Every one of those
/// differences is a place a port could quietly reuse the wrong half, which is why both halves are
/// written out rather than parameterised over a curve.
/// </para>
/// <para>
/// <b>CBC authenticates nothing, and the corpus says so out loud.</b> The -2 <c>install-wrong-receiver</c>
/// case records the 32 bytes of nonsense a wrong unwrap produces, not an error — because that is what -2
/// does, and a car that stops at "the decryption succeeded" installs it. Its -20 counterpart records
/// <c>keyRecovered: false</c> instead: GCM's tag check throws. Two protocols, two truths, and a port that
/// copied one to the other fails exactly one of these cases. It is also why <c>keyMatchesCertificate</c>
/// is a -2 field only: -20 does not need the check, because its cipher already made it.
/// </para>
/// <para>
/// <b>Honesty note, inherited.</b> The KDFs are the ones <c>ContractProvisioning</c> implements, with
/// empty SharedInfo/OtherInfo. No external stack implements either exchange to diff against — Josev
/// raises <c>NotImplementedError</c> on -20 provisioning on both sides — so these payload octets are
/// self-consistent across our three languages and nothing more. The wire <i>messages</i> around them stay
/// byte-exact per the usual oracles. This is a cross-port agreement corpus, not a conformance claim.
/// </para>
/// <para>
/// Keys and certificate validity are fixed so that the identities in this file are stable across
/// regenerations. The bytes are not: ECDSA picks a fresh nonce, the key wrap picks a fresh ephemeral key
/// and IV, and .NET picks a fresh certificate serial. A regeneration therefore rewrites every frame,
/// which is one more reason it is <see cref="ExplicitAttribute"/>.
/// </para>
/// </remarks>
[TestFixture]
public class ContractProvisioningCorpusTests
{

    private const string FileName = "Contract.provisioning.vectors.json";

    // ── the fixed identities ──────────────────────────────────────────────────────────────────────
    //
    // P-256 for -2, P-521 for -20, because the two key transports are on different curves and cannot
    // share material even as test data.

    /// <summary>The Secondary Actor's provisioning key — what signs a -2 response, and whose certificate
    /// travels in the response as the verify key.</summary>
    private const string Iso2ProvisioningKeyD =
        "3f9a2c5d8e10b74f36c95a2e8d417b06f3a95c2d84e017b6395fa2c8d10e47b3";

    /// <summary>An unrelated -2 key: what the <c>install-wrong-key</c> case signs with, while the response
    /// still carries the real provisioning certificate.</summary>
    private const string Iso2StrangerKeyD =
        "17c3e95a2d8f4061b73ea95c2d08f461b39ea75c2d18f403b69ea25c3d80f471";

    /// <summary>The car's own credential — the OEM provisioning key for an installation, the expiring
    /// contract key for an update. The answer is wrapped for this, and the port unwraps with it.</summary>
    private const string Iso2ReceiverKeyD =
        "5b8e10c47a2d93f605c8b1e42d97a306f5b8c1e42d97a3f605c8b1e42d97a306";

    /// <summary>A second car's credential: the <c>install-wrong-receiver</c> case wraps for this.</summary>
    private const string Iso2OtherReceiverKeyD =
        "2a7d05b3e91c48f627a0d5b3e91c48f627a0d5b3e91c48f627a0d5b3e91c48f6";

    /// <summary>The contract key the operator issues. Fixed, so <c>recoveredKeyD</c> is a stable
    /// statement rather than a copy of whatever the last run happened to mint.</summary>
    private const string Iso2ContractKeyD =
        "0c4e81b7a35d92f608c4e81b7a35d92f608c4e81b7a35d92f608c4e81b7a35d9";

    private const string Iso20CpsKeyD =
        "01a4f2c3d95b8e70612fa4c8d35b90e7418fa2c6d095b74e128fa03d76c5b192e8437ca6"
      + "05d29b4718fe30c6a95d472b8103fae62d59b0748125fac396d0b7e5c41a";

    private const string Iso20StrangerKeyD =
        "00c7e5b418fa2c6d095b74e128fa03d76c5b192e8437ca605d29b4718fe30c6a95d472b8"
      + "103fae62d59b0748125fac396d0b7e5c41d4f2a3c95b8e70612fa4c8d357";

    /// <summary>The car's OEM provisioning key. P-521, because that is the only curve -20's key transport
    /// can agree on — a P-256 OEM certificate cannot take part at all.</summary>
    private const string Iso20ReceiverKeyD =
        "013b7e5c418fa2c6d095b74e128fa03d76c5b192e8437ca605d29b4718fe30c6a95d472b"
      + "8103fae62d59b0748125fac396d0b7e5c41d4f2a3c95b8e70612fa4c8d31";

    private const string Iso20OtherReceiverKeyD =
        "008fa2c6d095b74e128fa03d76c5b192e8437ca605d29b4718fe30c6a95d472b8103fae6"
      + "2d59b0748125fac396d0b7e5c41d4f2a3c95b8e70612fa4c8d35b3b7e5c4";

    private const string Iso20ContractKeyD =
        "0195d472b8103fae62d59b0748125fac396d0b7e5c41d4f2a3c95b8e70612fa4c8d35b90"
      + "e7418fa2c6d095b74e128fa03d76c5b192e8437ca605d29b4718fe30c6a2";

    /// <summary>The eMAID the -2 installation is issued under, and the one an update carries over.</summary>
    private const string InstalledEmaid = "DE-VAN-C00000001-6";
    private const string RenewedEmaid   = "DE-VAN-C00000009-7";

    /// <summary>Pinned validity, so a regenerated certificate differs from its predecessor only in the
    /// serial .NET mints for it.</summary>
    private static readonly DateTimeOffset NotBefore = DateTimeOffset.FromUnixTimeSeconds(1_767_225_600);
    private static readonly DateTimeOffset NotAfter  = DateTimeOffset.FromUnixTimeSeconds(1_830_297_600);


    private static ECDsa P256(string d) => KeyFrom(ECCurve.NamedCurves.nistP256, d);
    private static ECDsa P521(string d) => KeyFrom(ECCurve.NamedCurves.nistP521, d);

    private static ECDsa KeyFrom(ECCurve curve, string d)
    {
        var key = ECDsa.Create(curve);
        key.ImportParameters(new ECParameters { Curve = curve, D = Convert.FromHexString(d) });
        return key;
    }

    private static ECDiffieHellman AgreementFrom(ECCurve curve, string d)
    {
        var key = ECDiffieHellman.Create(curve);
        key.ImportParameters(new ECParameters { Curve = curve, D = Convert.FromHexString(d) });
        return key;
    }

    /// <summary>A self-signed leaf for <paramref name="key"/>, with the key usages a provisioning
    /// credential needs: DigitalSignature to sign its request, KeyAgreement to receive the wrapped
    /// contract. Without the second, .NET refuses to hand out an ECDH view of the certificate at all —
    /// which is a real deployment mistake and not only a test detail.</summary>
    private static byte[] SelfSigned(string subject, ECDsa key, HashAlgorithmName hash)
    {
        var request = new CertificateRequest(subject, key, hash);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement, true));
        using var certificate = request.CreateSelfSigned(NotBefore, NotAfter);
        return certificate.RawData;
    }

    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    /// <summary>The private scalar of a key, fixed-width for the curve — what a port must produce from the
    /// ciphertext, and the field the whole corpus is really about.</summary>
    private static string ScalarOf(ECDsa key, int width)
    {
        var d = key.ExportParameters(true).D!;
        if (d.Length == width) return Hex(d);
        var padded = new byte[width];
        d.CopyTo(padded, width - d.Length);
        return Hex(padded);
    }


    #region ISO 15118-2

    /// <summary>The four signed elements of either -2 response, with the contract key wrapped for
    /// <paramref name="receiver"/>. Their <c>Id</c>s are the reference URIs, and their order is the order
    /// §7.9.2.4.2 signs them.</summary>
    private static (CertificateChainType Chain, ContractSignatureEncryptedPrivateKeyType Key,
                    DiffieHellmanPublickeyType DhPublicKey, EMAIDType Emaid) Iso2Elements(
        ECDiffieHellman receiver, ECDsa contractKey, string emaid)
    {

        var (dhPublicKey, wrapped) = ContractProvisioning.EncryptContractKey(receiver.PublicKey, contractKey);

        return (new CertificateChainType("id1", SelfSigned($"CN={emaid}, O=Vanaheimr (dev)", contractKey,
                                                           HashAlgorithmName.SHA256), SubCertificates: null),
                new ContractSignatureEncryptedPrivateKeyType("id2", wrapped),
                new DiffieHellmanPublickeyType("id3", dhPublicKey),
                new EMAIDType("id4", emaid));

    }

    /// <summary>The header signature over the four elements. <paramref name="omitEmaid"/> drops the fourth
    /// reference — the response is otherwise perfectly sound, and a verifier that reads the references it
    /// finds rather than the four it is owed accepts it.</summary>
    private static SignatureType SignIso2(CertificateChainType chain,
                                          ContractSignatureEncryptedPrivateKeyType key,
                                          DiffieHellmanPublickeyType dhPublicKey,
                                          EMAIDType emaid,
                                          ECDsa signingKey,
                                          bool standalone = false,
                                          bool omitEmaid  = false)
    {

        var buf = new byte[4096];
        var references = new List<(string, byte[])>();

        if (Iso2Codec.EncodeFragment_ContractSignatureCertChain(chain, buf, out int n1))
            references.Add((chain.Id!, V2GSignature.Digest(buf.AsSpan(0, n1))));
        if (Iso2Codec.EncodeFragment_ContractSignatureEncryptedPrivateKey(key, buf, out int n2))
            references.Add((key.Id, V2GSignature.Digest(buf.AsSpan(0, n2))));
        if (Iso2Codec.EncodeFragment_DHpublickey(dhPublicKey, buf, out int n3))
            references.Add((dhPublicKey.Id, V2GSignature.Digest(buf.AsSpan(0, n3))));
        if (!omitEmaid && Iso2Codec.EncodeFragment_eMAID(emaid, buf, out int n4))
            references.Add((emaid.Id, V2GSignature.Digest(buf.AsSpan(0, n4))));

        var signedInfo = V2GSignature.BuildSignedInfo(references, includeExiTransform: true);

        var value = standalone
            ? signingKey.SignData(XmlDsigInterop2.StandaloneOctets(signedInfo)
                                      ?? throw new InvalidOperationException("standalone encode failed."),
                                  HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            : V2GSignature.Sign(signedInfo, signingKey);

        return V2GSignature.BuildSignature(signedInfo, value);

    }

    private static string EncodeIso2(BodyBaseType body, SignatureType? signature)
    {

        var message = new V2G_Message(
            new MessageHeaderType(SessionID: Convert.FromHexString("00a1b2c3d4e5f607"),
                                  Notification: null, Signature: signature),
            new BodyType(body));

        var buf = new byte[16384];
        if (!message.TryEncode(buf, out int n))
            throw new InvalidOperationException("provisioning response encode failed.");

        return Hex(buf.AsSpan(0, n));

    }

    #endregion

    #region ISO 15118-20

    private static Iso20.SignedInstallationDataType Iso20InstallData(ECDiffieHellman receiver, ECDsa contractKey,
                                                                     ECDsa subCaKey, string subject)
    {

        var (dhPublicKey, wrapped) = Iso20Provisioning.EncryptContractKey(receiver.PublicKey, contractKey);

        return new Iso20.SignedInstallationDataType("sid1",
            new Iso20.ContractCertificateChainType(
                SelfSigned(subject, contractKey, HashAlgorithmName.SHA512),
                new Iso20.SubCertificatesType(new[] {
                    SelfSigned("CN=Vanaheimr MO Sub-CA (dev)", subCaKey, HashAlgorithmName.SHA512) })),
            Iso20.EcdhCurve.SECP521, dhPublicKey,
            SECP521_EncryptedPrivateKey: wrapped, X448_EncryptedPrivateKey: null, TPM_EncryptedPrivateKey: null);

    }

    private static Iso20.SignatureType SignIso20(Iso20.SignedInstallationDataType data, ECDsa signingKey,
                                                 string? referenceId = null)
    {

        var buf = new byte[8192];
        if (!Iso20.CommonMessagesCodec.EncodeFragment_SignedInstallationData(data, buf, out int n))
            throw new InvalidOperationException("SignedInstallationData fragment encode failed.");

        var signedInfo = Iso20Sig.BuildSignedInfo(referenceId ?? data.Id,
                             Iso20Sig.Digest(buf.AsSpan(0, n)), includeExiTransform: true);

        return Iso20Sig.BuildSignature(signedInfo, Iso20Sig.Sign(signedInfo, signingKey));

    }

    private static Iso20.CertificateInstallationRes Iso20Response(Iso20.SignedInstallationDataType data,
                                                                  byte[] cpsCertificate,
                                                                  Iso20.SignatureType? signature) =>
        new(new Iso20.MessageHeaderType(Convert.FromHexString("00a1b2c3d4e5f607"), 1_767_225_600, signature),
            Iso20.ResponseCode.OK, Iso20.Processing.Finished,
            CPSCertificateChain: new Iso20.CertificateChainType(cpsCertificate, SubCertificates: null),
            SignedInstallationData: data,
            RemainingContractCertificateChains: 0);

    private static string EncodeIso20(Iso20.CertificateInstallationRes res)
    {
        var buf = new byte[32768];
        if (!Iso20.CommonMessagesCodec.TryEncodeAny(res, buf, out int n))
            throw new InvalidOperationException("CertificateInstallationRes encode failed.");
        return Hex(buf.AsSpan(0, n));
    }

    #endregion


    /// <summary>
    /// Regenerates the corpus. <see cref="ExplicitAttribute"/> because the file is an oracle for two other
    /// languages: it must change when someone means it to, never as a side effect of a run.
    /// </summary>
    [Test, Explicit("Regenerates vectors/Contract.provisioning.vectors.json — run deliberately")]
    public void RegenerateTheCorpus()
    {

        using var provisioningKey = P256(Iso2ProvisioningKeyD);
        using var iso2Stranger    = P256(Iso2StrangerKeyD);
        using var iso2Contract    = P256(Iso2ContractKeyD);
        using var iso2Receiver    = AgreementFrom(ECCurve.NamedCurves.nistP256, Iso2ReceiverKeyD);
        using var iso2Other       = AgreementFrom(ECCurve.NamedCurves.nistP256, Iso2OtherReceiverKeyD);

        var provisioningChain = new CertificateChainType(
            Id: null, SelfSigned("CN=Vanaheimr SA (dev)", provisioningKey, HashAlgorithmName.SHA256),
            SubCertificates: null);

        var iso2ContractScalar = ScalarOf(iso2Contract, 32);
        var iso2Cases = new List<object>();

        // (1) the ordinary installation. Everything holds, and the car ends up with the contract key.
        {
            var (chain, key, dh, emaid) = Iso2Elements(iso2Receiver, iso2Contract, InstalledEmaid);
            iso2Cases.Add(new
            {
                name = "install-signed",
                what = "A sound CertificateInstallationRes: four references, all digests matching, the "
                     + "SignedInfo signed by the leaf of the SA provisioning chain the message itself "
                     + "carries, and the contract key wrapped for the car's OEM key.",
                frame        = EncodeIso2(new CertificateInstallationResType(
                                   ResponseCode.OK, provisioningChain, chain, key, dh, emaid),
                                   SignIso2(chain, key, dh, emaid, provisioningKey)),
                receiverKeyD = Iso2ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 4, digestOk = true, signatureOk = true,
                    signatureGrammar = "iso2-msgdef",
                    recoveredKeyD = iso2ContractScalar, keyMatchesCertificate = true,
                    emaid = InstalledEmaid,
                },
            });
        }

        // (2) the same, signed under Josev's standalone xmldsig grammar. Same SignedInfo on the wire,
        //     different octets underneath — the case that makes trying both grammars necessary.
        {
            var (chain, key, dh, emaid) = Iso2Elements(iso2Receiver, iso2Contract, InstalledEmaid);
            iso2Cases.Add(new
            {
                name = "install-standalone",
                what = "The same installation with the SignedInfo signed over its STANDALONE xmldsig "
                     + "encoding. A verifier that knows only ISO's grammar rejects a peer in the field.",
                frame        = EncodeIso2(new CertificateInstallationResType(
                                   ResponseCode.OK, provisioningChain, chain, key, dh, emaid),
                                   SignIso2(chain, key, dh, emaid, provisioningKey, standalone: true)),
                receiverKeyD = Iso2ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 4, digestOk = true, signatureOk = true,
                    signatureGrammar = "xmldsig-standalone",
                    recoveredKeyD = iso2ContractScalar, keyMatchesCertificate = true,
                    emaid = InstalledEmaid,
                },
            });
        }

        // (3) no signature at all. Unlike an unsigned tariff — which most stations send — this is a
        //     station handing out a contract that nobody vouched for.
        {
            var (chain, key, dh, emaid) = Iso2Elements(iso2Receiver, iso2Contract, InstalledEmaid);
            iso2Cases.Add(new
            {
                name = "install-unsigned",
                what = "A response with no header Signature. The key still unwraps and still belongs to "
                     + "the certificate — which is exactly the trap: everything the car can check WITHOUT "
                     + "the signature says yes.",
                frame        = EncodeIso2(new CertificateInstallationResType(
                                   ResponseCode.OK, provisioningChain, chain, key, dh, emaid), signature: null),
                receiverKeyD = Iso2ReceiverKeyD,
                expected = new
                {
                    signaturePresent = false, references = 0, digestOk = false, signatureOk = false,
                    signatureGrammar = "none",
                    recoveredKeyD = iso2ContractScalar, keyMatchesCertificate = true,
                    emaid = InstalledEmaid,
                },
            });
        }

        // (4) signed correctly, then the eMAID was edited. The ECDSA signature still verifies — it covers
        //     the SignedInfo, never the elements — and only the digest catches it.
        {
            var (chain, key, dh, emaid) = Iso2Elements(iso2Receiver, iso2Contract, InstalledEmaid);
            var tampered = new EMAIDType(emaid.Id, RenewedEmaid);
            iso2Cases.Add(new
            {
                name = "install-digest-tampered",
                what = $"Signed over eMAID {InstalledEmaid}, sent with {RenewedEmaid}. The signature "
                     + "verifies and the digest does not: a verifier that checks the ECDSA half alone "
                     + "installs a contract under an identity the operator never signed for.",
                frame        = EncodeIso2(new CertificateInstallationResType(
                                   ResponseCode.OK, provisioningChain, chain, key, dh, tampered),
                                   SignIso2(chain, key, dh, emaid, provisioningKey)),
                receiverKeyD = Iso2ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 4, digestOk = false, signatureOk = true,
                    signatureGrammar = "iso2-msgdef",
                    recoveredKeyD = iso2ContractScalar, keyMatchesCertificate = true,
                    emaid = RenewedEmaid,
                },
            });
        }

        // (5) three references where §7.9.2.4.2 asks for four. Every reference present is sound, so a
        //     verifier that iterates what it was given rather than what it is owed reports this as fine.
        {
            var (chain, key, dh, emaid) = Iso2Elements(iso2Receiver, iso2Contract, InstalledEmaid);
            iso2Cases.Add(new
            {
                name = "install-three-references",
                what = "The eMAID reference is missing; the other three verify perfectly. The eMAID is the "
                     + "identity the contract is issued under, so an unsigned one is a contract issued to "
                     + "whoever the station felt like naming.",
                frame        = EncodeIso2(new CertificateInstallationResType(
                                   ResponseCode.OK, provisioningChain, chain, key, dh, emaid),
                                   SignIso2(chain, key, dh, emaid, provisioningKey, omitEmaid: true)),
                receiverKeyD = Iso2ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 3, digestOk = false, signatureOk = true,
                    signatureGrammar = "iso2-msgdef",
                    recoveredKeyD = iso2ContractScalar, keyMatchesCertificate = true,
                    emaid = InstalledEmaid,
                },
            });
        }

        // (6) a sound signature by the wrong key, while the response still carries the real provisioning
        //     certificate. Both grammars are tried against that certificate and neither matches.
        {
            var (chain, key, dh, emaid) = Iso2Elements(iso2Receiver, iso2Contract, InstalledEmaid);
            iso2Cases.Add(new
            {
                name = "install-wrong-key",
                what = "Signed by a stranger, sent with the genuine SA provisioning chain. Digests hold — "
                     + "the stranger signed the real elements — and the signature does not.",
                frame        = EncodeIso2(new CertificateInstallationResType(
                                   ResponseCode.OK, provisioningChain, chain, key, dh, emaid),
                                   SignIso2(chain, key, dh, emaid, iso2Stranger)),
                receiverKeyD = Iso2ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 4, digestOk = true, signatureOk = false,
                    signatureGrammar = "none",
                    recoveredKeyD = iso2ContractScalar, keyMatchesCertificate = true,
                    emaid = InstalledEmaid,
                },
            });
        }

        // (7) the case CBC makes possible: a flawless signature over a key wrapped for somebody else. The
        //     unwrap "succeeds" and produces 32 bytes belonging to nobody.
        {
            var (chain, key, dh, emaid) = Iso2Elements(iso2Other, iso2Contract, InstalledEmaid);
            using var wrong = ContractProvisioning.RecoverContractKey(iso2Receiver, dh.Value, key.Value);

            iso2Cases.Add(new
            {
                name = "install-wrong-receiver",
                what = "Signature and digests all hold; the contract key is wrapped for a DIFFERENT car. "
                     + "AES-CBC authenticates nothing, so the unwrap does not fail — it yields the scalar "
                     + "recorded here, which is a perfectly valid private key belonging to nobody. Only "
                     + "checking the key against the certificate it arrived with catches this.",
                frame        = EncodeIso2(new CertificateInstallationResType(
                                   ResponseCode.OK, provisioningChain, chain, key, dh, emaid),
                                   SignIso2(chain, key, dh, emaid, provisioningKey)),
                receiverKeyD = Iso2ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 4, digestOk = true, signatureOk = true,
                    signatureGrammar = "iso2-msgdef",
                    recoveredKeyD = ScalarOf(wrong, 32), keyMatchesCertificate = false,
                    emaid = InstalledEmaid,
                },
            });
        }

        // (8) the renewal. Same shape, different message — and wrapped for the contract it replaces, which
        //     is what makes an update self-authenticating.
        {
            var (chain, key, dh, emaid) = Iso2Elements(iso2Receiver, iso2Contract, RenewedEmaid);
            iso2Cases.Add(new
            {
                name = "update-signed",
                what = "A CertificateUpdateRes, wrapped for the EXPIRING contract's key rather than an OEM "
                     + "key. The verdict path is identical to an installation's — a port that handles only "
                     + "the installation message decodes this as an unexpected body and gives up.",
                frame        = EncodeIso2(new CertificateUpdateResType(
                                   ResponseCode.OK, provisioningChain, chain, key, dh, emaid, RetryCounter: null),
                                   SignIso2(chain, key, dh, emaid, provisioningKey)),
                receiverKeyD = Iso2ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 4, digestOk = true, signatureOk = true,
                    signatureGrammar = "iso2-msgdef",
                    recoveredKeyD = iso2ContractScalar, keyMatchesCertificate = true,
                    emaid = RenewedEmaid,
                },
            });
        }


        // ── ISO 15118-20 ──────────────────────────────────────────────────────────────────────────

        using var cpsKey        = P521(Iso20CpsKeyD);
        using var iso20Stranger = P521(Iso20StrangerKeyD);
        using var iso20Contract = P521(Iso20ContractKeyD);
        using var iso20Receiver = AgreementFrom(ECCurve.NamedCurves.nistP521, Iso20ReceiverKeyD);
        using var iso20Other    = AgreementFrom(ECCurve.NamedCurves.nistP521, Iso20OtherReceiverKeyD);

        var cpsCertificate      = SelfSigned("CN=Vanaheimr CPS (dev)", cpsKey, HashAlgorithmName.SHA512);
        var iso20ContractScalar = ScalarOf(iso20Contract, 66);
        const string contractSubject = "CN=DE*VAN*C*000001, O=Vanaheimr (dev)";

        var iso20Cases = new List<object>();

        // (1) the ordinary installation.
        {
            var data = Iso20InstallData(iso20Receiver, iso20Contract, iso20Stranger, contractSubject);
            iso20Cases.Add(new
            {
                name = "install-signed",
                what = "A sound CertificateInstallationRes: one reference over the whole "
                     + "SignedInstallationData, signed by the CPS leaf the message carries, and the "
                     + "contract key wrapped with AES-256-GCM for the car's P-521 OEM key.",
                frame        = EncodeIso20(Iso20Response(data, cpsCertificate, SignIso20(data, cpsKey))),
                receiverKeyD = Iso20ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 1, digestOk = true, signatureOk = true,
                    keyRecovered = true, recoveredKeyD = iso20ContractScalar,
                },
            });
        }

        // (2) no signature at all.
        {
            var data = Iso20InstallData(iso20Receiver, iso20Contract, iso20Stranger, contractSubject);
            iso20Cases.Add(new
            {
                name = "install-unsigned",
                what = "No header Signature. As in -2, everything the car can check without one says yes.",
                frame        = EncodeIso20(Iso20Response(data, cpsCertificate, signature: null)),
                receiverKeyD = Iso20ReceiverKeyD,
                expected = new
                {
                    signaturePresent = false, references = 0, digestOk = false, signatureOk = false,
                    keyRecovered = true, recoveredKeyD = iso20ContractScalar,
                },
            });
        }

        // (3) signed over one SignedInstallationData, sent with another.
        {
            var signed = Iso20InstallData(iso20Receiver, iso20Contract, iso20Stranger, contractSubject);
            var sent   = Iso20InstallData(iso20Receiver, iso20Contract, iso20Stranger,
                                          "CN=DE*VAN*C*000002, O=Vanaheimr (dev)");
            iso20Cases.Add(new
            {
                name = "install-digest-tampered",
                what = "The signature covers a SignedInstallationData for one contract; the message carries "
                     + "another. The ECDSA half verifies and the digest does not.",
                frame        = EncodeIso20(Iso20Response(sent, cpsCertificate, SignIso20(signed, cpsKey))),
                receiverKeyD = Iso20ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 1, digestOk = false, signatureOk = true,
                    keyRecovered = true, recoveredKeyD = iso20ContractScalar,
                },
            });
        }

        // (4) one reference, sound digest, but it names something else. A verifier reading Reference[0]
        //     positionally rather than by Id passes this.
        {
            var data = Iso20InstallData(iso20Receiver, iso20Contract, iso20Stranger, contractSubject);
            iso20Cases.Add(new
            {
                name = "install-wrong-uri",
                what = "The single reference carries the right digest under the URI \"#somethingelse\". A "
                     + "verifier that reads Reference[0] by position accepts a signature that, read "
                     + "literally, covers an element this message does not contain.",
                frame        = EncodeIso20(Iso20Response(data, cpsCertificate,
                                   SignIso20(data, cpsKey, referenceId: "somethingelse"))),
                receiverKeyD = Iso20ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 1, digestOk = false, signatureOk = true,
                    keyRecovered = true, recoveredKeyD = iso20ContractScalar,
                },
            });
        }

        // (5) a sound signature by the wrong key, with the genuine CPS chain attached.
        {
            var data = Iso20InstallData(iso20Receiver, iso20Contract, iso20Stranger, contractSubject);
            iso20Cases.Add(new
            {
                name = "install-wrong-key",
                what = "Signed by a stranger, sent with the genuine CPS chain. Digest holds, signature "
                     + "does not.",
                frame        = EncodeIso20(Iso20Response(data, cpsCertificate, SignIso20(data, iso20Stranger))),
                receiverKeyD = Iso20ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 1, digestOk = true, signatureOk = false,
                    keyRecovered = true, recoveredKeyD = iso20ContractScalar,
                },
            });
        }

        // (6) -2's wrong-receiver case, and the one place the two protocols genuinely differ in outcome
        //     rather than in bytes: GCM's tag check refuses, where CBC handed over nonsense.
        {
            var data = Iso20InstallData(iso20Other, iso20Contract, iso20Stranger, contractSubject);
            iso20Cases.Add(new
            {
                name = "install-wrong-receiver",
                what = "Wrapped for a DIFFERENT car. Where -2's CBC yields 32 bytes of nonsense, GCM's tag "
                     + "check fails and the unwrap throws — so keyRecovered is false and there is no "
                     + "scalar to record. A port that ignores the tag turns this into -2's failure mode.",
                frame        = EncodeIso20(Iso20Response(data, cpsCertificate, SignIso20(data, cpsKey))),
                receiverKeyD = Iso20ReceiverKeyD,
                expected = new
                {
                    signaturePresent = true, references = 1, digestOk = true, signatureOk = true,
                    keyRecovered = false, recoveredKeyD = (string?) null,
                },
            });
        }


        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            generator = "ISO15118ConformanceTests.Simulation.Traces.ContractProvisioningCorpusTests.RegenerateTheCorpus",
            generatorNote =
                "Contract provisioning, both protocols. Each case is a whole response frame, the private "
              + "scalar of the key the answer is wrapped for, and what an EVCC must conclude: the signature "
              + "verdict (Iso2ContractCheck / Iso20ContractCheck) and the outcome of the ECDH key unwrap "
              + "(ContractProvisioning.RecoverContractKey). recoveredKeyD is the load-bearing field — the "
              + "unwrapped scalar is never transmitted, so nothing else in this repository can state that "
              + "two implementations derive the same key. -2 is P-256 / X9.63-KDF-SHA256 / AES-128-CBC / "
              + "four references, and carries keyMatchesCertificate because CBC authenticates nothing; -20 "
              + "is P-521 / ConcatKDF-SHA512 / AES-256-GCM / one reference, and carries keyRecovered "
              + "instead because its tag check refuses a wrong key outright. The KDFs use empty "
              + "SharedInfo and are self-consistent across our three languages only: no external stack "
              + "implements either exchange to diff against. The wire messages around the payload stay "
              + "byte-exact per the usual oracles. Keys and certificate validity are fixed; the bytes are "
              + "not, since ECDSA, the ephemeral key wrap and .NET's certificate serials are all random per "
              + "run. Test material throughout, never real operator or OEM keys.",
            iso2ProvisioningKeyD = Iso2ProvisioningKeyD,
            iso2ContractKeyD     = Iso2ContractKeyD,
            iso20CpsKeyD         = Iso20CpsKeyD,
            iso20ContractKeyD    = Iso20ContractKeyD,
            iso2  = iso2Cases,
            iso20 = iso20Cases,
        }, new JsonSerializerOptions { WriteIndented = true });

        var path = SourceVectorPath();
        File.WriteAllText(path, json, new UTF8Encoding(false));
        TestContext.Out.WriteLine($"wrote {iso2Cases.Count} -2 and {iso20Cases.Count} -20 cases to {path}");

    }


    /// <summary>Where the regenerator writes it: <c>libs/EVSimulatorApp/vectors/</c>, the submodule's source
    /// tree, beside every other corpus the ports are held to.</summary>
    private static string SourceVectorPath()
    {

        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ISO15118ConformanceTests.Simulation.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");
        var vectors = Path.Combine(dir!.FullName, "..", "libs", "EVSimulatorApp", "vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, FileName);

    }

    private static string VectorPath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName);

    private static JsonElement Corpus()
    {
        Assert.That(File.Exists(VectorPath), Is.True, $"corpus missing: {VectorPath}");
        return JsonDocument.Parse(File.ReadAllText(VectorPath)).RootElement;
    }


    /// <summary>The corpus still describes what it was built to describe. Guards against a regeneration
    /// that quietly drops the cases only a corpus can carry — the negatives.</summary>
    [Test]
    public void TheCorpusCoversTheCasesItWasBuiltFor()
    {

        var root = Corpus();

        string[] Names(string set) => root.GetProperty(set).EnumerateArray()
                                          .Select(c => c.GetProperty("name").GetString()!).ToArray();

        Assert.Multiple(() =>
        {

            foreach (var required in new[] { "install-signed", "install-standalone", "install-unsigned",
                                             "install-digest-tampered", "install-three-references",
                                             "install-wrong-key", "install-wrong-receiver", "update-signed" })
                Assert.That(Names("iso2"), Does.Contain(required), $"the -2 {required} case is gone");

            foreach (var required in new[] { "install-signed", "install-unsigned", "install-digest-tampered",
                                             "install-wrong-uri", "install-wrong-key", "install-wrong-receiver" })
                Assert.That(Names("iso20"), Does.Contain(required), $"the -20 {required} case is gone");

        });

    }


    /// <summary>The C# EVCC reaches the -2 verdict the corpus records, and unwraps the scalar it records.
    /// Without this the corpus would be an oracle nothing on this side is held to — and the ports would be
    /// checked against a claim no implementation had ever satisfied.</summary>
    [Test]
    public void TheCSharpIso2VerdictMatchesTheCorpus()
    {

        var root = Corpus();

        Assert.Multiple(() =>
        {
            foreach (var c in root.GetProperty("iso2").EnumerateArray())
            {

                var name     = c.GetProperty("name").GetString()!;
                var frame    = Convert.FromHexString(c.GetProperty("frame").GetString()!);
                var expected = c.GetProperty("expected");

                var decoded = (V2G_Message) Iso2Codec.DecodeAny(frame, out _);
                var body    = decoded.Body.BodyElement!;

                var verdict = Iso2ContractCheck.Evaluate(body, decoded.Header.Signature);

                Assert.That(verdict.SignaturePresent, Is.EqualTo(expected.GetProperty("signaturePresent").GetBoolean()), $"{name}: signaturePresent");
                Assert.That(verdict.References,       Is.EqualTo(expected.GetProperty("references").GetInt32()),         $"{name}: references");
                Assert.That(verdict.DigestOk,         Is.EqualTo(expected.GetProperty("digestOk").GetBoolean()),         $"{name}: digestOk");
                Assert.That(verdict.SignatureOk,      Is.EqualTo(expected.GetProperty("signatureOk").GetBoolean()),      $"{name}: signatureOk");
                Assert.That(verdict.SignatureGrammar, Is.EqualTo(expected.GetProperty("signatureGrammar").GetString()),  $"{name}: signatureGrammar");

                var payload = Iso2ContractCheck.Unpack(body);
                Assert.That(payload.Emaid.Value, Is.EqualTo(expected.GetProperty("emaid").GetString()), $"{name}: emaid");

                using var receiver  = AgreementFrom(ECCurve.NamedCurves.nistP256, c.GetProperty("receiverKeyD").GetString()!);
                using var recovered = ContractProvisioning.RecoverContractKey(
                                          receiver, payload.DhPublicKey.Value, payload.EncryptedKey.Value);

                Assert.That(ScalarOf(recovered, 32), Is.EqualTo(expected.GetProperty("recoveredKeyD").GetString()),
                            $"{name}: the unwrapped scalar");

                using var issued          = X509CertificateLoader.LoadCertificate(payload.ContractChain.Certificate);
                using var issuedPublicKey = issued.GetECDsaPublicKey()!;
                Assert.That(ContractProvisioning.Matches(recovered, issuedPublicKey),
                            Is.EqualTo(expected.GetProperty("keyMatchesCertificate").GetBoolean()),
                            $"{name}: keyMatchesCertificate");

            }
        });

    }


    /// <summary>The same for -20, where a wrong receiver throws rather than yielding nonsense.</summary>
    [Test]
    public void TheCSharpIso20VerdictMatchesTheCorpus()
    {

        var root = Corpus();

        Assert.Multiple(() =>
        {
            foreach (var c in root.GetProperty("iso20").EnumerateArray())
            {

                var name     = c.GetProperty("name").GetString()!;
                var frame    = Convert.FromHexString(c.GetProperty("frame").GetString()!);
                var expected = c.GetProperty("expected");

                var res     = (Iso20.CertificateInstallationRes) Iso20.CommonMessagesCodec.DecodeAny(frame, out _);
                var verdict = Iso20Check.Evaluate(res, res.Header.Signature);

                Assert.That(verdict.SignaturePresent, Is.EqualTo(expected.GetProperty("signaturePresent").GetBoolean()), $"{name}: signaturePresent");
                Assert.That(verdict.References,       Is.EqualTo(expected.GetProperty("references").GetInt32()),         $"{name}: references");
                Assert.That(verdict.DigestOk,         Is.EqualTo(expected.GetProperty("digestOk").GetBoolean()),         $"{name}: digestOk");
                Assert.That(verdict.SignatureOk,      Is.EqualTo(expected.GetProperty("signatureOk").GetBoolean()),      $"{name}: signatureOk");

                using var receiver = AgreementFrom(ECCurve.NamedCurves.nistP521, c.GetProperty("receiverKeyD").GetString()!);
                var data = res.SignedInstallationData;

                ECDsa? recovered = null;
                try
                {
                    recovered = Iso20Provisioning.RecoverContractKey(
                                    receiver, data.DHPublicKey, data.SECP521_EncryptedPrivateKey!);
                }
                catch (CryptographicException)
                {
                    // GCM refused the tag — the whole point of the wrong-receiver case.
                }

                using (recovered)
                {

                    Assert.That(recovered is not null, Is.EqualTo(expected.GetProperty("keyRecovered").GetBoolean()),
                                $"{name}: keyRecovered");

                    if (recovered is not null)
                        Assert.That(ScalarOf(recovered, 66), Is.EqualTo(expected.GetProperty("recoveredKeyD").GetString()),
                                    $"{name}: the unwrapped scalar");

                }

            }
        });

    }

}
