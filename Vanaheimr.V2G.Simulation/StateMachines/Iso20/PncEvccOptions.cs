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

    /// <summary>
    /// OEM-provisioning credentials that make an <see cref="Evcc20Base"/> request <b>contract provisioning</b>:
    /// when set (and the SECC announces CertificateInstallationService), the EVCC sends a signed
    /// <c>CertificateInstallationReq</c> before its AuthorizationReq — the OEM chain signed over its EXI
    /// fragment in the Josev interop form — and processes the response: it verifies the CPS signature over
    /// <c>SignedInstallationData</c> and ECDH-unwraps the issued contract private key
    /// (<see cref="ContractProvisioning.RecoverContractKey"/>). The OEM key must be <b>P-521</b> to take part
    /// in the -20 secp521r1 key agreement.
    /// </summary>
    /// <param name="OemCertificate">The OEM provisioning leaf certificate (DER, P-521).</param>
    /// <param name="OemSubCertificates">The OEM sub-CA certificates (DER), leaf-issuer first.</param>
    /// <param name="OemSignKey">The OEM leaf's private key, for signing the request.</param>
    /// <param name="OemKeyAgreement">The same private key as an ECDH handle, for unwrapping the contract key.</param>
    public sealed record CertInstallEvccOptions(
        byte[] OemCertificate,
        IReadOnlyList<byte[]> OemSubCertificates,
        ECDsa OemSignKey,
        ECDiffieHellman OemKeyAgreement);
}
