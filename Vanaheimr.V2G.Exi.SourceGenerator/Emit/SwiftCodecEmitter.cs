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

            /// <summary>Types some other type extends — they must stay subclassable.</summary>
            private HashSet<string> _baseNames = new(StringComparer.Ordinal);

            private int _run;

            public IReadOnlyList<GeneratedFile> Run()
            {
                Reject(plan);

                _baseNames = new HashSet<string>(
                    plan.ComplexTypes.Values.Select(s => s.BaseRecordName).Where(n => n is not null)!,
                    StringComparer.Ordinal);

                var files = new List<GeneratedFile>();

                foreach (var e in plan.Enums)
                    files.Add(Standalone(e.Name, () => EmitEnum(e)));

                foreach (var t in plan.OpaqueTypes)
                    files.Add(Standalone(t, () => EmitOpaque(t)));

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

                if (p.Fragments.Count > 0) No("EXI fragment codecs (--fragments)");

                foreach (var (name, sp) in p.ComplexTypes.Select(kv => (kv.Key, kv.Value))
                                            .Concat(p.GlobalElements.Select(g => (g.TypeName, g.Body))))
                {
                    // Optional attributes ride the content run, which an xs:choice does not have,
                    // and a simpleContent type's run is over its attributes rather than its children.
                    // A required attribute is written before the content and is unaffected; an
                    // optional one would have to ride a content run, which a choice does not have.
                    if (sp.IsChoice && sp.Attributes is { Count: > 0 } && RequiredAttr(sp) is null)
                        No($"optional attributes on the xs:choice type '{name}'");
                    // As above: a required attribute precedes the content, an optional one would
                    // need a run over the attributes that this back end does not build yet.
                    if (sp.SimpleContent is not null && sp.Attributes is { Count: > 0 } && RequiredAttr(sp) is null)
                        No($"optional attributes on the xs:simpleContent type '{name}'");

                    if (sp.Attributes is { Count: > 0 })
                    {
                        // A base type's attributes are not flattened into the derived plan, so a
                        // derived type could not pass them on.
                        if (sp.BaseRecordName is not null &&
                            p.ComplexTypes.TryGetValue(sp.BaseRecordName, out var basePlan) &&
                            basePlan.Attributes is { Count: > 0 })
                            No($"attributes on both '{name}' and its base '{sp.BaseRecordName}'");

                        foreach (var a in sp.Attributes)
                        {
                            if (a.Value is not ValueEncoding.StringValue)
                                No($"non-string attribute '{name}.{a.FieldName}' ({a.Value.GetType().Name})");
                            // A required attribute is written before the content; an optional one
                            // rides in the content run. Mixing the two shapes is not modelled.
                            if (a.Required && sp.Attributes.Count != 1)
                                No($"a required attribute alongside others on '{name}'");
                        }
                    }

                    foreach (var c in sp.Children)
                    {
                        if (c.IsWildcardAny)                             No($"xs:any wildcard '{name}.{c.FieldName}'");
                        // A substitution group contributes one production per member, which widens
                        // every enclosing run. Only the required-single shape is modelled so far, so
                        // the run machinery still sees one production per particle.
                        if (c.Value is ValueEncoding.SubstitutionChoice && c.Shape == ChildShape.BoundedRepeating)
                            No($"a repeating substitution group ('{name}.{c.FieldName}')");
                        if (c.Value is ValueEncoding.InlineChoice)       No($"inline choice '{name}.{c.FieldName}'");
                        // An opaque child is modelled as absent-only, so it must be optional:
                        // a required one could never be encoded at all.
                        if (c.Value is ValueEncoding.OpaqueElement && c.Shape != ChildShape.OptionalSingle)
                            No($"a {c.Shape} opaque element ('{name}.{c.FieldName}')");
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

            // ── attributes ───────────────────────────────────────────────────────────────────────

            /// <summary>
            /// The field carrying an xs:simpleContent value. Matches the other back ends' name, so
            /// all three expose the same shape.
            /// </summary>
            private const string SimpleContentField = "value";

            /// <summary>A synthetic child standing in for the simpleContent value.</summary>
            private static ChildPlan SimpleContentChild(SequencePlan sp) =>
                new(SimpleContentField, sp.SimpleContentType!, IsValueType: false,
                    ChildShape.RequiredSingle, sp.SimpleContent!);

            /// <summary>The lone required attribute of a type, or null.</summary>
            private static AttrPlan? RequiredAttr(SequencePlan sp) =>
                sp.Attributes is { Count: 1 } && sp.Attributes[0].Required ? sp.Attributes[0] : null;

            /// <summary>
            /// Optional attributes are the leading optionals of the content run: the AT event is the
            /// first production of the content's initial grammar state, and when the attribute is
            /// absent that same code doubles as the first SE. Prepending them lets the ordinary run
            /// machinery handle them, exactly as the other two back ends do.
            /// </summary>
            private static IReadOnlyList<ChildPlan> WithOptionalAttributes(SequencePlan sp)
            {
                if (sp.Attributes is null or { Count: 0 } || RequiredAttr(sp) is not null)
                    return sp.Children;

                var list = new List<ChildPlan>(sp.Attributes.Count + sp.Children.Count);
                foreach (var a in sp.Attributes)
                    list.Add(new ChildPlan(a.FieldName, a.Type, IsValueType: false,
                                           ChildShape.OptionalSingle, new ValueEncoding.AttributeValue()));
                list.AddRange(sp.Children);
                return list;
            }

            /// <summary>
            /// Whether a child's value is framed by a value-start bit and a child EE. An AT value is
            /// not — the run's event code *was* the AT event — and a complex child frames itself,
            /// writing its own element EE.
            /// </summary>
            private static bool WrapsValue(ChildPlan c) =>
                c.Value is not ValueEncoding.ComplexRef
                       and not ValueEncoding.AttributeValue
                       and not ValueEncoding.OpaqueElement;

            /// <summary>Declared fields in wire order: attributes precede content.</summary>
            private static IReadOnlyList<(string Name, string Type)> Fields(SequencePlan sp)
            {
                var fields = new List<(string, string)>();
                if (sp.Attributes is not null)
                    foreach (var a in sp.Attributes)
                        fields.Add((Prop(a.FieldName), Type(a.Type) + (a.Required ? "" : "?")));
                if (sp.SimpleContent is not null)
                    fields.Add((SimpleContentField, Type(sp.SimpleContentType!)));
                foreach (var c in sp.Children)
                    fields.Add((Prop(c.FieldName), DeclType(c)));
                return fields;
            }

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

            /// <summary>
            /// A stand-in for an XMLDSig element the generator does not model. It exists so the
            /// surrounding type can name it and leave it absent; a present one fails loud on both
            /// sides rather than being written as something plausible.
            /// </summary>
            private void EmitOpaque(string t)
            {
                _sb.Append("/// Opaque placeholder for the un-modelled XMLDSig element `").Append(t).AppendLine("`.");
                _sb.AppendLine("///");
                _sb.AppendLine("/// Only ever encoded or decoded as absent; a present instance fails loud.");
                _sb.Append("public struct ").Append(t).AppendLine(": Equatable, Sendable {");
                _sb.AppendLine("    public init() {}");
                _sb.AppendLine("}");
            }

            private void EmitType(SequencePlan sp, string name)
            {
                if (!_emitted.Add(name)) return;

                Target(_decl, name);
                EmitStruct(sp, name);

                // An abstract type is never on the wire itself — only its members are, each through
                // its own codec — so emitting one for it would be dead code that still has to be
                // kept correct.
                if (sp.IsAbstract) return;

                Target(_code, name);
                EmitEncode(sp, name);
                EmitDecode(sp, name);
            }

            /// <summary>
            /// A type outside any hierarchy is a <c>struct</c> — value semantics, synthesised
            /// <c>Equatable</c>. One that takes part in a hierarchy is a <c>class</c>, because Swift
            /// structs cannot inherit. That split mirrors Kotlin's own (<c>data class</c> for
            /// standalone types, plain <c>class</c> for hierarchy members), and it means a schema
            /// that adds a base to a type also changes that type's Swift kind — unavoidable, and
            /// visible rather than silent.
            /// </summary>
            /// <remarks>
            /// Inheritance carries no wire meaning of its own: <c>SequencePlan.Children</c> of a
            /// derived type already begins with the base's children, flattened, and the encoder
            /// walks all of them. The base relationship survives only so a field typed as the base
            /// can hold any member — the substitution case.
            /// </remarks>
            private void EmitStruct(SequencePlan sp, string name)
            {
                var isBase   = _baseNames.Contains(name);
                var isMember = sp.IsAbstract || isBase || sp.BaseRecordName is not null;

                var baseChildren = sp.BaseRecordName is not null &&
                                   plan.ComplexTypes.TryGetValue(sp.BaseRecordName, out var basePlan)
                                       ? basePlan.Children.Count
                                       : 0;

                var own = sp.Children.Skip(baseChildren).ToList();

                if (!isMember)
                {
                    _sb.Append("public struct ").Append(name).AppendLine(": Equatable, Sendable {");
                }
                else
                {
                    // No `final` on a base: Swift needs it open to be subclassed. Everything else is
                    // final, which is both faster and a statement that the set of members is closed
                    // to this file — the closest this shape gets to the guard Kotlin had to add.
                    _sb.Append(sp.IsAbstract || isBase ? "public class " : "public final class ").Append(name);
                    _sb.Append(sp.BaseRecordName is not null ? ": " + sp.BaseRecordName : "").AppendLine(" {");
                }

                // Attributes are declared before content, matching the AT-before-content order of the
                // grammar and the other two back ends' parameter lists. They never come from a base:
                // Reject() bars a derived type and its base from both carrying them.
                var ownFields = new List<(string Name, string Type, bool Optional)>();
                if (sp.Attributes is not null)
                    foreach (var a in sp.Attributes)
                        ownFields.Add((Prop(a.FieldName), Type(a.Type) + (a.Required ? "" : "?"), !a.Required));
                if (sp.SimpleContent is not null)
                    ownFields.Add((SimpleContentField, Type(sp.SimpleContentType!), false));
                foreach (var c in own)
                    ownFields.Add((Prop(c.FieldName), DeclType(c), c.Shape == ChildShape.OptionalSingle));

                foreach (var f in ownFields)
                    _sb.Append("    public var ").Append(f.Name).Append(": ").AppendLine(f.Type);
                if (ownFields.Count > 0) _sb.AppendLine();

                // The initialiser always takes every field, inherited children included, so callers
                // and the generated decoders see one flat parameter list whatever the hierarchy does.
                _sb.Append("    public init(");
                _sb.Append(string.Join(", ", Fields(sp).Select((f, i) =>
                    f.Name + ": " + f.Type + (f.Type.EndsWith("?") ? " = nil" : ""))));
                _sb.AppendLine(") {");
                foreach (var f in ownFields)
                    _sb.Append("        self.").Append(f.Name).Append(" = ").AppendLine(f.Name);
                if (sp.BaseRecordName is not null)
                    _sb.Append("        super.init(")
                       .Append(string.Join(", ", sp.Children.Take(baseChildren)
                                                   .Select(c => Prop(c.FieldName) + ": " + Prop(c.FieldName))))
                       .AppendLine(")");
                _sb.AppendLine("    }");
                _sb.AppendLine("}");
                _sb.AppendLine();
            }

            // ── encode ───────────────────────────────────────────────────────────────────────────

            private void EmitEncode(SequencePlan sp, string name)
            {
                _sb.Append("internal func encode").Append(name).Append("(_ w: BitWriter, _ msg: ")
                   .Append(name).AppendLine(") {");

                // A lone required attribute is unconditional: a 1-bit AT event, then a bare value.
                if (RequiredAttr(sp) is { } req)
                {
                    _sb.AppendLine("    w.writeBits(0, 1)   // AT(required attribute)");
                    _sb.Append("    ExiPrimitives.writeStringValue(w, msg.").Append(Prop(req.FieldName)).AppendLine(")");
                }

                var kids = WithOptionalAttributes(sp);

                // A lone repeating child owns the whole element: its terminator doubles as the EE.
                if (kids.Count == 1 && kids[0].Shape == ChildShape.BoundedRepeating)
                {
                    EmitEncodeList(kids[0], sp);
                    _sb.AppendLine("}");
                    _sb.AppendLine();
                    return;
                }

                if (sp.SimpleContent is not null)
                {
                    // A simple-content element has no child elements: one CONTENT event, the value
                    // bare, then the element's own end.
                    _sb.AppendLine("    w.writeBits(0, 1)   // CONTENT event");
                    EmitWriteValue(SimpleContentChild(sp), "msg." + SimpleContentField, "    ");
                    _sb.AppendLine("    w.writeBits(0, 1)   // element EE");
                    _sb.AppendLine("}");
                    _sb.AppendLine();
                    return;
                }

                if (sp.IsChoice)
                {
                    EmitEncodeChoice(sp, name);
                    _sb.AppendLine("    w.writeBits(0, 1)   // element EE");
                    _sb.AppendLine("}");
                    _sb.AppendLine();
                    return;
                }

                var closed = false;   // whether a run has already written the element EE
                for (var i = 0; i < kids.Count;)
                {
                    var c = kids[i];

                    if (c.Shape == ChildShape.RequiredSingle)
                    {
                        // A substitution reference has no SE of its own: the member's event code is
                        // the start event, so writing one first would insert a bit nothing reads.
                        if (c.Value is ValueEncoding.SubstitutionChoice sub)
                            EmitEncodeSubstitution(c, sub, "    ");
                        else
                        {
                            _sb.AppendLine("    w.writeBits(0, 1)   // SE");
                            EmitWriteFramedValue(c, "msg." + Prop(c.FieldName), "    ");
                        }
                        i++;
                        continue;
                    }

                    if (c.Shape == ChildShape.OptionalSingle)
                    {
                        var end = i;
                        while (end < kids.Count && kids[end].Shape == ChildShape.OptionalSingle) end++;

                        // A run ends either at the element EE or at the next required particle,
                        // whose start event shares the run's highest code.
                        var term = end < kids.Count ? kids[end] : null;
                        if (term is not null && term.Shape != ChildShape.RequiredSingle)
                            throw new NotSupportedException(
                                $"Swift back end: the optional run in '{name}' is terminated by " +
                                $"'{term.FieldName}', whose shape {term.Shape} this back end does not model yet.");

                        EmitEncodeOptionalRun(kids, i, end, term);
                        closed = term is null;
                        i = end + (term is null ? 0 : 1);
                        continue;
                    }

                    throw new NotSupportedException(
                        $"Swift back end: '{name}.{c.FieldName}' has shape {c.Shape} in a mixed sequence, " +
                        "which this back end does not model yet.");
                }

                if (!closed)
                    _sb.AppendLine("    w.writeBits(0, 1)   // element EE");

                _sb.AppendLine("}");
                _sb.AppendLine();
            }

            /// <summary>
            /// A child's value with its framing: a simple value sits between a value-start bit and a
            /// child EE, while an AT value and a nested complex type have neither — see
            /// <see cref="WrapsValue"/>.
            /// </summary>
            private void EmitWriteFramedValue(ChildPlan c, string expr, string ind)
            {
                if (!WrapsValue(c))
                {
                    EmitWriteValue(c, expr, ind);
                    return;
                }
                _sb.Append(ind).AppendLine("w.writeBits(0, 1)   // value-start");
                EmitWriteValue(c, expr, ind);
                _sb.Append(ind).AppendLine("w.writeBits(0, 1)   // child EE");
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
            private void EmitEncodeOptionalRun(IReadOnlyList<ChildPlan> kids, int start, int end, ChildPlan? term)
            {
                var m  = end - start;
                var id = _run++;
                const string ind = "            ";

                _sb.Append("    var st").Append(id).AppendLine(" = 0");
                _sb.Append("    var done").Append(id).AppendLine(" = false");
                _sb.Append("    while !done").Append(id).AppendLine(" {");
                _sb.Append("        switch st").Append(id).AppendLine(" {");

                for (var k = 0; k <= m; k++)
                {
                    // State k: the cursor sits at particle start+k, so every particle from there on
                    // is still possible and each contributes its own productions — plus the element
                    // EE (or the terminator), plus the non-strict phantom.
                    var totalProd = term is null ? 1 : ProductionCount(term);
                    for (var j = k; j < m; j++) totalProd += ProductionCount(kids[start + j]);
                    var width = BitsFor(totalProd + 1);

                    _sb.Append("        case ").Append(k).AppendLine(":");

                    var code  = 0;
                    var first = true;
                    for (var j = k; j < m; j++)
                        code = EmitEncodeRunParticle(kids[start + j], code, width, ref first, ind,
                                                     $"st{id} = {j + 1}");

                    var tail = first ? ind : ind + "    ";
                    if (!first) _sb.Append(ind).AppendLine("} else {");

                    _sb.Append(tail).Append("w.writeBits(").Append(code).Append(", ").Append(width);
                    if (term is null)
                    {
                        _sb.AppendLine(")   // element EE");
                    }
                    else
                    {
                        _sb.Append(")   // SE(").Append(term.FieldName).AppendLine(")");
                        EmitWriteFramedValue(term, "msg." + Prop(term.FieldName), tail);
                    }
                    _sb.Append(tail).Append("done").Append(id).AppendLine(" = true");

                    if (!first) _sb.Append(ind).AppendLine("}");
                }

                _sb.AppendLine("        default:");
                _sb.Append("            done").Append(id).AppendLine(" = true");
                _sb.AppendLine("        }");
                _sb.AppendLine("    }");
            }

            /// <summary>
            /// One particle of an optional run, as a link in the state's if / else-if chain. Returns
            /// the next free event code — a substitution reference consumes one per member.
            /// </summary>
            private int EmitEncodeRunParticle(ChildPlan p, int code, int width, ref bool first,
                                              string ind, string after)
            {
                var prop = "msg." + Prop(p.FieldName);

                if (p.Value is ValueEncoding.SubstitutionChoice sc)
                {
                    var baseCode = code;
                    var ordered  = sc.Members
                                     .Select((mm, i) => (Member: mm, Wire: baseCode + i))
                                     .Where(x => !x.Member.IsAbstractHead)
                                     .OrderByDescending(x => InheritanceDepth(x.Member.TypeName));

                    foreach (var (mbr, wire) in ordered)
                    {
                        // `as?` unwraps the optional and downcasts in one step; it matches subclasses
                        // too, which is why the branches are ordered most-derived-first.
                        _sb.Append(ind).Append(first ? "if" : "} else if").Append(" let v = ").Append(prop)
                           .Append(" as? ").Append(mbr.TypeName).AppendLine(" {");
                        EmitSubstitutionGuard(p, mbr.TypeName, ind + "    ");
                        _sb.Append(ind).Append("    w.writeBits(").Append(wire).Append(", ").Append(width)
                           .Append(")   // ").AppendLine(mbr.ElementName);
                        _sb.Append(ind).Append("    encode").Append(mbr.TypeName).AppendLine("(w, v)");
                        _sb.Append(ind).Append("    ").AppendLine(after);
                        first = false;
                    }
                    return code + sc.Members.Count;
                }

                _sb.Append(ind).Append(first ? "if" : "} else if").Append(" let v = ").Append(prop).AppendLine(" {");
                _sb.Append(ind).Append("    w.writeBits(").Append(code).Append(", ").Append(width)
                   .Append(")   // ").AppendLine(p.FieldName);
                EmitWriteFramedValue(p, "v", ind + "    ");
                _sb.Append(ind).Append("    ").AppendLine(after);
                first = false;
                return code + 1;
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
                    case ValueEncoding.AttributeValue:
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
                    case ValueEncoding.OpaqueElement oe:
                        // Only reached with a present instance; absence is handled by the run.
                        _sb.Append(ind).Append("preconditionFailure(\"encoding a present ").Append(oe.TypeName)
                           .AppendLine(" (XMLDSig) is not implemented in the Swift back end\")");
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

                if (RequiredAttr(sp) is { } req)
                {
                    _sb.AppendLine("    _ = try r.readBits(1)   // AT(required attribute)");
                    _sb.Append("    let ").Append(Local(req.FieldName))
                       .Append(" = try ExiPrimitives.readStringValue(r, slot: \"").Append(req.FieldName).AppendLine("\")");
                }

                var kids = WithOptionalAttributes(sp);

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

                if (sp.SimpleContent is not null)
                {
                    _sb.AppendLine("    _ = try r.readBits(1)   // CONTENT event");
                    _sb.Append("    let ").Append(SimpleContentField).Append(" = ")
                       .AppendLine(ReadValueExpr(SimpleContentChild(sp)));
                    _sb.AppendLine("    _ = try r.readBits(1)   // element EE");
                    var scArgs = new List<string>();
                    if (RequiredAttr(sp) is { } sra) scArgs.Add(Prop(sra.FieldName) + ": " + Local(sra.FieldName));
                    scArgs.Add(SimpleContentField + ": " + SimpleContentField);
                    _sb.Append("    return ").Append(name).Append("(").Append(string.Join(", ", scArgs)).AppendLine(")");
                    _sb.AppendLine("}");
                    _sb.AppendLine();
                    return;
                }

                if (sp.IsChoice)
                {
                    EmitDecodeChoice(sp, name);
                    _sb.AppendLine("}");
                    _sb.AppendLine();
                    return;
                }

                var closed = false;
                for (var i = 0; i < kids.Count;)
                {
                    var c = kids[i];

                    if (c.Shape == ChildShape.RequiredSingle)
                    {
                        if (c.Value is ValueEncoding.SubstitutionChoice sub)
                            EmitDecodeSubstitution(c, sub, "    ");
                        else
                        {
                            _sb.AppendLine("    _ = try r.readBits(1)   // SE");
                            EmitReadFramedValue(c, "let " + Local(c.FieldName), "    ");
                        }
                        i++;
                        continue;
                    }

                    var end = i;
                    while (end < kids.Count && kids[end].Shape == ChildShape.OptionalSingle) end++;
                    var term = end < kids.Count ? kids[end] : null;

                    EmitDecodeOptionalRun(kids, i, end, term);
                    closed = term is null;
                    i = end + (term is null ? 0 : 1);
                }

                if (!closed)
                    _sb.AppendLine("    _ = try r.readBits(1)   // element EE");

                _sb.Append("    return ").Append(name).Append("(").Append(CtorArgs(sp, kids)).AppendLine(")");
                _sb.AppendLine("}");
                _sb.AppendLine();
            }

            /// <summary>
            /// The decoder's constructor call, in declaration order. Optional attributes are already
            /// at the head of <paramref name="kids"/>; a required one is written before the content
            /// and so has to be put back in front here.
            /// </summary>
            private static string CtorArgs(SequencePlan sp, IReadOnlyList<ChildPlan> kids)
            {
                var args = new List<string>();
                if (RequiredAttr(sp) is { } req)
                    args.Add(Prop(req.FieldName) + ": " + Local(req.FieldName));
                args.AddRange(kids.Select(c => Prop(c.FieldName) + ": " + Local(c.FieldName)));
                return string.Join(", ", args);
            }

            private void EmitDecodeOptionalRun(IReadOnlyList<ChildPlan> kids, int start, int end, ChildPlan? term)
            {
                var m  = end - start;
                var id = _run++;

                for (var j = start; j < end; j++)
                    _sb.Append("    var ").Append(Local(kids[j].FieldName)).Append(": ")
                       .Append(DeclType(kids[j])).AppendLine(" = nil");

                // A required terminator is assigned inside the state machine, so it starts as an
                // optional and is unwrapped once the run is over: a stream that closes the element
                // without it is malformed, not a programming error.
                if (term is not null)
                    _sb.Append("    var ").Append(Local(term.FieldName)).Append(": ")
                       .Append(Type(term.Type)).AppendLine("? = nil");

                _sb.Append("    var st").Append(id).AppendLine(" = 0");
                _sb.Append("    var done").Append(id).AppendLine(" = false");
                _sb.Append("    while !done").Append(id).AppendLine(" {");
                _sb.Append("        switch st").Append(id).AppendLine(" {");

                for (var k = 0; k <= m; k++)
                {
                    var totalProd = term is null ? 1 : ProductionCount(term);
                    for (var j = k; j < m; j++) totalProd += ProductionCount(kids[start + j]);
                    var width = BitsFor(totalProd + 1);

                    _sb.Append("        case ").Append(k).AppendLine(":");
                    _sb.Append("            switch try r.readBits(").Append(width).AppendLine(") {");

                    var code = 0;
                    for (var j = k; j < m; j++)
                    {
                        var c = kids[start + j];

                        if (c.Value is ValueEncoding.SubstitutionChoice sc)
                        {
                            // One case per member, at consecutive codes; an abstract head reserves
                            // its slot without a branch and so falls through to the throw.
                            for (var mi = 0; mi < sc.Members.Count; mi++)
                            {
                                if (sc.Members[mi].IsAbstractHead) continue;
                                _sb.Append("            case ").Append(code + mi).Append(":   // ")
                                   .AppendLine(sc.Members[mi].ElementName);
                                _sb.Append("                ").Append(Local(c.FieldName)).Append(" = try decode")
                                   .Append(sc.Members[mi].TypeName).AppendLine("(r)");
                                _sb.Append("                st").Append(id).Append(" = ").Append(j + 1).AppendLine();
                            }
                            code += sc.Members.Count;
                            continue;
                        }

                        _sb.Append("            case ").Append(code).Append(":   // ").AppendLine(c.FieldName);
                        EmitReadFramedValue(c, Local(c.FieldName), "                ");
                        _sb.Append("                st").Append(id).Append(" = ").Append(j + 1).AppendLine();
                        code++;
                    }

                    if (term is null)
                    {
                        _sb.Append("            case ").Append(code).AppendLine(":   // element EE");
                    }
                    else
                    {
                        _sb.Append("            case ").Append(code).Append(":   // SE(").Append(term.FieldName).AppendLine(")");
                        EmitReadFramedValue(term, Local(term.FieldName), "                ");
                    }
                    _sb.Append("                done").Append(id).AppendLine(" = true");
                    _sb.AppendLine("            default:");
                    _sb.AppendLine("                throw ExiError.invalidEventCode(\"optional run\")");
                    _sb.AppendLine("            }");
                }

                _sb.AppendLine("        default:");
                _sb.Append("            done").Append(id).AppendLine(" = true");
                _sb.AppendLine("        }");
                _sb.AppendLine("    }");

                if (term is not null)
                {
                    _sb.Append("    guard let ").Append(Local(term.FieldName)).Append(" else {").AppendLine();
                    _sb.Append("        throw ExiError.invalidEventCode(\"").Append(term.FieldName)
                       .AppendLine(" is required but the element ended\")");
                    _sb.AppendLine("    }");
                }
            }

            /// <summary>
            /// Reads a child's value into <paramref name="target"/> (`let _x` or a plain `_x`),
            /// surrounded by the framing bits its encoding calls for — see <see cref="WrapsValue"/>.
            /// </summary>
            private void EmitReadFramedValue(ChildPlan c, string target, string ind)
            {
                if (WrapsValue(c))
                    _sb.Append(ind).AppendLine("_ = try r.readBits(1)   // value-start");

                _sb.Append(ind).Append(target).Append(" = ").AppendLine(ReadValueExpr(c));

                if (WrapsValue(c))
                    _sb.Append(ind).AppendLine("_ = try r.readBits(1)   // child EE");
            }

            private static string ReadValueExpr(ChildPlan c) => c.Value switch
            {
                ValueEncoding.UnsignedInt  => Convert(c, "try ExiPrimitives.readUnsignedInteger(r)"),
                ValueEncoding.SignedInt    => Convert(c, "try ExiPrimitives.readSignedInteger(r)"),
                ValueEncoding.StringValue or ValueEncoding.AttributeValue
                                           => $"try ExiPrimitives.readStringValue(r, slot: \"{c.FieldName}\")",
                ValueEncoding.Binary       => "try ExiPrimitives.readBinary(r)",
                ValueEncoding.NBitUnsigned nb => nb.Bias == 0
                                                    ? Convert(c, $"try r.readBits({nb.BitWidth})")
                                                    : Convert(c, $"Int64(try r.readBits({nb.BitWidth})) + {nb.Bias}"),
                // The index read stays inline — see ExiRuntime.exiEnum. A generated wrapper would
                // put a call where the other back ends have the read itself, which the
                // cross-emitter comparison reads as a divergence, correctly.
                ValueEncoding.EnumIndex ei => $"try exiEnum({ei.EnumName}.self, try r.readBits({ei.BitWidth}))",
                ValueEncoding.ComplexRef cr  => $"try decode{cr.TypeName}(r)",
                ValueEncoding.OpaqueElement oe =>
                    $"{{ throw ExiError.unsupportedConstruct(\"{oe.TypeName} (XMLDSig)\") }}()",
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

            // ── xs:choice ────────────────────────────────────────────────────────────────────────

            /// <summary>
            /// Mutually exclusive alternatives, selected by one event code over all of them plus the
            /// non-strict phantom. Exactly one must be set — an empty choice has nothing to write.
            /// </summary>
            private void EmitEncodeChoice(SequencePlan sp, string name)
            {
                if (sp.Children.Count == 0)
                    throw new NotSupportedException($"Swift back end: '{name}' is an empty xs:choice.");

                var width = BitsFor(sp.Children.Count + 1);

                for (var i = 0; i < sp.Children.Count; i++)
                {
                    var c = sp.Children[i];
                    _sb.Append("    ").Append(i == 0 ? "if" : "} else if").Append(" let v = msg.")
                       .Append(Prop(c.FieldName)).AppendLine(" {");
                    _sb.Append("        w.writeBits(").Append(i).Append(", ").Append(width)
                       .Append(")   // ").AppendLine(c.FieldName);
                    EmitWriteFramedValue(c, "v", "        ");
                }

                _sb.AppendLine("    } else {");
                _sb.Append("        preconditionFailure(\"no choice alternative set for ").Append(name).AppendLine("\")");
                _sb.AppendLine("    }");
            }

            private void EmitDecodeChoice(SequencePlan sp, string name)
            {
                var width = BitsFor(sp.Children.Count + 1);

                foreach (var c in sp.Children)
                    _sb.Append("    var ").Append(Local(c.FieldName)).Append(": ")
                       .Append(DeclType(c)).AppendLine(" = nil");

                _sb.Append("    switch try r.readBits(").Append(width).AppendLine(") {");
                for (var i = 0; i < sp.Children.Count; i++)
                {
                    _sb.Append("    case ").Append(i).Append(":   // ").AppendLine(sp.Children[i].FieldName);
                    EmitReadFramedValue(sp.Children[i], Local(sp.Children[i].FieldName), "        ");
                }
                _sb.AppendLine("    default:");
                _sb.Append("        throw ExiError.invalidEventCode(\"").Append(name).AppendLine(" choice\")");
                _sb.AppendLine("    }");

                // The encoder closes the element after the alternative; the decoder must consume it.
                _sb.AppendLine("    _ = try r.readBits(1)   // element EE");

                var args = new List<string>();
                if (RequiredAttr(sp) is { } ra) args.Add(Prop(ra.FieldName) + ": " + Local(ra.FieldName));
                args.AddRange(sp.Children.Select(c => Prop(c.FieldName) + ": " + Local(c.FieldName)));
                _sb.Append("    return ").Append(name).Append("(").Append(string.Join(", ", args)).AppendLine(")");
            }

            // ── substitution groups ──────────────────────────────────────────────────────────────

            /// <summary>
            /// Dispatches on the runtime type of a substitution reference. Branches go
            /// most-derived-first, because Swift's `as` pattern matches subclasses too, while each
            /// member keeps the event code of its own position in the group.
            /// </summary>
            private void EmitEncodeSubstitution(ChildPlan c, ValueEncoding.SubstitutionChoice sc, string ind)
            {
                var ordered = sc.Members
                                .Select((m, i) => (Member: m, Code: i))
                                .Where(x => !x.Member.IsAbstractHead)
                                .OrderByDescending(x => InheritanceDepth(x.Member.TypeName))
                                .ToList();

                // A concrete head is itself a member and, being the least derived, sorts last — so
                // its branch would test the value against its own declared type, which always
                // succeeds and makes the default behind it unreachable. Emitting it as the default
                // is the same code with the dead arm dropped.
                var headIsLast = ordered.Count > 1 &&
                                 ordered[ordered.Count - 1].Member.TypeName == Type(c.Type);

                _sb.Append(ind).Append("switch msg.").Append(Prop(c.FieldName)).AppendLine(" {");

                for (var i = 0; i < ordered.Count; i++)
                {
                    var (m, code) = ordered[i];
                    var last = i == ordered.Count - 1;

                    if (headIsLast && last)
                    {
                        _sb.Append(ind).AppendLine("default:");
                        _sb.Append(ind).Append("    let v = msg.").AppendLine(Prop(c.FieldName));
                    }
                    else
                    {
                        _sb.Append(ind).Append("case let v as ").Append(m.TypeName).AppendLine(":");
                    }

                    EmitSubstitutionGuard(c, m.TypeName, ind + "    ");
                    _sb.Append(ind).Append("    w.writeBits(").Append(code).Append(", ").Append(sc.BitWidth)
                       .Append(")   // ").AppendLine(m.ElementName);
                    _sb.Append(ind).Append("    encode").Append(m.TypeName).AppendLine("(w, v)");
                }

                if (!headIsLast)
                {
                    _sb.Append(ind).AppendLine("default:");
                    _sb.Append(ind).Append("    preconditionFailure(\"unsupported substitution member for ")
                       .Append(c.FieldName).AppendLine("\")");
                }

                _sb.Append(ind).AppendLine("}");
            }

            /// <summary>
            /// Requires the value to be *exactly* the member type its branch selected, not merely a
            /// subclass of it.
            /// </summary>
            /// <remarks>
            /// The `as` pattern matches subclasses, which is why the branches are ordered
            /// most-derived-first at all. Within the generated types that ordering partitions the
            /// space exactly, since every derived type is itself a member — but nothing stops
            /// application code subclassing one, and the types something extends are deliberately
            /// left subclassable. Such a value would take its nearest ancestor's branch and go out
            /// with that member's event code and encoder, quietly encoding something else.
            /// A leaf is `final`, so there `as` already means "exactly this" and the check is dead.
            /// </remarks>
            private void EmitSubstitutionGuard(ChildPlan c, string typeName, string ind)
            {
                var extensible = _baseNames.Contains(typeName) ||
                                 (plan.ComplexTypes.TryGetValue(typeName, out var sp) && sp.IsAbstract);
                if (!extensible) return;

                _sb.Append(ind).Append("precondition(type(of: v) == ").Append(typeName)
                   .Append(".self, \"").Append(c.FieldName)
                   .AppendLine(": a subclass of a substitution member is not itself one\")");
            }

            private void EmitDecodeSubstitution(ChildPlan c, ValueEncoding.SubstitutionChoice sc, string ind)
            {
                _sb.Append(ind).Append("let ").Append(Local(c.FieldName)).Append(": ").AppendLine(Type(c.Type));
                _sb.Append(ind).Append("switch try r.readBits(").Append(sc.BitWidth).AppendLine(") {");

                for (var i = 0; i < sc.Members.Count; i++)
                {
                    // An abstract head reserves a code slot without being encodable, so it has no
                    // branch here either and falls through to the throw.
                    if (sc.Members[i].IsAbstractHead) continue;

                    _sb.Append(ind).Append("case ").Append(i).Append(":   // ").AppendLine(sc.Members[i].ElementName);
                    _sb.Append(ind).Append("    ").Append(Local(c.FieldName)).Append(" = try decode")
                       .Append(sc.Members[i].TypeName).AppendLine("(r)");
                }

                _sb.Append(ind).AppendLine("default:");
                _sb.Append(ind).Append("    throw ExiError.invalidEventCode(\"").Append(c.FieldName)
                   .AppendLine(" substitution\")");
                _sb.Append(ind).AppendLine("}");
            }

            /// <summary>How many base links separate a type from its root; 0 for a type with no base.</summary>
            private int InheritanceDepth(string typeName)
            {
                var depth = 0;
                var name  = typeName;
                while (plan.ComplexTypes.TryGetValue(name, out var sp) && sp.BaseRecordName is not null)
                {
                    depth++;
                    name = sp.BaseRecordName;
                    if (depth > 32) break;   // a cycle cannot happen in a valid schema; do not hang on one
                }
                return depth;
            }

            /// <summary>
            /// How many event codes a particle occupies. A substitution group takes one per member,
            /// so it widens every run it sits in — getting this wrong shifts every following bit
            /// while leaving the shape of the generated code entirely plausible.
            /// </summary>
            private static int ProductionCount(ChildPlan c) =>
                c.Value is ValueEncoding.SubstitutionChoice sc ? sc.Members.Count
                : c.Value is ValueEncoding.InlineChoice ic     ? ic.Members.Count
                : c.IsWildcardAny                              ? 2
                : 1;

            private static int BitsFor(int n)
            {
                var bits = 0;
                while ((1 << bits) < n) bits++;
                return bits;
            }
        }
    }
}
