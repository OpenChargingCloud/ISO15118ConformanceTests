using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Vanaheimr.V2G.Simulation.Transport.BouncyCastle
{
    /// <summary>
    /// Shared building blocks for the BouncyCastle V2G TLS client/server: the ISO 15118-20 signature
    /// schemes, credentialed-signer construction, and peer-certificate validation.
    /// </summary>
    internal static class BcV2GTls
    {
        /// <summary>ISO 15118-20's two TLS signature schemes: ECDSA-secp521r1-SHA512 and Ed448.</summary>
        internal static readonly IList<SignatureAndHashAlgorithm> AcceptedSignatureAlgorithms =
            new List<SignatureAndHashAlgorithm>
            {
                SignatureScheme.GetSignatureAndHashAlgorithm(SignatureScheme.ecdsa_secp521r1_sha512),
                SignatureScheme.GetSignatureAndHashAlgorithm(SignatureScheme.ed448),
            };

        /// <summary>The -20 TLS 1.3 cipher suites.</summary>
        internal static readonly int[] CipherSuites =
        {
            CipherSuite.TLS_AES_256_GCM_SHA384,
            CipherSuite.TLS_CHACHA20_POLY1305_SHA256,
        };

        internal static readonly ProtocolVersion[] Tls13Only = { ProtocolVersion.TLSv13 };

        internal static TlsCredentials BuildSigner(BcTlsCrypto crypto, BcTlsCredentials creds, TlsContext context)
        {
            // TLS 1.3 uses the CertificateEntry form with an (empty) certificate_request_context — the
            // legacy Certificate(TlsCertificate[]) ctor is a 1.2-only structure and trips an internal_error.
            var entries = creds.CertificateChain
                               .Select(der => new CertificateEntry(new BcTlsCertificate(crypto, der),
                                                                    (IDictionary<int, byte[]>?) null))
                               .ToArray();

            return new BcDefaultTlsCredentialedSigner(
                       new TlsCryptoParameters(context),
                       crypto,
                       creds.PrivateKey,
                       new Certificate(TlsUtilities.EmptyBytes, entries),
                       SignatureScheme.GetSignatureAndHashAlgorithm(creds.SignatureScheme));
        }

        internal static void ValidatePeer(Certificate? peer, Func<byte[], bool>? validate, short missingAlert)
        {
            if (peer is null || peer.IsEmpty)
                throw new TlsFatalAlert(missingAlert);

            if (validate is null)
                return;

            if (!validate(peer.GetCertificateAt(0).GetEncoded()))
                throw new TlsFatalAlert(AlertDescription.bad_certificate);
        }
    }
}
