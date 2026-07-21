using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Vanaheimr.V2G.Simulation.Transport.BouncyCastle
{
    /// <summary>
    /// EVCC-side BouncyCastle TLS client: TLS 1.3 with the -20 cipher suites, validates the SECC server
    /// certificate, and presents the Vehicle client certificate when the SECC requests one (mutual TLS).
    /// </summary>
    internal sealed class BcV2GTlsClient : DefaultTlsClient
    {
        private readonly BcTlsCrypto _crypto;
        private readonly BcTlsOptions _options;

        public BcV2GTlsClient(BcTlsCrypto crypto, BcTlsOptions options) : base(crypto)
        {
            _crypto  = crypto;
            _options = options;
        }

        public override ProtocolVersion[] GetProtocolVersions() => BcV2GTls.Tls13Only;

        protected override int[] GetSupportedCipherSuites() => BcV2GTls.CipherSuites;

        public override TlsAuthentication GetAuthentication()
            => new V2GAuthentication(_crypto, _options, () => m_context);

        private sealed class V2GAuthentication : TlsAuthentication
        {
            private readonly BcTlsCrypto _crypto;
            private readonly BcTlsOptions _options;
            private readonly Func<TlsContext> _context;

            public V2GAuthentication(BcTlsCrypto crypto, BcTlsOptions options, Func<TlsContext> context)
            {
                _crypto  = crypto;
                _options = options;
                _context = context;
            }

            public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
                => BcV2GTls.ValidatePeer(serverCertificate?.Certificate, _options.ValidatePeerLeaf, AlertDescription.bad_certificate);

            public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
                => BcV2GTls.BuildSigner(_crypto, _options.OwnCredentials, _context());
        }
    }
}
