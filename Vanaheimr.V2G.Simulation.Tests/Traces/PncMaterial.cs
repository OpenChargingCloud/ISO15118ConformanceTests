using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

namespace Vanaheimr.V2G.Simulation.Tests.Traces;

/// <summary>
/// The fixed contract identity a recorded Plug &amp; Charge session signs with.
/// </summary>
/// <remarks>
/// <para>
/// Both halves have to be pinned, and the second one is easy to miss. The <b>private key</b> is a
/// constant for the same reason the meter corpus's is: a fresh key changes every signature. The
/// <b>certificate</b> has to be a checked-in file, because it travels inside the request — and it
/// cannot simply be regenerated from the fixed key, since a self-signed certificate is itself
/// ECDSA-signed and therefore differs on every creation. Pinning only the key would leave a corpus
/// that changes in the middle of a message body and looks like a state-machine bug.
/// </para>
/// <para>
/// Test material. Not a real contract credential, and the private key is in the source on purpose.
/// </para>
/// </remarks>
internal static class PncMaterial
{

    /// <summary>A fixed P-256 private scalar. Test material; never a real contract key.</summary>
    private const string PrivateKeyD =
        "5b3fa1c8d9e04726b8153c4a9f2e6d0817ac35be49f1d268a70b5c93e4f18d2a";

    private const string CertificateFileName = "Session.pnc-contract.der";

    /// <summary>The contract private key, as the EVCC needs it. Caller owns disposal.</summary>
    public static ECDsa Key()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D     = Convert.FromHexString(PrivateKeyD),
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
        File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", CertificateFileName));

    public static string SourceCertificatePath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vanaheimr.V2G.Simulation.Tests.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");
        var vectors = Path.Combine(dir!.FullName, "Vectors");
        Directory.CreateDirectory(vectors);
        return Path.Combine(vectors, CertificateFileName);
    }

    /// <summary>
    /// Creates the certificate. Run once, deliberately: its bytes are an input to every recorded PnC
    /// session, so regenerating it invalidates the corpus and every port checked against it.
    /// </summary>
    public static void Regenerate()
    {
        using var key = Key();
        var request = new CertificateRequest("CN=TraceCorpusContract", key, HashAlgorithmName.SHA256);
        // Fixed validity so only the ECDSA self-signature varies between runs — one fewer reason for
        // the file to differ if somebody does regenerate it and diffs out of curiosity.
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(-1),
            DateTimeOffset.FromUnixTimeSeconds(1_767_225_600).AddYears(10));

        File.WriteAllBytes(SourceCertificatePath(), certificate.RawData);
    }
}
