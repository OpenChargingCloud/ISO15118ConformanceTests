using System.Text.RegularExpressions;

using Vanaheimr.V2G.Exi.SourceGenerator.Emit;

namespace Vanaheimr.V2G.Exi.Tests.Infrastructure
{
    /// <summary>
    /// Reduces a back end's output to the sequence of wire operations each generated function
    /// performs — event codes, bit widths, primitive calls and nested codec calls, in order — so
    /// two back ends can be compared for the same <c>SchemaPlan</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the gate the vector corpus cannot be: vectors pin the *encoder* against a reference,
    /// and a decoder is then checked by round-tripping through that same encoder. A bug mirrored in
    /// both directions survives both. Two independent emitters agreeing operation-for-operation is
    /// what rules it out — and the C# side is itself pinned to the vectors.
    /// </para>
    /// <para>
    /// Values are deliberately *not* compared. <c>(uint)msg.SchemaID</c>, <c>UInt32(v)</c> and
    /// <c>msg.schemaID!!.toLong().toUInt()</c> are the same operand written three ways; insisting
    /// they match textually would compare languages, not grammars. What must agree is every
    /// literal event code and every bit width, because those are the bits on the wire.
    /// </para>
    /// </remarks>
    internal static class CrossEmitterComparison
    {
        /// <summary>
        /// A `w.WriteBits(` / `r.readBits(` call head, either casing. The argument list is then
        /// scanned by hand rather than matched: every back end puts a `// comment` after the call,
        /// and a regex that tries to end at the closing paren either stops short or swallows the
        /// comment's own brackets.
        /// </summary>
        private static readonly Regex BitsHead =
            new(@"\b[wr]\.(?<op>[Ww]rite[Bb]its|[Rr]ead[Bb]its)\s*\(", RegexOptions.Compiled);

        private static readonly Regex Primitive =
            new(@"\bExiPrimitives\.(?<name>[A-Za-z]+)\s*\(", RegexOptions.Compiled);

        /// <summary>A nested per-type codec call: `Encode_Foo(`, `encodeFoo(`, `decodeFoo(`.</summary>
        private static readonly Regex Codec =
            new(@"(?<![A-Za-z0-9_.])(?<name>(?:[Ee]ncode|[Dd]ecode)_?[A-Z][A-Za-z0-9_]*)\s*\(",
                RegexOptions.Compiled);

        /// <summary>The head of a generated encoder/decoder, in any of the three languages.</summary>
        private static readonly Regex FunctionHead =
            new(@"^\s*(?:(?:private|internal|public)\s+)?(?:static\s+)?(?:fun|func|void|[A-Za-z_][A-Za-z0-9_<>\[\]?]*)\s+"
              + @"(?<name>(?:[Ee]ncode|[Dd]ecode)_?[A-Z][A-Za-z0-9_]*)\s*\(",
                RegexOptions.Compiled);

        /// <summary>Function key → the ordered operations it performs.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> Operations(
            IReadOnlyList<GeneratedFile> files)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                string? current = null;

                foreach (var line in EmitterHarness.Lines(file))
                {
                    var head = FunctionHead.Match(line);
                    if (head.Success)
                    {
                        current = Key(head.Groups["name"].Value);
                        if (!result.ContainsKey(current)) result[current] = new List<string>();
                        continue;
                    }

                    if (current is null) continue;

                    foreach (var op in OperationsIn(line))
                        result[current].Add(op);
                }
            }

            return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>) kv.Value,
                                       StringComparer.Ordinal);
        }

        /// <summary>
        /// One line's operations, in source order. A line carries at most a handful, and the three
        /// back ends put the same ones on the same line because they share the emitter's structure.
        /// </summary>
        private static IEnumerable<string> OperationsIn(string rawLine)
        {
            var line = StripComment(rawLine);
            var ops  = new List<(int Pos, string Op)>();

            foreach (Match m in BitsHead.Matches(line))
            {
                var open = line.IndexOf('(', m.Index);
                var args = SplitArgs(Inside(line, open));
                var write = m.Groups["op"].Value.StartsWith("w", StringComparison.OrdinalIgnoreCase);
                // Reads carry only the width; writes carry the event code first.
                var code  = write && args.Count > 0 ? Operand(args[0], isWidth: false) : null;
                var idx   = write ? 1 : 0;
                var width = Operand(args.Count > idx ? args[idx] : "", isWidth: true);
                ops.Add((m.Index, code is null ? $"bits(w={width})" : $"bits({code},w={width})"));
            }

            foreach (Match m in Primitive.Matches(line))
                ops.Add((m.Index, "prim:" + m.Groups["name"].Value.ToLowerInvariant()));

            foreach (Match m in Codec.Matches(line))
                ops.Add((m.Index, "codec:" + Key(m.Groups["name"].Value)));

            return ops.OrderBy(o => o.Pos).Select(o => o.Op);
        }

        /// <summary>Drops a trailing `//` comment — all three back ends annotate their event codes.</summary>
        private static string StripComment(string line)
        {
            var at = line.IndexOf("//", StringComparison.Ordinal);
            return at < 0 ? line : line.Substring(0, at);
        }

        /// <summary>The text between the paren at <paramref name="open"/> and its match.</summary>
        private static string Inside(string line, int open)
        {
            if (open < 0) return "";
            var depth = 0;
            for (var i = open; i < line.Length; i++)
            {
                if (line[i] is '(' or '[') depth++;
                else if (line[i] is ')' or ']')
                {
                    depth--;
                    if (depth == 0) return line.Substring(open + 1, i - open - 1);
                }
            }
            return line.Substring(open + 1);
        }

        /// <summary>
        /// A literal stays itself; anything else collapses to <c>value</c>. Conditional widths such
        /// as <c>i == 0 ? 1 : 2</c> are kept, with whitespace removed — they *are* wire structure.
        /// </summary>
        /// <param name="isWidth">
        /// A conditional *width* is wire structure and is kept, with its identifiers masked; a
        /// conditional *value* is just an operand written differently in each language — Swift
        /// unwraps into a local where C# reaches through the optional — and collapses like any
        /// other value.
        /// </param>
        private static string Operand(string raw, bool isWidth)
        {
            var s = raw.Trim().TrimEnd('u', 'U', 'L');

            // Kotlin spells a conditional `if (c) a else b` where C# and Swift use `c ? a : b`.
            // Same wire structure, so it is canonicalised rather than treated as a divergence.
            var kotlinIf = Regex.Match(s, @"^\s*if\s*\((?<c>.+?)\)\s*(?<a>.+?)\s+else\s+(?<b>.+?)\s*$");
            if (kotlinIf.Success)
                s = $"{kotlinIf.Groups["c"].Value}?{kotlinIf.Groups["a"].Value}:{kotlinIf.Groups["b"].Value}";

            s = Regex.Replace(s, @"\s+", "");

            // C# writes its literals as `1u`; Swift and Kotlin do not. Stripping the suffix has to
            // happen before identifiers are masked, or the `u` becomes part of the mask and two
            // spellings of the same constant read as a divergence.
            s = Regex.Replace(s, @"(\d)[uUlL]+", "$1");
            if (Regex.IsMatch(s, @"^\(?[a-zA-Z]+\)?\d+$")) s = Regex.Replace(s, @"^\(?[a-zA-Z]+\)?", "");
            if (Regex.IsMatch(s, @"^\d+$")) return s;
            if (s.Contains('?') && isWidth) return Regex.Replace(s, @"[a-zA-Z_][a-zA-Z0-9_]*", "x");
            return "value";
        }

        /// <summary>Splits a call's argument list on top-level commas.</summary>
        private static List<string> SplitArgs(string args)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is '(' or '[') depth++;
                else if (args[i] is ')' or ']') depth--;
                else if (args[i] == ',' && depth == 0)
                {
                    parts.Add(args.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(args.Substring(start));
            return parts;
        }

        /// <summary>`Encode_AppProtocolType`, `encodeAppProtocolType` → `encode:appprotocoltype`.</summary>
        private static string Key(string name)
        {
            var lower = name.ToLowerInvariant().Replace("_", "");
            return lower.StartsWith("encode") ? "encode:" + lower.Substring(6)
                                              : "decode:" + lower.Substring(6);
        }

        /// <summary>
        /// Compares two back ends' operation maps, returning one line per disagreement. An empty
        /// result means every function they share performs the same operations in the same order.
        /// </summary>
        /// <remarks>
        /// Deliberately asymmetric: a function <paramref name="right"/> emits and
        /// <paramref name="left"/> does not is a hole in <paramref name="left"/> and fails. The
        /// reverse is not — a back end may need a helper the others do not, as Swift does for
        /// enumerations, whose fallible initialiser has no C# counterpart (C# casts inline).
        /// Those carry no wire operations of their own beyond the read they wrap.
        /// </remarks>
        public static IReadOnlyList<string> Diff(
            IReadOnlyDictionary<string, IReadOnlyList<string>> left,  string leftName,
            IReadOnlyDictionary<string, IReadOnlyList<string>> right, string rightName)
        {
            var problems = new List<string>();

            foreach (var key in right.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!left.TryGetValue(key, out var l)) { problems.Add($"{key}: missing from {leftName}"); continue; }
                var r = right[key];

                if (l.SequenceEqual(r, StringComparer.Ordinal)) continue;

                var at = 0;
                while (at < l.Count && at < r.Count && l[at] == r[at]) at++;
                problems.Add(
                    $"{key}: diverges at operation {at} — {leftName} has " +
                    $"{(at < l.Count ? l[at] : "<end>")}, {rightName} has {(at < r.Count ? r[at] : "<end>")} " +
                    $"({leftName}: {l.Count} ops, {rightName}: {r.Count})");
            }

            return problems;
        }
    }
}
