using NUnit.Framework;

using Vanaheimr.V2G.Exi.Tests.Infrastructure;
using Vanaheimr.V2G.Iso15118_2.Generated;

namespace Vanaheimr.V2G.Exi.Tests.Interop
{
    /// <summary>
    /// ISO 15118-2 <b>DC</b> EIM frames captured from a live Josev session (SwitchEV/iso15118 @ <c>d645255</c>,
    /// rebuilt on Debian trixie, EIM, no TLS — see <c>docs/interop-runs/2026-07-21-iso2-dc-eim-notls/</c>).
    /// Josev encodes with EXIficient; our codec must decode and re-encode each byte-for-byte. This covers the
    /// full DC charge loop (ChargeParameterDiscovery → CableCheck → PreCharge → PowerDelivery → CurrentDemand
    /// → WeldingDetection), including the DC-specific <c>PhysicalValue</c>/<c>DC_EVStatus</c> content. Runs in
    /// normal CI (bytes baked in).
    /// </summary>
    [TestFixture]
    public class JosevCapturedFramesDcTests
    {
        private static readonly (string Name, string Hex)[] Frames =
        {
            ("SessionSetupReq",             "8098004011d019ea204b245f0000"),
            ("ChargeParameterDiscoveryReq", "809802086d14c116a891219094c800080028003080fa01020a1807c082019008306c1b00830781702d050000"),
            ("CableCheckReq",               "809802086d14c116a891219031000500"),
            ("PreChargeReq",                "809802086d14c116a89121917100050208064061800800"),
            ("PowerDeliveryReq",            "809802086d14c116a89121915000022000a00400"),
            ("CurrentDemandReq",            "809802086d14c116a8912190d1000a01860021006101f4020414300f800030819006102802080640"),
            ("WeldingDetectionReq",         "809802086d14c116a891219211003200"),
        };

        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (var (name, hex) in Frames)
                yield return new TestCaseData(hex).SetName(name);
        }

        [TestCaseSource(nameof(Cases))]
        public void JosevDcFrame_DecodesAndReEncodesIdentically(string hex)
        {
            var josev = HexUtil.Parse(hex);

            var decoded = (V2G_Message) Iso2Codec.DecodeAny(josev, out int consumed);
            Assert.That(consumed, Is.EqualTo(josev.Length), "our decoder must consume all of Josev's bytes");

            var buf = new byte[512];
            Assert.That(decoded.TryEncode(buf, out int n), Is.True, "re-encode failed");
            Assert.That(buf.AsSpan(0, n).ToArray(), Is.EqualTo(josev),
                "our codec must re-encode Josev's frame to the identical bytes");
        }
    }
}
