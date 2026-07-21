using Org.BouncyCastle.Crypto;

namespace Vanaheimr.V2G.Simulation.Transport.BouncyCastle
{
    /// <summary>
    /// The certificate material one side presents in the BouncyCastle TLS handshake: the certificate
    /// chain (leaf first, DER-encoded), the leaf's private key, and the TLS 1.3 signature scheme it signs
    /// with (e.g. <c>SignatureScheme.ecdsa_secp521r1_sha512</c> or <c>SignatureScheme.ed448</c>).
    /// </summary>
    public sealed record BcTlsCredentials(
        byte[][]                CertificateChain,
        AsymmetricKeyParameter  PrivateKey,
        int                     SignatureScheme);
}
