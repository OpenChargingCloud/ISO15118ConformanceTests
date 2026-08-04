/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

using cloud.charging.open.protocols.ISO15118_20.CommonMessages;
using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;

namespace Vanaheimr.V2G.Experiments.Pqc
{
    /// <summary>One measured wire-size variant of the exemplar message.</summary>
    public sealed record PqcSizeRow(string Variant, int SignatureBytes, int ExiBytes, int CborBytes, int JsonBytes)
    {
        /// <summary>EXI's absolute saving over compact JSON for this variant.</summary>
        public int ExiSavingBytes => JsonBytes - ExiBytes;

        /// <summary>EXI's relative saving over compact JSON for this variant.</summary>
        public double ExiSavingPercent => JsonBytes == 0 ? 0 : 100.0 * ExiSavingBytes / JsonBytes;

        /// <summary>EXI's absolute saving over CBOR — the binary-clean alternative, so this is EXI's
        /// *structural* advantage with the base64 effect removed.</summary>
        public int ExiSavingVsCborBytes => CborBytes - ExiBytes;

        /// <summary>EXI's relative saving over CBOR for this variant.</summary>
        public double ExiSavingVsCborPercent => CborBytes == 0 ? 0 : 100.0 * ExiSavingVsCborBytes / CborBytes;

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
            var cbor = ToCbor(message);
            return new PqcSizeRow(variant, signatureBytes, exiLength, cbor.Length, json.Length);
        }

        public static string ToMarkdown(IReadOnlyList<PqcSizeRow> rows)
        {
            var lines = new List<string>
            {
                "| Signature | Sig bytes | EXI | CBOR | JSON | EXI vs JSON | EXI vs CBOR | Sig share of EXI |",
                "|---|--:|--:|--:|--:|--:|--:|--:|",
            };
            lines.AddRange(rows.Select(r =>
                $"| {r.Variant} | {r.SignatureBytes:N0} | {r.ExiBytes:N0} | {r.CborBytes:N0} | {r.JsonBytes:N0} " +
                $"| {r.ExiSavingBytes:N0} B ({r.ExiSavingPercent:F1} %) " +
                $"| {r.ExiSavingVsCborBytes:N0} B ({r.ExiSavingVsCborPercent:F1} %) " +
                $"| {r.SignatureShareOfExiPercent:F1} % |"));
            return string.Join("\n", lines);
        }

        // ── CBOR: the binary-clean strawman — same structure and field names as the JSON variant,
        //    but byte arrays as raw CBOR byte strings (no base64 inflation). Hand-mapped because the
        //    point is a *fair* minimal encoding, not a reflection framework. Nulls skipped like JSON.
        private static byte[] ToCbor(AuthorizationReq m)
        {
            var w = new System.Formats.Cbor.CborWriter();

            void Map(int count, Action body) { w.WriteStartMap(count); body(); w.WriteEndMap(); }
            void Text(string key, string value) { w.WriteTextString(key); w.WriteTextString(value); }
            void Bytes(string key, byte[] value) { w.WriteTextString(key); w.WriteByteString(value); }

            Map(3, () =>
            {
                w.WriteTextString("Header");
                var h = m.Header;
                Map(h.Signature is null ? 2 : 3, () =>
                {
                    Bytes("SessionID", h.SessionID);
                    w.WriteTextString("TimeStamp"); w.WriteUInt64(h.TimeStamp);
                    if (h.Signature is { } sig)
                    {
                        w.WriteTextString("Signature");
                        Map(2, () =>
                        {
                            w.WriteTextString("SignedInfo");
                            var si = sig.SignedInfo;
                            Map(3, () =>
                            {
                                w.WriteTextString("CanonicalizationMethod");
                                Map(1, () => Text("Algorithm", si.CanonicalizationMethod.Algorithm));
                                w.WriteTextString("SignatureMethod");
                                Map(1, () => Text("Algorithm", si.SignatureMethod.Algorithm));
                                w.WriteTextString("Reference");
                                w.WriteStartArray(si.Reference.Count);
                                foreach (var r in si.Reference)
                                    Map(3, () =>
                                    {
                                        Text("URI", r.URI!);
                                        w.WriteTextString("DigestMethod");
                                        Map(1, () => Text("Algorithm", r.DigestMethod.Algorithm));
                                        Bytes("DigestValue", r.DigestValue);
                                    });
                                w.WriteEndArray();
                            });
                            w.WriteTextString("SignatureValue");
                            Map(1, () => Bytes("Value", sig.SignatureValue.Value));
                        });
                    }
                });

                Text("SelectedAuthorizationService", m.SelectedAuthorizationService.ToString());

                w.WriteTextString("PnC_AReqAuthorizationMode");
                var pnc = m.PnC_AReqAuthorizationMode!;
                Map(3, () =>
                {
                    Text("Id", pnc.Id);
                    Bytes("GenChallenge", pnc.GenChallenge);
                    w.WriteTextString("ContractCertificateChain");
                    Map(2, () =>
                    {
                        Bytes("Certificate", pnc.ContractCertificateChain.Certificate);
                        w.WriteTextString("SubCertificates");
                        Map(1, () =>
                        {
                            w.WriteTextString("Certificate");
                            var subs = pnc.ContractCertificateChain.SubCertificates.Certificate;
                            w.WriteStartArray(subs.Count);
                            foreach (var c in subs) w.WriteByteString(c);
                            w.WriteEndArray();
                        });
                    });
                });
            });

            return w.Encode();
        }
    }
}
