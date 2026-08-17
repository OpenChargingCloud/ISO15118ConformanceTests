/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ISO15118ConformanceTests
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
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
/// The fixed factory identity a recorded contract-provisioning session asks with, in one file all
/// three languages read — <see cref="PncMaterial"/>'s counterpart for the exchange that happens
/// <em>before</em> a car has a contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three identities, because -2 and -20 cannot share one and an update is not an installation.</b>
/// The -2 key transport agrees on secp256r1 and the -20 one on secp521r1, so a single OEM credential
/// could take part in only one of them — a P-256 provisioning certificate reaching a -20 station is a
/// real interop case, and it is the one where the station has to wrap for a throwaway recipient and
/// say so. And an <c>CertificateUpdateReq</c> presents the <em>expiring contract</em> rather than an
/// OEM certificate, which is the whole reason a renewal needs no other proof of who is asking.
/// </para>
/// <para>
/// <b>Both halves of each are pinned, and the certificate is the one that is easy to miss.</b> The
/// private key is fixed because a fresh key changes every signature <i>and</i> every ECDH — a replay
/// would decrypt the recorded answer to different bytes. The certificate is stored rather than
/// derived because it travels inside the request and cannot be rebuilt from the key: a self-signed
/// certificate is itself ECDSA-signed, so it differs on every creation. Pinning only the key would
/// leave a corpus that changes in the middle of a message body and reads like a state-machine bug.
/// The same lesson <see cref="PncMaterial"/> records, met a second time.
/// </para>
/// <para>
/// <b>KeyUsage is load-bearing here, unlike in the contract identity.</b> Both the station and the car
/// need an ECDH view of this credential, and .NET's <c>GetECDiffieHellmanPublicKey</c> refuses one on
/// a certificate whose KeyUsage extension excludes <c>keyAgreement</c>. A provisioning certificate
/// without it parses, validates, and then silently cannot receive a contract.
/// </para>
/// <para>
/// Test material. Not a real OEM credential, and the private keys are in the repository on purpose.
/// </para>
/// </remarks>
internal static class OemMaterial
{

    private const string FileName = "Session.oem-material.json";

    private static string Path_ =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName);

    private static JsonElement Material =>
        JsonDocument.Parse(File.ReadAllText(Path_)).RootElement;

    private static JsonElement Entry(string name) => Material.GetProperty(name);


    /// <summary>The -2 OEM provisioning key (P-256), as an ECDsa for signing the request.</summary>
    public static ECDsa Iso2SignKey() => Signer(ECCurve.NamedCurves.nistP256, "iso2Oem");

    /// <summary>The same key as an ECDH handle, for unwrapping the issued contract.</summary>
    public static ECDiffieHellman Iso2KeyAgreement() => Agreement(ECCurve.NamedCurves.nistP256, "iso2Oem");

    public static byte[] Iso2Certificate() => Der("iso2Oem");

    /// <summary>The expiring contract a <c>CertificateUpdateReq</c> presents, and the key its answer is
    /// wrapped for.</summary>
    public static ECDsa Iso2ExpiringSignKey() => Signer(ECCurve.NamedCurves.nistP256, "iso2Expiring");

    public static ECDiffieHellman Iso2ExpiringKeyAgreement() => Agreement(ECCurve.NamedCurves.nistP256, "iso2Expiring");

    public static byte[] Iso2ExpiringCertificate() => Der("iso2Expiring");

    /// <summary>The eMAID of the expiring contract. An update carries it and the answer carries it back:
    /// a renewal renews a contract, it does not issue a different one.</summary>
    public static string Iso2ExpiringEmaid() => Entry("iso2Expiring").GetProperty("emaid").GetString()!;

    /// <summary>The -20 OEM provisioning key (P-521), the only curve -20's key transport can agree on.</summary>
    public static ECDsa Iso20SignKey() => Signer(ECCurve.NamedCurves.nistP521, "iso20Oem");

    public static ECDiffieHellman Iso20KeyAgreement() => Agreement(ECCurve.NamedCurves.nistP521, "iso20Oem");

    public static byte[] Iso20Certificate() => Der("iso20Oem");


    private static byte[] Der(string name) =>
        Convert.FromHexString(Entry(name).GetProperty("certificate").GetString()!);

    private static ECParameters Parameters(ECCurve curve, string name) =>
        new()
        {
            Curve = curve,
            D     = Convert.FromHexString(Entry(name).GetProperty("privateKeyD").GetString()!),
        };

    private static ECDsa Signer(ECCurve curve, string name)
    {
        var key = ECDsa.Create(curve);
        key.ImportParameters(Parameters(curve, name));
        return key;
    }

    private static ECDiffieHellman Agreement(ECCurve curve, string name)
    {
        var key = ECDiffieHellman.Create(curve);
        key.ImportParameters(Parameters(curve, name));
        return key;
    }


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
    /// Creates the identities. Run once, deliberately: they are an <i>input</i> to every recorded
    /// provisioning session, so regenerating them invalidates those traces and every port checked
    /// against them.
    /// </summary>
    /// <remarks>
    /// <b>Idempotent by design</b>, as <see cref="PncMaterial.Regenerate"/> is and for the reason
    /// recorded there: an existing key and certificate are reused, and only missing fields are
    /// created. A regenerator that minted fresh material on every run would re-record every
    /// provisioning trace as a side effect of adding one field.
    /// </remarks>
    public static string Regenerate()
    {

        var existing = File.Exists(Path_) ? Material : default;

        var json = JsonSerializer.Serialize(new
        {
            note = "Fixed OEM-provisioning and expiring-contract identities for the session-trace "
                 + "corpus. Test material: the private keys are here on purpose and are not real "
                 + "credentials. Read by the C#, Kotlin and Swift trace suites alike, so all three ask "
                 + "for a contract as the same car. Regenerating invalidates every Session.*-cert* "
                 + "trace. KeyUsage carries keyAgreement deliberately — without it the platform "
                 + "refuses an ECDH view of the certificate and the car silently cannot be wrapped for.",
            iso2Oem      = Identity(existing, "iso2Oem",      ECCurve.NamedCurves.nistP256,
                                    "CN=WMIVIN0000000042", HashAlgorithmName.SHA256, emaid: null),
            iso2Expiring = Identity(existing, "iso2Expiring", ECCurve.NamedCurves.nistP256,
                                    "CN=DE-VAN-C00000009-7", HashAlgorithmName.SHA256,
                                    emaid: "DE-VAN-C00000009-7"),
            iso20Oem     = Identity(existing, "iso20Oem",     ECCurve.NamedCurves.nistP521,
                                    "CN=WMIVIN0000000521", HashAlgorithmName.SHA512, emaid: null),
        }, new JsonSerializerOptions { WriteIndented = true });

        var path = SourcePath();
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;

    }

    private static object Identity(JsonElement existing, string name, ECCurve curve,
                                   string subject, HashAlgorithmName hash, string? emaid)
    {

        var kept = existing.ValueKind is JsonValueKind.Object && existing.TryGetProperty(name, out var e)
                       ? e : default;

        using var key = ECDsa.Create(curve);
        if (kept.ValueKind is JsonValueKind.Object && kept.TryGetProperty("privateKeyD", out var keptD))
            key.ImportParameters(new ECParameters { Curve = curve, D = Convert.FromHexString(keptD.GetString()!) });

        byte[] certificate;
        if (kept.ValueKind is JsonValueKind.Object && kept.TryGetProperty("certificate", out var keptCert))
            certificate = Convert.FromHexString(keptCert.GetString()!);
        else
        {
            var request = new CertificateRequest(subject, key, hash);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement, true));

            // Fixed validity, so only the ECDSA self-signature varies between runs.
            using var created = request.CreateSelfSigned(
                DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(-1),
                DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(10));
            certificate = created.RawData;
        }

        return new
        {
            curve       = curve.Oid.FriendlyName ?? "EC",
            subject,
            emaid,
            privateKeyD = Convert.ToHexString(key.ExportParameters(true).D!).ToLowerInvariant(),
            certificate = Convert.ToHexString(certificate).ToLowerInvariant(),
        };

    }

}
