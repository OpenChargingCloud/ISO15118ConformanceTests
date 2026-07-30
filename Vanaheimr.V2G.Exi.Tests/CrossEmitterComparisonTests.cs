using NUnit.Framework;

using Vanaheimr.V2G.Exi.Tests.Infrastructure;

namespace Vanaheimr.V2G.Exi.Tests
{
    /// <summary>
    /// Gate 2 of the three in <c>kotlin/README.md</c>: two back ends, one <c>SchemaPlan</c>, and
    /// every generated function performing the same wire operations in the same order.
    /// </summary>
    /// <remarks>
    /// The vectors pin the encoder against cbV2G, and each decoder is then checked by round-tripping
    /// through its own encoder — so a bug mirrored in both directions passes both. Independent
    /// emitters agreeing is what catches it. Until now this comparison existed only as prose in
    /// <c>kotlin/README.md</c>; nothing in the repository ran it.
    /// </remarks>
    [TestFixture]
    public class CrossEmitterComparisonTests
    {
        private static (string, string)[] AppProtocolSchema() =>
            EmitterHarness.RealSchemaSet("Vanaheimr.V2G.Exi.Prototype")
                          .Where(f => f.Name.Contains("AppProtocol"))
                          .Select(f => (f.Name, f.Xsd))
                          .ToArray();

        [Test]
        public void SwiftAndCSharpAgreeOperationForOperation()
        {
            var schema = AppProtocolSchema();

            var swift  = CrossEmitterComparison.Operations(
                             EmitterHarness.EmitSwift("app", "SupportedAppProtocolCodec", schema));
            var csharp = CrossEmitterComparison.Operations(
                             EmitterHarness.EmitCSharp("Test.App", "SupportedAppProtocolCodec", schema));

            Assert.That(swift, Is.Not.Empty, "no functions parsed out of the Swift output");

            var problems = CrossEmitterComparison.Diff(swift, "swift", csharp, "csharp");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void KotlinAndCSharpAgreeOperationForOperation()
        {
            // Kotlin is compared too, not for Swift's sake but because this gate has never actually
            // run: the claim that the two agree was carried by the documentation alone.
            var schema = AppProtocolSchema();

            var kotlin = CrossEmitterComparison.Operations(
                             EmitterHarness.Emit("app", "SupportedAppProtocolCodec", schema));
            var csharp = CrossEmitterComparison.Operations(
                             EmitterHarness.EmitCSharp("Test.App", "SupportedAppProtocolCodec", schema));

            var problems = CrossEmitterComparison.Diff(kotlin, "kotlin", csharp, "csharp");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        /// <summary>
        /// The whole ISO 15118-2 set — 94 types, 111 generated files. This is the first schema set
        /// large enough for the comparison to be a real gate rather than a demonstration: every
        /// construct the back end models appears here, in combinations no mini-XSD covers.
        /// </summary>
        [Test]
        public void SwiftAndCSharpAgreeAcrossTheWholeIso2Set()
        {
            var schema = EmitterHarness.RealSchemaSet("Vanaheimr.V2G.Exi.Iso15118_2")
                                       .Select(f => (f.Name, f.Xsd))
                                       .ToArray();

            var swift  = CrossEmitterComparison.Operations(
                             EmitterHarness.EmitSwift("iso2", "Iso15118_2Codec", schema));
            var csharp = CrossEmitterComparison.Operations(
                             EmitterHarness.EmitCSharp("Iso2", "Iso15118_2Codec", schema));

            Assert.That(swift.Count, Is.GreaterThan(150), "far fewer functions than the set has types");

            var problems = CrossEmitterComparison.Diff(swift, "swift", csharp, "csharp");
            Assert.That(problems, Is.Empty,
                        $"{problems.Count} of {csharp.Count} functions differ:\n" +
                        string.Join("\n", problems.Take(15)));
        }

        /// <summary>
        /// ISO 15118-20 CommonMessages — 144 types, and the set that brings inline choices, which
        /// -2 has none of. Larger than -2 in every dimension, so it is the harder gate of the two.
        /// </summary>
        [Test]
        public void SwiftAndCSharpAgreeAcrossTheIso20CommonMessagesSet()
        {
            var schema = EmitterHarness.RealSchemaSet("Vanaheimr.V2G.Exi.Iso15118_20.CommonMessages")
                                       .Select(f => (f.Name, f.Xsd))
                                       .ToArray();

            var swift  = CrossEmitterComparison.Operations(
                             EmitterHarness.EmitSwift("c20", "CommonMessagesCodec", schema));
            var csharp = CrossEmitterComparison.Operations(
                             EmitterHarness.EmitCSharp("C20", "CommonMessagesCodec", schema));

            Assert.That(swift.Count, Is.GreaterThan(200), "far fewer functions than the set has types");

            var problems = CrossEmitterComparison.Diff(swift, "swift", csharp, "csharp");
            Assert.That(problems, Is.Empty,
                        $"{problems.Count} of {csharp.Count} functions differ:\n" +
                        string.Join("\n", problems.Take(15)));
        }

        /// <summary>
        /// The comparison is only worth anything if it can fail. A back end whose output is parsed
        /// into an empty operation list would make every assertion above vacuously true.
        /// </summary>
        [Test]
        public void ComparisonDetectsADivergence()
        {
            var schema = AppProtocolSchema();
            var swift  = CrossEmitterComparison.Operations(
                             EmitterHarness.EmitSwift("app", "SupportedAppProtocolCodec", schema));

            Assert.That(swift.Values.Sum(v => v.Count), Is.GreaterThan(50),
                        "too few operations parsed — the extractor is not seeing the generated code");

            // Drop one operation from one function and require the diff to name it.
            var mangled = swift.ToDictionary(
                kv => kv.Key,
                kv => kv.Key == "encode:appprotocoltype"
                          ? (IReadOnlyList<string>) kv.Value.Skip(1).ToList()
                          : kv.Value,
                StringComparer.Ordinal);

            var problems = CrossEmitterComparison.Diff(mangled, "mangled", swift, "swift");
            Assert.That(problems, Has.Exactly(1).Contains("encode:appprotocoltype"));
        }
    }
}
