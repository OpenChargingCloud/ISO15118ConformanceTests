using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

using Vanaheimr.V2G.Iso15118_20.CommonMessages;
using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Experiments.Pqc
{
    /// <summary>One measured wire-size variant of the exemplar message.</summary>
    public sealed record PqcSizeRow(string Variant, int SignatureBytes, int ExiBytes, int JsonBytes)
    {
        /// <summary>EXI's absolute saving over compact JSON for this variant.</summary>
        public int ExiSavingBytes => JsonBytes - ExiBytes;

        /// <summary>EXI's relative saving over compact JSON for this variant.</summary>
        public double ExiSavingPercent => JsonBytes == 0 ? 0 : 100.0 * ExiSavingBytes / JsonBytes;

        /// <summary>How much of the EXI message is signature.</summary>
        public double SignatureShareOfExiPercent => ExiBytes == 0 ? 0 : 100.0 * SignatureBytes / ExiBytes;
    }

    /// <summary>
    /// <b>EXPERIMENT.</b> Measures what post-quantum signatures do to ISO 15118 message sizes — and to
    /// EXI's raison d'être. Exemplar: the -20 Plug &amp; Charge <c>AuthorizationReq</c> (challenge echo +
    /// a real 3-certificate contract chain), encoded three ways (unsigned, ECDSA-P521-signed,
    /// ML-DSA-87-signed) and serialized both as EXI (our generated codec) and as compact JSON
    /// (System.Text.Json over the same records, byte arrays as base64) — the "should we have just used
    /// JSON?" strawman. The point the numbers make: EXI's saving is a (roughly constant) few hundred
    /// bytes of structural overhead, while a PQC signature adds ~4.6 KB of incompressible randomness
    /// to BOTH encodings — the saving stops mattering. See <c>docs/experiments/pqc.md</c>.
    /// </summary>
    public static class PqcSizeReport
    {
        private static readonly JsonSerializerOptions CompactJson = new()
        {
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static IReadOnlyList<PqcSizeRow> Measure()
        {
            // A realistic PnC contract chain: 3 ECDSA-P256 certificates (leaf + 2 sub-CAs), the same
            // curve Josev's shipped dev PKI uses for its -2/-20 contract material.
            using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var chain = Enumerable.Range(1, 3)
                .Select(i => new CertificateRequest($"CN=PQC-SIZE-DEMO-{i}", leafKey, HashAlgorithmName.SHA256)
                    .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1)))
                .Select(c => c.RawData)
                .ToArray();

            var pncMode = new PnC_AReqAuthorizationModeType("id1", RandomNumberGenerator.GetBytes(16),
                new ContractCertificateChainType(chain[0], new SubCertificatesType(chain[1..])));

            var fragment = new byte[8192];
            if (!CommonMessagesCodec.EncodeFragment_PnC_AReqAuthorizationMode(pncMode, fragment, out int fragmentLength))
                throw new InvalidOperationException("fragment encode failed");
            var digest = V2GSignature.Digest(fragment.AsSpan(0, fragmentLength));

            // Classical: the -20 mandatory suite (ECDSA-P521/SHA-512, 132-byte r||s).
            using var p521 = ECDsa.Create(ECCurve.NamedCurves.nistP521);
            var classicalInfo = V2GSignature.BuildSignedInfo("id1", digest);
            var classicalSig  = V2GSignature.BuildSignature(classicalInfo, V2GSignature.Sign(classicalInfo, p521));

            // Post-quantum: ML-DSA-87 behind the experimental URI (4 627-byte signature).
            var (mlDsaPriv, _) = MLDsaV2GSignature.GenerateKeyPair();
            var mlDsaInfo = V2GSignature.BuildSignedInfo("id1", digest, MLDsaV2GSignature.MlDsa87Experimental);
            var mlDsaSig  = V2GSignature.BuildSignature(mlDsaInfo, MLDsaV2GSignature.Sign(mlDsaInfo, mlDsaPriv));

            var header = new MessageHeaderType(RandomNumberGenerator.GetBytes(8),
                                               (ulong) DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                               Signature: null);

            AuthorizationReq Req(SignatureType? signature) =>
                new(header with { Signature = signature }, Authorization.PnC, null, pncMode);

            return new[]
            {
                Row("unsigned",            0,                                  Req(null)),
                Row("ECDSA-P521/SHA-512",  classicalSig.SignatureValue.Value.Length, Req(classicalSig)),
                Row("ML-DSA-87 (PQC)",     mlDsaSig.SignatureValue.Value.Length,     Req(mlDsaSig)),
            };
        }

        private static PqcSizeRow Row(string variant, int signatureBytes, AuthorizationReq message)
        {
            var buf = new byte[65536];
            if (!message.TryEncode(buf, out int exiLength))
                throw new InvalidOperationException($"EXI encode failed for '{variant}'");
            var json = JsonSerializer.Serialize(message, CompactJson);
            return new PqcSizeRow(variant, signatureBytes, exiLength, json.Length);
        }

        public static string ToMarkdown(IReadOnlyList<PqcSizeRow> rows)
        {
            var lines = new List<string>
            {
                "| Signature | Sig bytes | EXI bytes | JSON bytes | EXI saving | Sig share of EXI |",
                "|---|--:|--:|--:|--:|--:|",
            };
            lines.AddRange(rows.Select(r =>
                $"| {r.Variant} | {r.SignatureBytes:N0} | {r.ExiBytes:N0} | {r.JsonBytes:N0} " +
                $"| {r.ExiSavingBytes:N0} B ({r.ExiSavingPercent:F1} %) | {r.SignatureShareOfExiPercent:F1} % |"));
            return string.Join("\n", lines);
        }
    }
}
