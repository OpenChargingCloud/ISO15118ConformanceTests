using NUnit.Framework;

using Vanaheimr.V2G.Experiments.Pqc;

namespace Vanaheimr.V2G.Experiments.Pqc.Tests
{
    /// <summary>
    /// EXPERIMENT: generates the payload-vs-signature size comparison (see docs/experiments/pqc.md)
    /// and pins its qualitative claims — the numbers that show EXI's compactness advantage becoming
    /// irrelevant once post-quantum signatures dominate the message.
    /// </summary>
    [TestFixture]
    public class SizeReportTests
    {
        [Test]
        public void SizeReport_MlDsaSignatureDwarfsExiSavings()
        {
            var rows = PqcSizeReport.Measure();
            TestContext.Out.WriteLine(PqcSizeReport.ToMarkdown(rows));

            var unsigned  = rows.Single(r => r.Variant == "unsigned");
            var classical = rows.Single(r => r.SignatureBytes > 0 && r.SignatureBytes < 200);
            var mlDsa     = rows.Single(r => r.SignatureBytes > 4000);

            Assert.Multiple(() =>
            {
                // EXI beats compact JSON in every variant (it always will — that's not in question) …
                Assert.That(rows, Has.All.Matches<PqcSizeRow>(r => r.ExiSavingBytes > 0));

                // … but the saving is structural and roughly constant, while the PQC signature adds
                // ~4.5 KB to BOTH encodings: the ML-DSA message is dominated by its signature …
                Assert.That(mlDsa.SignatureShareOfExiPercent, Is.GreaterThan(50),
                    "the ML-DSA-87 signature alone is more than half the EXI message");

                // … and EXI's whole advantage is smaller than the PQC signature it now carries.
                Assert.That(mlDsa.ExiSavingBytes, Is.LessThan(mlDsa.SignatureBytes),
                    "everything EXI saves is less than what ML-DSA adds");

                // The classical message stays payload-dominated (sig ~10 %) — the world -2/-20 was
                // designed for; under ML-DSA that flips to signature-dominated (~80 %).
                Assert.That(classical.SignatureShareOfExiPercent, Is.LessThan(15));

                // Sanity: the ML-DSA EXI message is the unsigned one plus roughly the signature.
                Assert.That(mlDsa.ExiBytes - unsigned.ExiBytes,
                    Is.GreaterThan(mlDsa.SignatureBytes).And.LessThan(mlDsa.SignatureBytes + 400),
                    "signature + SignedInfo overhead, nothing else");

                // CBOR — the binary-clean alternative — sits between EXI and JSON in every variant …
                Assert.That(rows, Has.All.Matches<PqcSizeRow>(r => r.ExiBytes < r.CborBytes && r.CborBytes < r.JsonBytes));

                // … and once byte strings stay raw, EXI's advantage collapses to structural overhead:
                // against CBOR, EXI saves only a fraction of what it saves against base64-JSON …
                Assert.That(mlDsa.ExiSavingVsCborBytes, Is.LessThan(mlDsa.ExiSavingBytes / 2),
                    "most of EXI's PQC-row 'saving' vs JSON is just base64 avoidance");

                // … single-digit percent of the message — the rounding error the conclusion talks about.
                Assert.That(mlDsa.ExiSavingVsCborPercent, Is.LessThan(10));
            });
        }
    }
}
