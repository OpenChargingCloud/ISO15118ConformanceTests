using System.Security.Cryptography;

using Vanaheimr.V2G.Iso15118_20.AC.Generated;

namespace Vanaheimr.V2G.Iso15118_20.AC;

/// <summary>
/// XMLDSig signing/verification for ISO 15118-20 AC — identical suite and shape to
/// <see cref="Vanaheimr.V2G.Iso15118_20.CommonMessages.V2GSignature"/> (SHA-512, ECDSA-P521,
/// 132-byte raw <c>r‖s</c>), just against AC's own duplicated <c>SignedInfoType</c>/codec.
/// </summary>
public static class V2GSignature
{
    public const string CanonicalizationExi = "http://www.w3.org/TR/canonical-exi/";
    public const string EcdsaSha512 = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512";
    public const string Sha512 = "http://www.w3.org/2001/04/xmlenc#sha512";

    public static byte[] Digest(ReadOnlySpan<byte> fragmentBytes) => SHA512.HashData(fragmentBytes);

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

    public static SignatureType BuildSignature(SignedInfoType signedInfo, byte[] signatureValue) =>
        new(Id: null,
            SignedInfo: signedInfo,
            SignatureValue: new SignatureValueType(Id: null, Value: signatureValue),
            KeyInfo: null,
            Object: null);

    public static byte[] SignedInfoFragment(SignedInfoType signedInfo)
    {
        var buf = new byte[512];
        while (true)
        {
            if (AcCodec.EncodeFragment_SignedInfo(signedInfo, buf, out int n))
                return buf.AsSpan(0, n).ToArray();
            buf = new byte[buf.Length * 2];
        }
    }

    public static byte[] Sign(SignedInfoType signedInfo, ECDsa privateKey) =>
        privateKey.SignData(SignedInfoFragment(signedInfo), HashAlgorithmName.SHA512,
                            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static bool Verify(SignedInfoType signedInfo, byte[] signatureValue, ECDsa publicKey) =>
        publicKey.VerifyData(SignedInfoFragment(signedInfo), signatureValue, HashAlgorithmName.SHA512,
                             DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static bool VerifyReference(ReferenceType reference, ReadOnlySpan<byte> signedElementFragment) =>
        CryptographicOperations.FixedTimeEquals(reference.DigestValue, Digest(signedElementFragment));
}
