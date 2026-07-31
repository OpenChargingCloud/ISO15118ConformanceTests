using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace Vanaheimr.V2G.Simulation.Tests.Traces;

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


    private static string SourcePath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vanaheimr.V2G.Simulation.Tests.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");
        var vectors = Path.Combine(dir!.FullName, "Vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, FileName);
    }

    /// <summary>
    /// Creates the identity. Run once, deliberately: it is an <i>input</i> to every recorded PnC
    /// session, so regenerating it invalidates those traces and every port checked against them.
    /// </summary>
    public static string Regenerate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var d = key.ExportParameters(includePrivateParameters: true).D!;

        var request = new CertificateRequest("CN=TraceCorpusContract", key, HashAlgorithmName.SHA256);
        // Fixed validity, so only the ECDSA self-signature varies between runs — one fewer reason for
        // the file to differ if somebody regenerates it and diffs out of curiosity.
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(-1),
            DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(10));

        var json = JsonSerializer.Serialize(new
        {
            note = "Fixed Plug & Charge contract identity for the session-trace corpus. Test material: "
                 + "the private key is here on purpose and is not a real credential. Read by the C#, "
                 + "Kotlin and Swift trace suites alike, so all three sign the identical session. "
                 + "Regenerating invalidates every Session.*-pnc trace.",
            curve       = "P-256",
            privateKeyD = Convert.ToHexString(d).ToLowerInvariant(),
            certificate = Convert.ToHexString(certificate.RawData).ToLowerInvariant(),
        }, new JsonSerializerOptions { WriteIndented = true });

        var path = SourcePath();
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }
}
