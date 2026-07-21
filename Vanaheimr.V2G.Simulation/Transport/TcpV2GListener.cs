using System.Net;
using System.Net.Security;
using System.Net.Sockets;

using Vanaheimr.V2G.Simulation.Transport.BouncyCastle;

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
        private readonly BcTlsOptions? _bcTls;

        /// <param name="endpoint">Pass port 0 to let the OS assign a free port — read it back via <see cref="LocalEndpoint"/>.</param>
        /// <param name="tls">When given, <see cref="AcceptAsync"/> authenticates as a TLS server before returning.</param>
        public TcpV2GListener(IPEndPoint endpoint, TlsOptions? tls = null)
        {
            if (tls is { ServerCertificate: null })
                throw new ArgumentException("TlsOptions.ServerCertificate is required for the SECC/listener side.", nameof(tls));

            _listener = new TcpListener(endpoint);
            EnableDualStack(_listener, endpoint);
            _tls = tls;
            _listener.Start();
        }

        /// <summary>SECC listener using the <b>BouncyCastle</b> TLS backend (the -20-faithful P-521 / Ed448 profile).</summary>
        public TcpV2GListener(IPEndPoint endpoint, BcTlsOptions bcTls)
        {
            _listener = new TcpListener(endpoint);
            EnableDualStack(_listener, endpoint);
            _bcTls = bcTls;
            _listener.Start();
        }

        // Binding [::] as dual-stack lets the SECC accept both IPv4 (loopback tests) and IPv6 connections —
        // the latter is what a real ISO 15118 EVCC (e.g. Josev over an fe80::…%eth0 link-local) uses.
        private static void EnableDualStack(TcpListener listener, IPEndPoint endpoint)
        {
            if (endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                endpoint.Address.Equals(IPAddress.IPv6Any))
                listener.Server.DualMode = true;
        }

        public IPEndPoint LocalEndpoint => (IPEndPoint)_listener.LocalEndpoint;

        /// <summary>Accepts the next incoming connection and returns its stream (plain, or TLS-authenticated if configured).</summary>
        public async Task<Stream> AcceptAsync(CancellationToken ct = default)
        {
            var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            Stream stream = client.GetStream(); // owns the underlying socket; disposing the stream closes it

            if (_bcTls is not null)
                return await BcTlsTransport.AuthenticateServerAsync(stream, _bcTls, ct).ConfigureAwait(false);

            if (_tls is null) return stream;

            var ssl = new SslStream(stream, leaveInnerStreamOpen: false,
                userCertificateValidationCallback: _tls.ClientCertificateValidation);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _tls.ServerCertificate,
                EnabledSslProtocols = _tls.EnabledSslProtocols,
                ClientCertificateRequired = _tls.RequireClientCertificate, // mutual TLS for ISO 15118-20
            }, ct).ConfigureAwait(false);
            return ssl;
        }

        public void Dispose() => _listener.Stop();
    }
}
