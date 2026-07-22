using System.Security.Cryptography;

namespace Vanaheimr.V2G.Simulation.StateMachines.Iso20
{
    /// <summary>
    /// Contract credentials that switch an <see cref="Evcc20Base"/> from EIM to <b>Plug &amp; Charge</b>
    /// authorization: when set (and the SECC offers PnC with a GenChallenge), the EVCC sends a <b>signed</b>
    /// <c>AuthorizationReq</c> — challenge echo + contract chain + an XMLDSig signature over the
    /// <c>PnC_AReqAuthorizationMode</c> fragment, in Josev's exact interop form (see
    /// <see cref="XmlDsigInteropSign"/>).
    /// </summary>
    /// <param name="ContractCertificate">The contract leaf certificate (DER).</param>
    /// <param name="SubCertificates">The MO sub-CA certificates (DER), leaf-issuer first.</param>
    /// <param name="ContractKey">The contract leaf's private key (P-256 for the Josev interop form).</param>
    public sealed record PncEvccOptions(
        byte[] ContractCertificate,
        IReadOnlyList<byte[]> SubCertificates,
        ECDsa ContractKey);
}
