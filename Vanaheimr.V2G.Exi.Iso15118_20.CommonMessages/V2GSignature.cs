using System.Security.Cryptography;

using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Iso15118_20.CommonMessages;

/// <summary>
/// XMLDSig signing/verification for ISO 15118-20 CommonMessages. Same two-level EXI-fragment digest
/// scheme as -2's <c>V2GSignature</c> (see there for the general shape), but with -20's stronger
/// suite: SHA-512 digests and ECDSA over NIST P-521 (secp521r1), raw <c>r‖s</c> <c>SignatureValue</c>
/// (132 bytes: 66 + 66, IEEE P1363) rather than ASN.1/DER.
/// <para>Ed448 is in the -20 signature-suite options but is out of scope here — .NET has no built-in
/// Ed448 support (see README "still does NOT do").</para>
/// </summary>
public static class V2GSignature
{
    /// <summary>EXI canonicalization (the only C14N ISO 15118-20 uses).</summary>
    public const string CanonicalizationExi = "http://www.w3.org/TR/canonical-exi/";

    /// <summary>ECDSA-SHA512 signature method.</summary>
    public const string EcdsaSha512 = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512";

    /// <summary>SHA-512 digest method.</summary>
    public const string Sha512 = "http://www.w3.org/2001/04/xmlenc#sha512";

    /// <summary>SHA-512 of an element's EXI fragment — the value that goes into its
    /// <see cref="ReferenceType.DigestValue"/>.</summary>
    public static byte[] Digest(ReadOnlySpan<byte> fragmentBytes) => SHA512.HashData(fragmentBytes);

    /// <summary>Builds a single-reference <see cref="SignedInfoType"/> over one already-computed
    /// element digest, with the fixed EXI-C14N / ECDSA-SHA512 / SHA-512 algorithm URIs. The reference
    /// URI is <c>"#" + <paramref name="referenceId"/></c> — the <c>Id</c> attribute of the signed
    /// element.</summary>
    public static SignedInfoType BuildSignedInfo(string referenceId, byte[] digest) =>
        new(
            Id: null,
            CanonicalizationMethod: new CanonicalizationMethodType(Algorithm: CanonicalizationExi, ANY: null),
            SignatureMethod: new SignatureMethodType(Algorithm: EcdsaSha512, HMACOutputLength: null, ANY: null),
            Reference: new[]
            {
                new ReferenceType(
                    Id: null, Type: null, URI: "#" + referenceId, Transforms: null,
                    DigestMethod: new DigestMethodType(Algorithm: Sha512, ANY: null),
                    DigestValue: digest),
            });

    /// <summary>Assembles the header <see cref="SignatureType"/> from a signed <c>SignedInfo</c> and
    /// its raw <c>r‖s</c> <c>SignatureValue</c> (KeyInfo/Object absent, as -20 uses).</summary>
    public static SignatureType BuildSignature(SignedInfoType signedInfo, byte[] signatureValue) =>
        new(Id: null,
            SignedInfo: signedInfo,
            SignatureValue: new SignatureValueType(Id: null, Value: signatureValue),
            KeyInfo: null,
            Object: null);

    /// <summary>Encodes a <see cref="SignedInfoType"/> as its EXI fragment — the exact octets that are
    /// SHA-512'd and signed (or verified).</summary>
    public static byte[] SignedInfoFragment(SignedInfoType signedInfo)
    {
        var buf = new byte[512];
        while (true)
        {
            if (CommonMessagesCodec.EncodeFragment_SignedInfo(signedInfo, buf, out int n))
                return buf.AsSpan(0, n).ToArray();
            buf = new byte[buf.Length * 2];
        }
    }

    /// <summary>Signs a <see cref="SignedInfoType"/>: SHA-512 over its EXI fragment, ECDSA-P521,
    /// returning the raw <c>r‖s</c> (132-byte) <c>SignatureValue</c>.</summary>
    public static byte[] Sign(SignedInfoType signedInfo, ECDsa privateKey) =>
        privateKey.SignData(SignedInfoFragment(signedInfo), HashAlgorithmName.SHA512,
                            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <summary>Verifies a raw <c>r‖s</c> <c>SignatureValue</c> against a <see cref="SignedInfoType"/>
    /// and public key. Only checks the ECDSA signature over the SignedInfo fragment; the caller is
    /// responsible for confirming each reference digest matches the signed element (see
    /// <see cref="VerifyReference"/>).</summary>
    public static bool Verify(SignedInfoType signedInfo, byte[] signatureValue, ECDsa publicKey) =>
        publicKey.VerifyData(SignedInfoFragment(signedInfo), signatureValue, HashAlgorithmName.SHA512,
                             DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <summary>Confirms that a reference's <see cref="ReferenceType.DigestValue"/> equals the SHA-512
    /// of the given signed-element fragment — the second half of verification.</summary>
    public static bool VerifyReference(ReferenceType reference, ReadOnlySpan<byte> signedElementFragment) =>
        CryptographicOperations.FixedTimeEquals(reference.DigestValue, Digest(signedElementFragment));
}
