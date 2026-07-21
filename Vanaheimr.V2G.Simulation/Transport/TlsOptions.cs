using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Vanaheimr.V2G.Simulation.Transport
{
    /// <summary>
    /// TLS knobs for <see cref="TcpV2GListener"/>/<see cref="TcpV2GClient"/>. Supports both server-side TLS
    /// (ISO 15118-2) and <b>mutual TLS</b> (ISO 15118-20): the SECC presents its server certificate and,
    /// when <see cref="RequireClientCertificate"/> is set, requires and validates the EVCC's TLS client
    /// certificate (the CharIN "Vehicle" certificate) — see <c>docs/pki-model.md</c>.
    /// <para>
    /// <b>Known gap:</b> ISO 15118-20's TLS profile pins specific cipher suites/curves (TLS 1.3,
    /// <c>TLS_AES_256_GCM_SHA384</c>/<c>TLS_CHACHA20_POLY1305_SHA256</c>, secp521r1); this uses
    /// <see cref="SslProtocols.Tls13"/> with whatever cipher suites .NET/Schannel negotiate by default
    /// rather than enforcing the spec's exact list.
    /// </para>
    /// </summary>
    public sealed record TlsOptions
    {
        /// <summary>SECC side: the certificate to present. Required when TLS is enabled on the listener.</summary>
        public X509Certificate2? ServerCertificate { get; init; }

        /// <summary>SECC side: the intermediate CA certificates to send alongside <see cref="ServerCertificate"/> so
        /// the EVCC can build the chain to its trust anchor (e.g. a Josev EVCC validating our SECC leaf against the
        /// V2G root needs the CPO Sub-CAs). Without these, <c>SslStream</c> sends only the leaf. Null = leaf only.</summary>
        public X509Certificate2Collection? ServerCertificateChain { get; init; }

        /// <summary>EVCC side: how to validate the SECC's certificate. Defaults to the platform's normal chain validation if not set.</summary>
        public RemoteCertificateValidationCallback? ServerCertificateValidation { get; init; }

        /// <summary>EVCC side: the TLS client certificate to present for mutual TLS (the Vehicle certificate). Null = no client cert.</summary>
        public X509Certificate2? ClientCertificate { get; init; }

        /// <summary>EVCC side: the intermediate CA certificates to send alongside <see cref="ClientCertificate"/> so the
        /// SECC can build the chain to its trust anchor. Without these, <c>SslStream</c> sends only the leaf and a peer
        /// that holds only the root (e.g. Josev, which loads just the OEM root) can't verify the client. Null = leaf only.</summary>
        public X509Certificate2Collection? ClientCertificateChain { get; init; }

        /// <summary>SECC side: require a TLS client certificate from the EVCC (mutual TLS). Off for plain server-side TLS.</summary>
        public bool RequireClientCertificate { get; init; }

        /// <summary>SECC side: how to validate the EVCC's client certificate. Defaults to the platform's normal chain validation if not set.</summary>
        public RemoteCertificateValidationCallback? ClientCertificateValidation { get; init; }

        public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.Tls13;
    }
}
