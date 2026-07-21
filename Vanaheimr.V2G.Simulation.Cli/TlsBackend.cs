namespace Vanaheimr.V2G.Simulation.Cli
{
    /// <summary>Which TLS stack to use: .NET's <c>SslStream</c> (Schannel/OpenSSL) or BouncyCastle.</summary>
    public enum TlsBackend
    {
        /// <summary>No TLS — plain TCP.</summary>
        None,

        /// <summary>.NET <c>SslStream</c>: fast, platform-native; a self-signed P-256 dev server cert.</summary>
        Dotnet,

        /// <summary>BouncyCastle: the -20-faithful profile (TLS 1.3, secp521r1, mutual TLS) — needs <c>--pki-dir</c>.</summary>
        BouncyCastle,
    }
}
