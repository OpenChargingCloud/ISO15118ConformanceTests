using NUnit.Framework;

using Vanaheimr.V2G.Exi.SourceGenerator.Emit;
using Vanaheimr.V2G.Exi.Tests.Infrastructure;

namespace Vanaheimr.V2G.Exi.Tests
{
    /// <summary>
    /// Gate 3 for the Swift back end: the things a byte-level diff cannot see. Bit-exactness says
    /// nothing about whether the emitted Swift is well-formed — that only shows up in
    /// <c>swift test</c>, which does not run in this suite, so the shape is checked here instead.
    /// </summary>
    [TestFixture]
    public class SwiftEmitterSplitTests
    {
        private static IReadOnlyList<GeneratedFile> AppProtocol() =>
            EmitterHarness.EmitSwift("app", "SupportedAppProtocolCodec",
                EmitterHarness.RealSchemaSet("Vanaheimr.V2G.Exi.Prototype")
                              .Where(f => f.Name.Contains("AppProtocol"))
                              .Select(f => (f.Name, f.Xsd))
                              .ToArray());

        [Test]
        public void EmitsOneFilePerTypePlusTheCodec()
        {
            var files = AppProtocol();

            Assert.That(files.Select(f => f.FileName), Is.EquivalentTo(new[]
            {
                "ResponseCode.swift",
                "AppProtocolType.swift",
                "SupportedAppProtocolReq.swift",
                "SupportedAppProtocolRes.swift",
                "SupportedAppProtocolCodec.swift",
            }));
        }

        /// <summary>
        /// A global element's body also appears in <c>SchemaPlan.ComplexTypes</c>, so emitting both
        /// without a guard produces every struct, encoder and decoder twice. Swift rejects the
        /// redeclaration, but only after the emitter has silently written it — this caught exactly
        /// that during the port.
        /// </summary>
        [Test]
        public void DeclaresNothingTwice()
        {
            var declarations = AppProtocol()
                .SelectMany(f => EmitterHarness.Lines(f)
                    .Where(l => l.StartsWith("public struct ") || l.StartsWith("public enum ") ||
                                l.StartsWith("internal func "))
                    .Select(l => l.Trim()))
                .ToList();

            Assert.That(declarations, Is.Unique);
            Assert.That(declarations, Is.Not.Empty);
        }

        /// <summary>Every codec call must resolve to a function some file in the set declares.</summary>
        [Test]
        public void EveryCodecCallResolves()
        {
            var files = AppProtocol();

            var declared = files
                .SelectMany(f => EmitterHarness.Lines(f))
                .Select(l => System.Text.RegularExpressions.Regex.Match(
                            l, @"^internal func (?<name>(?:encode|decode)[A-Za-z0-9_]*)\("))
                .Where(m => m.Success)
                .Select(m => m.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);

            var called = files
                .SelectMany(f => EmitterHarness.Lines(f))
                .SelectMany(l => EmitterHarness.CodecCall.Matches(l).Select(m => m.Groups["name"].Value))
                .Where(n => n is not ("decodeAny"))
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(called, Is.Not.Empty);
            Assert.That(called.Except(declared), Is.Empty,
                        "calls with no declaration: " + string.Join(", ", called.Except(declared)));
        }

        /// <summary>
        /// The runtime import appears exactly where the runtime is used. Swift does not warn about
        /// an unused import, so a blanket one would go unnoticed — and its absence where it *is*
        /// needed is a compile error the .NET suite would never see.
        /// </summary>
        [Test]
        public void ImportsTheRuntimeExactlyWhereItIsUsed()
        {
            foreach (var file in AppProtocol())
            {
                var usesRuntime = file.Source.Contains("BitReader") || file.Source.Contains("BitWriter") ||
                                  file.Source.Contains("ExiPrimitives") || file.Source.Contains("ExiError") ||
                                  file.Source.Contains("exiEnum");
                var imports = file.Source.Contains("import ExiRuntime");

                Assert.That(imports, Is.EqualTo(usesRuntime),
                            $"{file.FileName}: import/use mismatch (uses={usesRuntime}, imports={imports})");
            }
        }

        /// <summary>
        /// Constructs the back end does not model must fail loudly. The -2 and -20 sets are full of
        /// them, and a back end that quietly emitted something plausible for a substitution group
        /// would produce a codec that compiles, runs, and is wrong on the wire.
        /// </summary>
        [Test]
        public void RefusesConstructsItDoesNotModel()
        {
            // ISO 15118-2 passes Reject() as of the repeating-children work, so the set that still
            // exercises this is -20 CommonMessages: it carries inline choices, which are not
            // modelled. When that changes too, move this to whatever is still refused — the point
            // is that an unmodelled construct never gets emitted as something plausible.
            var set = EmitterHarness.RealSchemaSet("Vanaheimr.V2G.Exi.Iso15118_20.CommonMessages")
                                    .Select(f => (f.Name, f.Xsd))
                                    .ToArray();

            var ex = Assert.Throws<NotSupportedException>(
                         () => EmitterHarness.EmitSwift("iso20", "CommonMessagesCodec", set));

            // Which construct stops it moves as the back end grows, so the assertion is on the
            // refusal being attributable and specific, not on today's wording.
            Assert.That(ex!.Message, Does.Contain("Swift back end"));
            Assert.That(ex.Message, Does.Match(@"model(led)? yet"));
            Assert.That(ex.Message, Does.Match(@"'[^']+'"), "the refusal must name what it refused");
        }
    }
}
