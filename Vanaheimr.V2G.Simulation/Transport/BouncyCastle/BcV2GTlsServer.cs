using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Vanaheimr.V2G.Simulation.Transport.BouncyCastle
{
    /// <summary>
    /// SECC-side BouncyCastle TLS server: TLS 1.3 with the -20 cipher suites, presents its SECC certificate,
    /// and (for mutual TLS) requires + validates the EVCC's Vehicle client certificate.
    /// </summary>
    internal sealed class BcV2GTlsServer : DefaultTlsServer
    {
        private readonly BcTlsCrypto _crypto;
        private readonly BcTlsOptions _options;

        public BcV2GTlsServer(BcTlsCrypto crypto, BcTlsOptions options) : base(crypto)
        {
            _crypto  = crypto;
            _options = options;
        }

        public override ProtocolVersion[] GetProtocolVersions() => BcV2GTls.Tls13Only;

        protected override int[] GetSupportedCipherSuites() => BcV2GTls.CipherSuites;

        public override TlsCredentials GetCredentials()
            => BcV2GTls.BuildSigner(_crypto, _options.OwnCredentials, m_context);

        public override CertificateRequest GetCertificateRequest()
            => _options.RequireClientCertificate
                   ? new CertificateRequest(TlsUtilities.EmptyBytes, BcV2GTls.AcceptedSignatureAlgorithms, null, null)
                   : null!;

        public override void NotifyClientCertificate(Certificate clientCertificate)
            => BcV2GTls.ValidatePeer(clientCertificate, _options.ValidatePeerLeaf, AlertDescription.certificate_required);
    }
}
