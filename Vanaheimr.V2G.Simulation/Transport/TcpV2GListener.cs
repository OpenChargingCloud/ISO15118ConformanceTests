using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace Vanaheimr.V2G.Simulation.Transport
{
    /// <summary>
    /// SECC-side TCP listener: binds a fixed endpoint (no SDP discovery — out of scope for this slice)
    /// and hands back a <see cref="Stream"/> per accepted connection — a plain <see cref="NetworkStream"/>,
    /// or an authenticated <see cref="SslStream"/> if <paramref name="tls"/> is given. Either way,
    /// <see cref="Framing.V2GTPStream"/> only ever sees a <see cref="Stream"/>, so TLS is transparent to
    /// framing by construction.
    /// </summary>
    public sealed class TcpV2GListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TlsOptions? _tls;

        /// <param name="endpoint">Pass port 0 to let the OS assign a free port — read it back via <see cref="LocalEndpoint"/>.</param>
        /// <param name="tls">When given, <see cref="AcceptAsync"/> authenticates as a TLS server before returning.</param>
        public TcpV2GListener(IPEndPoint endpoint, TlsOptions? tls = null)
        {
            if (tls is { ServerCertificate: null })
                throw new ArgumentException("TlsOptions.ServerCertificate is required for the SECC/listener side.", nameof(tls));

            _listener = new TcpListener(endpoint);
            _tls = tls;
            _listener.Start();
        }

        public IPEndPoint LocalEndpoint => (IPEndPoint)_listener.LocalEndpoint;

        /// <summary>Accepts the next incoming connection and returns its stream (plain, or TLS-authenticated if configured).</summary>
        public async Task<Stream> AcceptAsync(CancellationToken ct = default)
        {
            var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            Stream stream = client.GetStream(); // owns the underlying socket; disposing the stream closes it

            if (_tls is null) return stream;

            var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _tls.ServerCertificate,
                EnabledSslProtocols = _tls.EnabledSslProtocols,
                ClientCertificateRequired = false, // mutual TLS out of scope
            }, ct).ConfigureAwait(false);
            return ssl;
        }

        public void Dispose() => _listener.Stop();
    }
}
