using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

using Vanaheimr.V2G.Simulation.Transport.BouncyCastle;

namespace Vanaheimr.V2G.Simulation.Transport
{
    /// <summary>
    /// EVCC-side TCP connect to a fixed host:port (no SDP discovery — out of scope for this slice).
    /// Returns a <see cref="Stream"/> — plain, or TLS-authenticated against <paramref name="host"/> if
    /// <paramref name="tls"/> is given. When <see cref="TlsOptions.ClientCertificate"/> is set, the EVCC
    /// presents it for mutual TLS (the Vehicle certificate).
    /// </summary>
    public static class TcpV2GClient
    {
        public static async Task<Stream> ConnectAsync(string host, int port, TlsOptions? tls = null, CancellationToken ct = default)
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            Stream stream = client.GetStream(); // owns the underlying socket; disposing the stream closes it

            if (tls is null) return stream;

            var ssl = new SslStream(stream, leaveInnerStreamOpen: false, tls.ServerCertificateValidation);
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = tls.EnabledSslProtocols,
            };
            if (tls.ClientCertificate is { } clientCert)
                options.ClientCertificates = new X509CertificateCollection { clientCert };

            await ssl.AuthenticateAsClientAsync(options, ct).ConfigureAwait(false);
            return ssl;
        }

        /// <summary>
        /// EVCC-side connect using the <b>BouncyCastle</b> TLS backend (the -20-faithful P-521 / Ed448
        /// profile Schannel can't do). Overload selected by passing <see cref="BcTlsOptions"/>.
        /// </summary>
        public static async Task<Stream> ConnectAsync(string host, int port, BcTlsOptions bcTls, CancellationToken ct = default)
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            return await BcTlsTransport.AuthenticateClientAsync(client.GetStream(), bcTls, ct).ConfigureAwait(false);
        }
    }
}
