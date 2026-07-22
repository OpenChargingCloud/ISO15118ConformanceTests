using NUnit.Framework;

using X = Vanaheimr.V2G.XmlDsig.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Interop
{
    /// <summary>
    /// Our generator, fed <c>xmldsig-core-schema.xsd</c> standalone (the <c>Vanaheimr.V2G.Exi.XmlDsig</c>
    /// project), reproduces <b>byte-for-byte</b> the exact 209-byte <c>SignedInfo</c> octets Josev's own codec
    /// signs (<see cref="JosevPnCSignatureDiag.JosevStandaloneXmldsigSignedInfoHex"/>). This is what lets our
    /// SECC verify Josev-style PnC signatures — see <c>XmlDsigInteropVerify</c>. Notably our generator matches
    /// Josev's <b>pre-generated</b> grammar exactly, where EXIficient's own <i>runtime</i> XSDGrammarsBuilder
    /// over the same schema does not (244 B, per <c>tools/exificient-ref</c>).
    /// </summary>
    [TestFixture]
    public class XmlDsigStandaloneGrammarReproducesJosev
    {
        [Test]
        public void OurStandaloneXmldsigCodec_MatchesJosevOctets()
        {
            var si = new X.SignedInfoType(
                Id: null,
                CanonicalizationMethod: new X.CanonicalizationMethodType("http://www.w3.org/TR/canonical-exi/", ANY: null),
                SignatureMethod: new X.SignatureMethodType("http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256", HMACOutputLength: null, ANY: null),
                Reference: new[]
                {
                    new X.ReferenceType(
                        Id: null, Type: null, URI: "#id1",
                        Transforms: new X.TransformsType(new[] { new X.TransformType("http://www.w3.org/TR/canonical-exi/", XPath: null, ANY: null) }),
                        DigestMethod: new X.DigestMethodType("http://www.w3.org/2001/04/xmlenc#sha256", ANY: null),
                        DigestValue: Convert.FromBase64String("ubOqbKGp+UN/pxqyi6k04w2/TFMAqFy/dJ6LIKa3Rw0=")),
                });

            var buf = new byte[512];
            Assert.That(X.XmlDsigCodec.EncodeFragment_SignedInfo(si, buf, out int n), Is.True);
            var ours = buf.AsSpan(0, n).ToArray();
            TestContext.Out.WriteLine($"our standalone-xmldsig codec: {n} bytes");
            TestContext.Out.WriteLine($"ours : {Convert.ToHexString(ours)}");
            TestContext.Out.WriteLine($"josev: {JosevPnCSignatureDiag.JosevStandaloneXmldsigSignedInfoHex.ToUpperInvariant()}");
            Assert.That(Convert.ToHexString(ours),
                Is.EqualTo(JosevPnCSignatureDiag.JosevStandaloneXmldsigSignedInfoHex.ToUpperInvariant()));
        }
    }
}
