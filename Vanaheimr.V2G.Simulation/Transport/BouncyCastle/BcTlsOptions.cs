namespace Vanaheimr.V2G.Simulation.Transport.BouncyCastle
{
    /// <summary>
    /// Configuration for the BouncyCastle TLS backend (<see cref="BcTlsTransport"/>) — the managed,
    /// platform-independent alternative to .NET's <c>SslStream</c>, used for the ISO 15118-20-faithful
    /// TLS profile (TLS 1.3, secp521r1 / Ed448) that Windows Schannel cannot do. See <c>docs/pki-model.md</c>.
    /// </summary>
    public sealed record BcTlsOptions
    {
        /// <summary>The certificate + key this side presents (SECC server cert, or EVCC Vehicle client cert).</summary>
        public required BcTlsCredentials OwnCredentials { get; init; }

        /// <summary>Validate the peer's leaf certificate (DER). Return false to abort the handshake. Null = accept any.</summary>
        public Func<byte[], bool>? ValidatePeerLeaf { get; init; }

        /// <summary>SECC side only: require a client certificate from the EVCC (mutual TLS). Ignored on the client.</summary>
        public bool RequireClientCertificate { get; init; }

        /// <summary><b>EXPERIMENTAL</b> (see <c>Vanaheimr.V2G.Experiments.Pqc</c>): override the TLS
        /// named groups this side offers/accepts for the key exchange — e.g.
        /// <c>Org.BouncyCastle.Tls.NamedGroup.MLKEM1024</c> for a post-quantum ML-KEM (FIPS 203) key
        /// exchange. Setting this deviates from ISO 15118-20's TLS profile (which pins classical
        /// groups) — loopback experiments only, never wire-conformant. Null (default): BouncyCastle's
        /// standard list, i.e. the production behaviour, unchanged.</summary>
        public int[]? ExperimentalNamedGroups { get; init; }
    }
}
