using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Vanaheimr.V2G.Exi.SourceGenerator.Grammar;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Emit
{
    /// <summary>
    /// Swift back end. Emits one file per type, plus one for the codec enum — mandatory here rather
    /// than merely preferable: the -20 sets are tens of thousands of lines and Swift's type checker
    /// degrades sharply on single files of that size (see <c>docs/CONCEPT.md</c> §3.7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three shapes differ from the Kotlin back end, none of them changing a byte:
    /// </para>
    /// <list type="bullet">
    /// <item>Records become <c>struct</c>s, so messages are values; the codec object becomes an
    /// <c>enum</c> with static members, Swift's idiom for a namespace that cannot be instantiated.</item>
    /// <item>Every decoder is <c>throws</c>. Swift has no unchecked exceptions, so the distinction
    /// the other back ends get for free is carried in the signatures — encoders stay non-throwing
    /// because they are driven by our own values (see <c>ExiRuntime.ExiError</c>).</item>
    /// <item><c>targetNamespace</c> has nowhere to go: a Swift module is defined by its SwiftPM
    /// target, not declared in source. It is recorded in the file header for traceability.</item>
    /// </list>
    /// <para>
    /// Coverage is deliberately partial — see <see cref="Writer.Reject"/>. Everything this back end
    /// does not model yet fails loudly rather than emitting something plausible, matching the
    /// generator's fail-loud rule in <c>CLAUDE.md</c>.
    /// </para>
    /// </remarks>
    internal sealed class SwiftCodecEmitter : ICodecEmitter
    {
        public static readonly SwiftCodecEmitter Instance = new();

        public string Language      => "swift";
        public string FileExtension => ".swift";

        public IReadOnlyList<GeneratedFile> Emit(SchemaPlan plan, string targetNamespace, string codecClassName) =>
            new Writer(plan, targetNamespace, codecClassName).Run();

        private sealed class Writer(SchemaPlan plan, string moduleName, string codecEnum)
        {
            private StringBuilder _sb = new();
            private readonly List<string> _order = new();
            private readonly Dictionary<string, StringBuilder> _decl = new(StringComparer.Ordinal);
            private readonly Dictionary<string, StringBuilder> _code = new(StringComparer.Ordinal);
            private readonly HashSet<string> _fileNames = new(StringComparer.Ordinal);

            /// <summary>
            /// Types already emitted. A global element's body is usually also present in
            /// <see cref="SchemaPlan.ComplexTypes"/>, so without this the same struct, encoder and
            /// decoder land in the file twice — which Swift rejects as a redeclaration, but only
            /// after the emitter has silently produced it.
            /// </summary>
            private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);

            private int _run;

            public IReadOnlyList<GeneratedFile> Run()
            {
                Reject(plan);

                var files = new List<GeneratedFile>();

                foreach (var e in plan.Enums)
                    files.Add(Standalone(e.Name, () => EmitEnum(e)));

                foreach (var name in plan.ComplexTypes.Keys)
                    EmitType(plan.ComplexTypes[name], name);

                foreach (var ge in plan.GlobalElements)
                    EmitType(ge.Body, ge.TypeName);

                foreach (var name in _order)
                {
                    var body = new StringBuilder(_decl[name].ToString());
                    if (_code.TryGetValue(name, out var codec)) body.Append(codec);
                    files.Add(File(name, body.ToString()));
                }

                files.Add(Standalone(codecEnum, EmitFacade));
                return files;
            }

            /// <summary>
            /// Fail loud on every construct this back end does not model yet. The slice it does
            /// model is the one AppProtocol needs; the -2 and -20 sets add attributes, choices,
            /// substitution groups, simple content and fragments, and each will land with its own
            /// vectors rather than being guessed at now.
            /// </summary>
            private static void Reject(SchemaPlan p)
            {
                void No(string what) => throw new NotSupportedException(
                    "Swift back end: " + what + " is not modelled yet. The back end covers the " +
                    "AppProtocol slice; extend it deliberately, against that construct's vectors.");

                if (p.Fragments.Count   > 0) No("EXI fragment codecs (--fragments)");
                if (p.OpaqueTypes.Count > 0) No("opaque element types");

                foreach (var (name, sp) in p.ComplexTypes.Select(kv => (kv.Key, kv.Value))
                                            .Concat(p.GlobalElements.Select(g => (g.TypeName, g.Body))))
                {
                    if (sp.IsAbstract)                    No($"abstract type '{name}'");
                    if (sp.BaseRecordName is not null)    No($"type extension ('{name}' extends '{sp.BaseRecordName}')");
                    if (sp.IsChoice)                      No($"xs:choice type '{name}'");
                    if (sp.SimpleContent is not null)     No($"xs:simpleContent type '{name}'");
                    if (sp.Attributes is { Count: > 0 })  No($"attributes on '{name}'");

                    foreach (var c in sp.Children)
                    {
                        if (c.IsWildcardAny)                             No($"xs:any wildcard '{name}.{c.FieldName}'");
                        if (c.Value is ValueEncoding.SubstitutionChoice) No($"substitution group '{name}.{c.FieldName}'");
                        if (c.Value is ValueEncoding.InlineChoice)       No($"inline choice '{name}.{c.FieldName}'");
                        if (c.Value is ValueEncoding.OpaqueElement)      No($"opaque element '{name}.{c.FieldName}'");
                    }
                }
            }

            // ── file assembly ────────────────────────────────────────────────────────────────────

            private GeneratedFile Standalone(string name, Action emit)
            {
                _sb = new StringBuilder();
                emit();
                return File(name, _sb.ToString());
            }

            private GeneratedFile File(string name, string body)
            {
                var sb = new StringBuilder();
                sb.AppendLine("// <auto-generated/>");
                sb.AppendLine("// Generated by Vanaheimr.V2G.Exi.SourceGenerator (Swift back end). Do not edit by hand.");
                sb.Append("// Schema target namespace: ").AppendLine(moduleName);
                sb.AppendLine();
                if (body.Contains("BitReader") || body.Contains("BitWriter") || body.Contains("ExiPrimitives") ||
                    body.Contains("ExiError"))
                    sb.AppendLine("import ExiRuntime").AppendLine();

                sb.AppendLine(body.TrimEnd('\r', '\n'));

                if (!_fileNames.Add(name))
                    throw new NotSupportedException(
                        $"Swift back end: two declarations are both called '{name}', so they would share " +
                        "a file. One file per type only works while type names are unique.");

                return new GeneratedFile(name + ".swift", sb.ToString());
            }

            private void Target(Dictionary<string, StringBuilder> which, string name)
            {
                if (!_decl.ContainsKey(name))
                {
                    _order.Add(name);
                    _decl[name] = new StringBuilder();
                }
                if (!which.TryGetValue(name, out var sb)) which[name] = sb = new StringBuilder();
                _sb = sb;
            }

            // ── naming ───────────────────────────────────────────────────────────────────────────

            private static readonly HashSet<string> SwiftKeywords = new(StringComparer.Ordinal)
            {
                "associatedtype", "class", "deinit", "enum", "extension", "fileprivate", "func", "import",
                "init", "inout", "internal", "let", "open", "operator", "private", "precedencegroup",
                "protocol", "public", "rethrows", "static", "struct", "subscript", "typealias", "var",
                "break", "case", "catch", "continue", "default", "defer", "do", "else", "fallthrough",
                "for", "guard", "if", "in", "repeat", "return", "throw", "switch", "where", "while",
                "as", "any", "await", "false", "is", "nil", "self", "Self", "super", "throws", "true", "try",
            };

            private static string Ident(string name) => SwiftKeywords.Contains(name) ? "`" + name + "`" : name;
            private static string Camel(string pascal) =>
                pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
            private static string Prop(string pascal)  => Ident(Camel(pascal));
            private static string Local(string pascal) => "_" + Camel(pascal);

            private static string Type(TypeRef t) => t switch
            {
                TypeRef.Named n     => n.Name,
                TypeRef.Primitive p => p.Kind switch
                {
                    PrimitiveKind.Bool   => "Bool",
                    PrimitiveKind.Int8   => "Int8",
                    PrimitiveKind.Int16  => "Int16",
                    PrimitiveKind.Int32  => "Int32",
                    PrimitiveKind.Int64  => "Int64",
                    PrimitiveKind.UInt8  => "UInt8",
                    PrimitiveKind.UInt16 => "UInt16",
                    PrimitiveKind.UInt32 => "UInt32",
                    PrimitiveKind.UInt64 => "UInt64",
                    PrimitiveKind.String => "String",
                    PrimitiveKind.Binary => "[UInt8]",
                    _ => throw new NotSupportedException($"Swift back end: primitive {p.Kind} is not mapped."),
                },
                _ => throw new NotSupportedException("Swift back end: untyped child."),
            };

            private static string DeclType(ChildPlan c) => c.Shape switch
            {
                ChildShape.RequiredSingle    => Type(c.Type),
                ChildShape.OptionalSingle    => Type(c.Type) + "?",
                ChildShape.BoundedRepeating  => "[" + Type(c.Type) + "]",
                _ => throw new NotSupportedException($"Swift back end: shape {c.Shape}."),
            };

            // ── declarations ─────────────────────────────────────────────────────────────────────

            private void EmitEnum(EnumPlan e)
            {
                // Int-backed so the EXI enumeration index is the raw value in both directions; the
                // members keep their schema spelling rather than Swift's lowerCamelCase convention,
                // because they are wire identifiers shared with the other two back ends.
                _sb.Append("public enum ").Append(e.Name).AppendLine(": Int, CaseIterable, Sendable {");
                foreach (var m in e.Members)
                    _sb.Append("    case ").AppendLine(Ident(m));
                _sb.AppendLine("}");
            }

            private void EmitType(SequencePlan sp, string name)
            {
                if (!_emitted.Add(name)) return;

                Target(_decl, name);
                EmitStruct(sp, name);
                Target(_code, name);
                EmitEncode(sp, name);
                EmitDecode(sp, name);
            }

            private void EmitStruct(SequencePlan sp, string name)
            {
                _sb.Append("public struct ").Append(name).AppendLine(": Equatable, Sendable {");
                foreach (var c in sp.Children)
                    _sb.Append("    public var ").Append(Prop(c.FieldName)).Append(": ").AppendLine(DeclType(c));
                _sb.AppendLine();
                _sb.Append("    public init(");
                _sb.Append(string.Join(", ", sp.Children.Select(c =>
                    Prop(c.FieldName) + ": " + DeclType(c) + (c.Shape == ChildShape.OptionalSingle ? " = nil" : ""))));
                _sb.AppendLine(") {");
                foreach (var c in sp.Children)
                    _sb.Append("        self.").Append(Prop(c.FieldName)).Append(" = ").AppendLine(Prop(c.FieldName));
                _sb.AppendLine("    }");
                _sb.AppendLine("}");
                _sb.AppendLine();
            }

            // ── encode ───────────────────────────────────────────────────────────────────────────

            private void EmitEncode(SequencePlan sp, string name)
            {
                _sb.Append("internal func encode").Append(name).Append("(_ w: BitWriter, _ msg: ")
                   .Append(name).AppendLine(") {");

                var kids = sp.Children;

                // A lone repeating child owns the whole element: its terminator doubles as the EE.
                if (kids.Count == 1 && kids[0].Shape == ChildShape.BoundedRepeating)
                {
                    EmitEncodeList(kids[0], sp);
                    _sb.AppendLine("}");
                    _sb.AppendLine();
                    return;
                }

                var i = 0;
                for (; i < kids.Count && kids[i].Shape == ChildShape.RequiredSingle; i++)
                {
                    _sb.AppendLine("    w.writeBits(0, 1)   // SE");
                    _sb.AppendLine("    w.writeBits(0, 1)   // value-start");
                    EmitWriteValue(kids[i], "msg." + Prop(kids[i].FieldName), "    ");
                    _sb.AppendLine("    w.writeBits(0, 1)   // child EE");
                }

                if (i < kids.Count)
                {
                    if (kids.Skip(i).Any(c => c.Shape != ChildShape.OptionalSingle))
                        throw new NotSupportedException(
                            $"Swift back end: '{name}' mixes shapes in a way this back end does not model yet " +
                            "(only leading required singles followed by an optional run).");
                    EmitEncodeOptionalRun(kids, i);
                }
                else
                {
                    _sb.AppendLine("    w.writeBits(0, 1)   // element EE");
                }

                _sb.AppendLine("}");
                _sb.AppendLine();
            }

            /// <summary>
            /// A repeating child: first item takes a 1-bit SE, every following item and the
            /// terminator a 2-bit event code (item = 0, EE = 1). Mirrors the C# and Kotlin emitters.
            /// </summary>
            private void EmitEncodeList(ChildPlan c, SequencePlan sp)
            {
                var min = sp.ListMin > 0 ? sp.ListMin : Math.Max(1, c.ListMin);
                var max = sp.ListMax > 0 ? sp.ListMax : c.ListMax;

                _sb.Append("    let list = msg.").AppendLine(Prop(c.FieldName));
                _sb.Append("    precondition((").Append(min).Append("...").Append(max)
                   .AppendLine(").contains(list.count), \"list size out of schema range\")");
                _sb.AppendLine("    for (i, item) in list.enumerated() {");
                _sb.AppendLine("        w.writeBits(0, i == 0 ? 1 : 2)   // SE(item)");
                EmitWriteValue(c, "item", "        ");
                _sb.AppendLine("    }");
                _sb.AppendLine("    w.writeBits(1, 2)   // list terminator / element EE");
            }

            /// <summary>
            /// The optional run, as a state machine over particle positions. At state k the cursor
            /// sits at particle <c>start + k</c> and every particle from there on is still possible,
            /// so the selector widens with what remains — plus one production for the element EE and
            /// one for the non-strict phantom. Getting that <c>+1</c> wrong is invisible in a round
            /// trip and shifts every following bit.
            /// </summary>
            private void EmitEncodeOptionalRun(IReadOnlyList<ChildPlan> kids, int start)
            {
                var m  = kids.Count - start;
                var id = _run++;

                _sb.Append("    var st").Append(id).AppendLine(" = 0");
                _sb.Append("    var done").Append(id).AppendLine(" = false");
                _sb.Append("    while !done").Append(id).AppendLine(" {");
                _sb.Append("        switch st").Append(id).AppendLine(" {");

                for (var k = 0; k <= m; k++)
                {
                    var totalProd = 1 + (m - k);                 // element EE + remaining optionals
                    var width     = BitsFor(totalProd + 1);      // + the non-strict phantom

                    _sb.Append("        case ").Append(k).AppendLine(":");

                    var code = 0;
                    for (var j = k; j < m; j++, code++)
                    {
                        var c    = kids[start + j];
                        var prop = "msg." + Prop(c.FieldName);
                        _sb.Append("            if let v = ").Append(prop).AppendLine(" {");
                        _sb.Append("                w.writeBits(").Append(code).Append(", ").Append(width)
                           .Append(")   // ").AppendLine(c.FieldName);
                        _sb.AppendLine("                w.writeBits(0, 1)   // value-start");
                        EmitWriteValue(c, "v", "                ");
                        _sb.AppendLine("                w.writeBits(0, 1)   // child EE");
                        _sb.Append("                st").Append(id).Append(" = ").Append(j + 1).AppendLine();
                        _sb.AppendLine("            } else {");
                    }

                    _sb.Append(new string(' ', 12 + 4 * (m - k))).Append("w.writeBits(").Append(code)
                       .Append(", ").Append(width).AppendLine(")   // element EE");
                    _sb.Append(new string(' ', 12 + 4 * (m - k))).Append("done").Append(id).AppendLine(" = true");

                    for (var j = m - 1; j >= k; j--)
                        _sb.Append(new string(' ', 12 + 4 * (j - k))).AppendLine("}");
                }

                _sb.AppendLine("        default:");
                _sb.Append("            done").Append(id).AppendLine(" = true");
                _sb.AppendLine("        }");
                _sb.AppendLine("    }");
            }

            private void EmitWriteValue(ChildPlan c, string expr, string ind)
            {
                switch (c.Value)
                {
                    case ValueEncoding.UnsignedInt:
                        _sb.Append(ind).Append("ExiPrimitives.writeUnsignedInteger(w, UInt64(").Append(expr).AppendLine("))");
                        break;
                    case ValueEncoding.SignedInt:
                        _sb.Append(ind).Append("ExiPrimitives.writeSignedInteger(w, Int64(").Append(expr).AppendLine("))");
                        break;
                    case ValueEncoding.StringValue:
                        _sb.Append(ind).Append("ExiPrimitives.writeStringValue(w, ").Append(expr).AppendLine(")");
                        break;
                    case ValueEncoding.Binary:
                        _sb.Append(ind).Append("ExiPrimitives.writeBinary(w, ").Append(expr).AppendLine(")");
                        break;
                    case ValueEncoding.NBitUnsigned nb:
                        _sb.Append(ind).Append("w.writeBits(UInt32(")
                           .Append(nb.Bias == 0 ? expr : "Int64(" + expr + ") - " + nb.Bias)
                           .Append("), ").Append(nb.BitWidth).AppendLine(")");
                        break;
                    case ValueEncoding.EnumIndex ei:
                        _sb.Append(ind).Append("w.writeBits(UInt32(").Append(expr).Append(".rawValue), ")
                           .Append(ei.BitWidth).AppendLine(")");
                        break;
                    case ValueEncoding.ComplexRef cr:
                        _sb.Append(ind).Append("encode").Append(cr.TypeName).Append("(w, ").Append(expr).AppendLine(")");
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Swift back end: value encoding {c.Value.GetType().Name} is not modelled yet.");
                }
            }

            // ── decode ───────────────────────────────────────────────────────────────────────────

            private void EmitDecode(SequencePlan sp, string name)
            {
                _sb.Append("internal func decode").Append(name).Append("(_ r: BitReader) throws -> ")
                   .Append(name).AppendLine(" {");

                var kids = sp.Children;

                if (kids.Count == 1 && kids[0].Shape == ChildShape.BoundedRepeating)
                {
                    var c   = kids[0];
                    var max = sp.ListMax > 0 ? sp.ListMax : c.ListMax;
                    _sb.Append("    var list = [").Append(Type(c.Type)).AppendLine("]()");
                    _sb.AppendLine("    _ = try r.readBits(1)   // SE(item) first");
                    _sb.Append("    list.append(").Append(ReadValueExpr(c)).AppendLine(")");
                    _sb.AppendLine("    while true {");
                    _sb.AppendLine("        let ec = try r.readBits(2)");
                    _sb.AppendLine("        if ec == 1 { break }   // element EE");
                    _sb.Append("        guard ec == 0, list.count < ").Append(max).AppendLine(" else {");
                    _sb.AppendLine("            throw ExiError.invalidEventCode(\"repeating element\")");
                    _sb.AppendLine("        }");
                    _sb.Append("        list.append(").Append(ReadValueExpr(c)).AppendLine(")");
                    _sb.AppendLine("    }");
                    _sb.Append("    return ").Append(name).Append("(").Append(Prop(c.FieldName)).AppendLine(": list)");
                    _sb.AppendLine("}");
                    _sb.AppendLine();
                    return;
                }

                var i = 0;
                for (; i < kids.Count && kids[i].Shape == ChildShape.RequiredSingle; i++)
                {
                    _sb.AppendLine("    _ = try r.readBits(1)   // SE");
                    _sb.AppendLine("    _ = try r.readBits(1)   // value-start");
                    _sb.Append("    let ").Append(Local(kids[i].FieldName)).Append(" = ")
                       .AppendLine(ReadValueExpr(kids[i]));
                    _sb.AppendLine("    _ = try r.readBits(1)   // child EE");
                }

                if (i < kids.Count)
                    EmitDecodeOptionalRun(kids, i);
                else
                    _sb.AppendLine("    _ = try r.readBits(1)   // element EE");

                _sb.Append("    return ").Append(name).Append("(")
                   .Append(string.Join(", ", kids.Select(c => Prop(c.FieldName) + ": " + Local(c.FieldName))))
                   .AppendLine(")");
                _sb.AppendLine("}");
                _sb.AppendLine();
            }

            private void EmitDecodeOptionalRun(IReadOnlyList<ChildPlan> kids, int start)
            {
                var m  = kids.Count - start;
                var id = _run++;

                for (var j = start; j < kids.Count; j++)
                    _sb.Append("    var ").Append(Local(kids[j].FieldName)).Append(": ")
                       .Append(DeclType(kids[j])).AppendLine(" = nil");

                _sb.Append("    var st").Append(id).AppendLine(" = 0");
                _sb.Append("    var done").Append(id).AppendLine(" = false");
                _sb.Append("    while !done").Append(id).AppendLine(" {");
                _sb.Append("        switch st").Append(id).AppendLine(" {");

                for (var k = 0; k <= m; k++)
                {
                    var totalProd = 1 + (m - k);
                    var width     = BitsFor(totalProd + 1);

                    _sb.Append("        case ").Append(k).AppendLine(":");
                    _sb.Append("            switch try r.readBits(").Append(width).AppendLine(") {");

                    var code = 0;
                    for (var j = k; j < m; j++, code++)
                    {
                        var c = kids[start + j];
                        _sb.Append("            case ").Append(code).Append(":   // ").AppendLine(c.FieldName);
                        _sb.AppendLine("                _ = try r.readBits(1)   // value-start");
                        _sb.Append("                ").Append(Local(c.FieldName)).Append(" = ")
                           .AppendLine(ReadValueExpr(c));
                        _sb.AppendLine("                _ = try r.readBits(1)   // child EE");
                        _sb.Append("                st").Append(id).Append(" = ").Append(j + 1).AppendLine();
                    }

                    _sb.Append("            case ").Append(code).AppendLine(":   // element EE");
                    _sb.Append("                done").Append(id).AppendLine(" = true");
                    _sb.AppendLine("            default:");
                    _sb.AppendLine("                throw ExiError.invalidEventCode(\"optional run\")");
                    _sb.AppendLine("            }");
                }

                _sb.AppendLine("        default:");
                _sb.Append("            done").Append(id).AppendLine(" = true");
                _sb.AppendLine("        }");
                _sb.AppendLine("    }");
            }

            private static string ReadValueExpr(ChildPlan c) => c.Value switch
            {
                ValueEncoding.UnsignedInt  => Convert(c, "try ExiPrimitives.readUnsignedInteger(r)"),
                ValueEncoding.SignedInt    => Convert(c, "try ExiPrimitives.readSignedInteger(r)"),
                ValueEncoding.StringValue  => $"try ExiPrimitives.readStringValue(r, slot: \"{c.FieldName}\")",
                ValueEncoding.Binary       => "try ExiPrimitives.readBinary(r)",
                ValueEncoding.NBitUnsigned nb => nb.Bias == 0
                                                    ? Convert(c, $"try r.readBits({nb.BitWidth})")
                                                    : Convert(c, $"Int64(try r.readBits({nb.BitWidth})) + {nb.Bias}"),
                // The index read stays inline — see ExiRuntime.exiEnum. A generated wrapper would
                // put a call where the other back ends have the read itself, which the
                // cross-emitter comparison reads as a divergence, correctly.
                ValueEncoding.EnumIndex ei => $"try exiEnum({ei.EnumName}.self, try r.readBits({ei.BitWidth}))",
                ValueEncoding.ComplexRef cr => $"try decode{cr.TypeName}(r)",
                _ => throw new NotSupportedException(
                         $"Swift back end: value encoding {c.Value.GetType().Name} is not modelled yet."),
            };

            /// <summary>Wraps a read in the field's declared type when the two differ.</summary>
            private static string Convert(ChildPlan c, string expr)
            {
                var t = Type(c.Type);
                return t is "String" or "[UInt8]" ? expr : $"{t}({expr})";
            }

            // ── facade ───────────────────────────────────────────────────────────────────────────

            private void EmitFacade()
            {
                _sb.Append("public enum ").Append(codecEnum).AppendLine(" {");
                _sb.AppendLine();
                _sb.AppendLine("    public static let exiHeader: UInt8 = 0x80");
                _sb.AppendLine();

                foreach (var ge in plan.GlobalElements)
                {
                    _sb.Append("    public static func encode(_ msg: ").Append(ge.TypeName).AppendLine(") -> [UInt8] {");
                    _sb.AppendLine("        let w = BitWriter(capacity: 256)");
                    _sb.AppendLine("        w.writeBits(UInt32(exiHeader), 8)");
                    _sb.Append("        w.writeBits(").Append(ge.DocumentIndex).Append(", ")
                       .Append(plan.DocumentSelectorBits).AppendLine(")   // document element selector");
                    _sb.Append("        encode").Append(ge.TypeName).AppendLine("(w, msg)");
                    _sb.AppendLine("        w.alignToByte()");
                    _sb.AppendLine("        return w.bytes");
                    _sb.AppendLine("    }");
                    _sb.AppendLine();
                }

                _sb.AppendLine("    public static func decodeAny(_ src: [UInt8]) throws -> Any {");
                _sb.AppendLine("        guard src.first == exiHeader else { throw ExiError.invalidHeader }");
                _sb.AppendLine("        let r = BitReader(src, offset: 1)");
                _sb.Append("        let sel = try r.readBits(").Append(plan.DocumentSelectorBits).AppendLine(")");
                _sb.AppendLine("        switch sel {");
                foreach (var ge in plan.GlobalElements)
                {
                    _sb.Append("        case ").Append(ge.DocumentIndex).Append(": return try decode")
                       .Append(ge.TypeName).AppendLine("(r)");
                }
                _sb.AppendLine("        default: throw ExiError.unknownDocumentIndex(sel)");
                _sb.AppendLine("        }");
                _sb.AppendLine("    }");
                _sb.AppendLine("}");
            }

            private static int BitsFor(int n)
            {
                var bits = 0;
                while ((1 << bits) < n) bits++;
                return bits;
            }
        }
    }
}
