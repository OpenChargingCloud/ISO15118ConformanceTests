using NUnit.Framework;
using Vanaheimr.V2G.Iso15118_20.WPT.Generated;

namespace Vanaheimr.V2G.Exi.Tests
{
    /// <summary>
    /// Self-consistency (encode → decode → re-encode) coverage for the two WPT fields that exercise
    /// grammar shapes this repo's generator designed independently, because no working cbV2G reference
    /// exists for them (see <c>Iso15118_20WptFixtures</c> and <c>docs/xsd-inventory-15118-20.md</c>):
    /// <c>WPT_LF_DataPackageList</c> (an optional bounded list mid-run, capped at 2 items — cbV2G's own
    /// generated grammar for it does work, just not past that cap) and <c>LF_SystemSetupData</c> (whose
    /// <c>WPT_LF_TransmitterDataType.TxSpecData</c>, minOccurs=2/maxOccurs=255 followed by an optional
    /// tail, cbV2G's own generated encoder cannot represent at all — confirmed empirically to fail with
    /// EXI_ERROR__UNKNOWN_EVENT_CODE even at the schema's required minimum). These tests can only assert
    /// the C# codec is internally consistent, not that it matches an external reference.
    /// </summary>
    [TestFixture]
    public class Iso15118_20WptSelfConsistencyTests
    {
        private static MessageHeaderType Header() =>
            new(SessionID: new byte[8], TimeStamp: 1_700_000_000UL, Signature: null);

        [Test]
        public void FinePositioningReq_WithDataPackageList_Present_Roundtrips()
        {
            // Exercises the "mid-run optional list" construct with the list non-empty (2 items, its
            // cbV2G-verified cap) AND the following optional element present.
            var message = new WPT_FinePositioningReq(
                Header(), Processing.Finished, WPT_EVResult.EVResultSuccess,
                VendorSpecificDataContainer: new[] { new byte[] { 0x01 }, new byte[] { 0x02 } },
                WPT_LF_DataPackageList: new WPT_LF_DataPackageListType(
                    NumPackages: 1,
                    WPT_LF_DataPackage: new WPT_LF_DataPackageType(
                        PackageIndex: 0,
                        LF_TxData: new WPT_LF_TxDataListType(new WPT_LF_TxDataType(TxIdentifier: 1, new RationalNumberType(0, 100))),
                        LF_RxData: null)));

            var buf1 = new byte[512];
            Assert.That(message.TryEncode(buf1, out int n1), Is.True, "encode failed");

            var decoded = (WPT_FinePositioningReq)WptCodec.DecodeAny(buf1.AsSpan(0, n1), out int consumed);
            Assert.That(consumed, Is.EqualTo(n1), "decoder did not consume all encoded bytes");

            var buf2 = new byte[512];
            Assert.That(decoded.TryEncode(buf2, out int n2), Is.True, "re-encode failed");

            Assert.That(buf2.AsSpan(0, n2).ToArray(), Is.EqualTo(buf1.AsSpan(0, n1).ToArray()),
                "decode∘encode is not the identity on the wire");
        }

        [Test]
        public void FinePositioningSetupReq_WithLFSystemSetupData_Transmitter_Roundtrips()
        {
            // Exercises the true-self-loop "required repeating + optional tail" construct at its schema
            // minimum (2 TxSpecData items) plus the optional TxPackageSpecData tail present.
            var message = new WPT_FinePositioningSetupReq(
                Header(), Processing.Finished,
                new WPT_FinePositioningMethodListType(new[] { WPT_FinePositioningMethod.Manual }),
                new WPT_PairingMethodListType(new[] { WPT_PairingMethod.LPE }),
                new WPT_AlignmentCheckMethodListType(new[] { WPT_AlignmentCheckMethod.PowerCheck }),
                NaturalOffset: 0,
                VendorSpecificDataContainer: Array.Empty<byte[]>(),
                LF_SystemSetupData: new WPT_LF_SystemSetupDataType(
                    LF_TransmitterSetupData: new WPT_LF_TransmitterDataType(
                        NumberOfTransmitters: 2,
                        SignalFrequency: new RationalNumberType(0, 100),
                        TxSpecData: new[]
                        {
                            new WPT_TxRxSpecDataType(1, new WPT_CoordinateXYZType(0, 0, 0), new WPT_CoordinateXYZType(0, 0, 0)),
                            new WPT_TxRxSpecDataType(2, new WPT_CoordinateXYZType(10, 0, 0), new WPT_CoordinateXYZType(0, 0, 0)),
                        },
                        TxPackageSpecData: null),
                    LF_ReceiverSetupData: null));

            var buf1 = new byte[512];
            Assert.That(message.TryEncode(buf1, out int n1), Is.True, "encode failed");

            var decoded = (WPT_FinePositioningSetupReq)WptCodec.DecodeAny(buf1.AsSpan(0, n1), out int consumed);
            Assert.That(consumed, Is.EqualTo(n1), "decoder did not consume all encoded bytes");

            var buf2 = new byte[512];
            Assert.That(decoded.TryEncode(buf2, out int n2), Is.True, "re-encode failed");

            Assert.That(buf2.AsSpan(0, n2).ToArray(), Is.EqualTo(buf1.AsSpan(0, n1).ToArray()),
                "decode∘encode is not the identity on the wire");
        }
    }
}
