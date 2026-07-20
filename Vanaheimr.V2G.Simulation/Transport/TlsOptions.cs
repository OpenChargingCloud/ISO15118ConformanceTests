using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Vanaheimr.V2G.Simulation.Transport
{
    /// <summary>
    /// TLS knobs for <see cref="TcpV2GListener"/>/<see cref="TcpV2GClient"/>. Server-side TLS only —
    /// mutual TLS is a documented gap (per <c>docs/prompts/phase5.md</c>), not a DoD item for this slice.
    /// <para>
    /// <b>Known gap:</b> ISO 15118-20's TLS profile pins specific cipher suites/curves; this uses
    /// <see cref="SslProtocols.Tls13"/> with whatever cipher suites .NET/Schannel negotiate by default
    /// rather than enforcing the spec's exact list.
    /// </para>
    /// </summary>
    public sealed record TlsOptions
    {
        /// <summary>SECC side: the certificate to present. Required when TLS is enabled on the listener.</summary>
        public X509Certificate2? ServerCertificate { get; init; }

        /// <summary>EVCC side: how to validate the SECC's certificate. Defaults to the platform's normal chain validation if not set.</summary>
        public RemoteCertificateValidationCallback? ServerCertificateValidation { get; init; }

        public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.Tls13;
    }
}
