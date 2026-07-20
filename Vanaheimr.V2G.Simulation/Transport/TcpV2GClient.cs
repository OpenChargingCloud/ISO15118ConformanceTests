using System.Net.Security;
using System.Net.Sockets;

namespace Vanaheimr.V2G.Simulation.Transport
{
    /// <summary>
    /// EVCC-side TCP connect to a fixed host:port (no SDP discovery — out of scope for this slice).
    /// Returns a <see cref="Stream"/> — plain, or TLS-authenticated against <paramref name="host"/> if
    /// <paramref name="tls"/> is given.
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
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = tls.EnabledSslProtocols,
            }, ct).ConfigureAwait(false);
            return ssl;
        }
    }
}
