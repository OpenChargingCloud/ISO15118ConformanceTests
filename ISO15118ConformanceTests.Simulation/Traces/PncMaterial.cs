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
using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace ISO15118ConformanceTests.Simulation.Traces;

/// <summary>
/// The fixed contract identity a recorded Plug &amp; Charge session signs with, in one file all three
/// languages read.
/// </summary>
/// <remarks>
/// <para>
/// Both halves have to be pinned, and the second one is easy to miss. The <b>private key</b> is fixed
/// for the same reason the meter corpus's is: a fresh key changes every signature. The
/// <b>certificate</b> has to be stored rather than derived, because it travels inside the request and
/// cannot be regenerated from the fixed key — a self-signed certificate is itself ECDSA-signed and so
/// differs on every creation. Pinning only the key would leave a corpus that changes in the middle of
/// a message body and reads like a state-machine bug.
/// </para>
/// <para>
/// It lives in <c>Vectors/</c> as data rather than as a constant in this file because the Kotlin and
/// Swift ports need exactly the same identity to reproduce the same session. A constant here would
/// have to be copied into two more languages, and copies drift — the whole reason the corpus exists.
/// </para>
/// <para>
/// Test material. Not a real contract credential, and the private key is in the repository on purpose.
/// </para>
/// </remarks>
internal static class PncMaterial
{

    private const string FileName = "Session.pnc-material.json";

    private static string Path_ =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName);

    private static JsonElement Material =>
        JsonDocument.Parse(File.ReadAllText(Path_)).RootElement;

    /// <summary>The contract private key, as the EVCC needs it. Caller owns disposal.</summary>
    public static ECDsa Key()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D     = Convert.FromHexString(Material.GetProperty("privateKeyD").GetString()!),
        });
        return key;
    }

    /// <summary>The public half, for verifying what a replayed session actually signed.</summary>
    public static ECDsa PublicKey()
    {
        using var key = Key();
        return ECDsa.Create(key.ExportParameters(includePrivateParameters: false));
    }

    public static byte[] Certificate() =>
        Convert.FromHexString(Material.GetProperty("certificate").GetString()!);

    /// <summary>A certificate whose Common Name is 19 characters and therefore cannot be an eMAID —
    /// the value this corpus actually shipped with until 2026-07-31, kept as the negative case that
    /// every back end's check is held to.</summary>
    public static byte[] CertificateWithUnusableEmaid() =>
        Convert.FromHexString(Material.GetProperty("certificateWithUnusableEmaid").GetString()!);


    private static string SourcePath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ISO15118ConformanceTests.Simulation.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");
        var vectors = Path.Combine(dir!.FullName, "..", "libs", "EVSimulatorApp", "vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, FileName);
    }

    /// <summary>
    /// Creates the identity. Run once, deliberately: it is an <i>input</i> to every recorded PnC
    /// session, so regenerating it invalidates those traces and every port checked against them.
    /// </summary>
    /// <remarks>
    /// <b>Idempotent by design.</b> An existing key and certificate are reused; only fields that are
    /// missing get created. A regenerator that minted a fresh key on every run would re-record every
    /// PnC trace as a side effect of adding one field — which is exactly what happened once, and is
    /// the sort of churn that makes a checked-in corpus unreviewable.
    /// </remarks>
    public static string Regenerate()
    {
        var existing = File.Exists(Path_) ? Material : default;

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (existing.ValueKind is JsonValueKind.Object &&
            existing.TryGetProperty("privateKeyD", out var kept))
            key.ImportParameters(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D     = Convert.FromHexString(kept.GetString()!),
            });

        var d = key.ExportParameters(includePrivateParameters: true).D!;

        // The Common Name IS the eMAID, and ISO 15118-2 constrains eMAIDType to 14-15 characters
        // (V2G_CI_MsgDataTypes.xsd). The first version of this corpus used "TraceCorpusContract" —
        // 19 characters — and every layer accepted it: the generated codec does not enforce string
        // length facets, so a non-conformant eMAID went on the wire and nothing said so. Found while
        // giving Swift an X.509 reader, because that reader checks the length the schema states.
        // DE + provider (3) + instance (9) = 14, the form without a check digit.
        var request = new CertificateRequest("CN=DE8AA1A2B3C4D5", key, HashAlgorithmName.SHA256);
        // Fixed validity, so only the ECDSA self-signature varies between runs — one fewer reason for
        // the file to differ if somebody regenerates it and diffs out of curiosity.
        var notBefore = DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(-1);
        var notAfter  = DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(10);

        using var created = request.CreateSelfSigned(notBefore, notAfter);
        var certificate = existing.ValueKind is JsonValueKind.Object &&
                          existing.TryGetProperty("certificate", out var keptCert)
                              ? Convert.FromHexString(keptCert.GetString()!)
                              : created.RawData;

        // The negative case, generated once and kept: 19 characters, which is what this corpus
        // shipped with until the length rule was checked anywhere at all.
        using var unusable = new CertificateRequest("CN=TraceCorpusContract", key, HashAlgorithmName.SHA256)
                                 .CreateSelfSigned(notBefore, notAfter);
        var unusableDer = existing.ValueKind is JsonValueKind.Object &&
                          existing.TryGetProperty("certificateWithUnusableEmaid", out var keptBad)
                              ? Convert.FromHexString(keptBad.GetString()!)
                              : unusable.RawData;

        var json = JsonSerializer.Serialize(new
        {
            note = "Fixed Plug & Charge contract identity for the session-trace corpus. Test material: "
                 + "the private key is here on purpose and is not a real credential. Read by the C#, "
                 + "Kotlin and Swift trace suites alike, so all three sign the identical session. "
                 + "Regenerating invalidates every Session.*-pnc trace.",
            curve       = "P-256",
            privateKeyD = Convert.ToHexString(d).ToLowerInvariant(),
            certificate = Convert.ToHexString(certificate).ToLowerInvariant(),
            certificateWithUnusableEmaid = Convert.ToHexString(unusableDer).ToLowerInvariant(),
        }, new JsonSerializerOptions { WriteIndented = true });

        var path = SourcePath();
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }
}
