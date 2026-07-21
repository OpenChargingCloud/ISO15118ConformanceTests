using Org.BouncyCastle.Security;

using cloud.charging.open.protocols.ISO15118.PKI;

using Vanaheimr.V2G.Simulation.Transport.BouncyCastle;

namespace Vanaheimr.V2G.Simulation.Tests.TestData
{
    /// <summary>
    /// Builds a mutual-TLS <see cref="BcTlsOptions"/> pair (SECC + EVCC) from a fresh strict-20 PKI
    /// hierarchy, feeding the BouncyCastle certificate material straight in — no PKCS#12/X509Certificate2
    /// bridge, since BouncyCastle TLS consumes the builder's native cert + key objects directly. Peer
    /// validation pins the exact expected leaf certificate (chain-to-root validation is covered by the
    /// .NET-path tests; here the point is that the -20 P-521 / Ed448 handshake works over the wire).
    /// </summary>
    public static class V2GBcTlsFixture
    {
        public static (BcTlsOptions Secc, BcTlsOptions Evcc) MutualTls(V2GAlgorithm algorithm, int signatureScheme)
        {
            var h = V2GHierarchy.Build(
                        algorithm,
                        new SecureRandom(),
                        V2GProfileOptions: new V2GProfileOptions(V2GProfileFlavor.Strict15118_20, algorithm, V2GPolicySet.None));

            static byte[][] Chain(params V2GIssued[] certs) => [.. certs.Select(c => c.Certificate.GetEncoded())];
            static Func<byte[], bool> Pin(byte[] expected) => actual => expected.AsSpan().SequenceEqual(actual);

            var secc = new BcTlsOptions
            {
                OwnCredentials           = new BcTlsCredentials(Chain(h.SeccLeaf, h.CpoSubCa2, h.CpoSubCa1),
                                                                h.SeccLeaf.KeyPair.Private, signatureScheme),
                RequireClientCertificate = true,
                ValidatePeerLeaf         = Pin(h.VehicleLeaf.Certificate.GetEncoded()),
            };

            var evcc = new BcTlsOptions
            {
                OwnCredentials   = new BcTlsCredentials(Chain(h.VehicleLeaf, h.VehicleSubCa2, h.VehicleSubCa1),
                                                        h.VehicleLeaf.KeyPair.Private, signatureScheme),
                ValidatePeerLeaf = Pin(h.SeccLeaf.Certificate.GetEncoded()),
            };

            return (secc, evcc);
        }
    }
}
