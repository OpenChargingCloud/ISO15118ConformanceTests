namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar
{
    /// <summary>
    /// The type of a field, named independently of any target language: either an XSD
    /// built-in (<see cref="Primitive"/>) or a generated type referred to by name
    /// (<see cref="Named"/> — a record, an enum, or an opaque placeholder).
    /// </summary>
    /// <remarks>
    /// <see cref="Named"/> deliberately does not distinguish record from enum: whether the
    /// referent is a value type is carried alongside (<c>IsValueType</c> on the owning plan),
    /// because that is a property of the referent, not of the reference. Emitters that need
    /// the distinction — C# nullability, for instance — read that flag.
    /// </remarks>
    internal abstract record TypeRef
    {
        /// <summary>An XSD built-in datatype.</summary>
        public sealed record Primitive(PrimitiveKind Kind) : TypeRef;

        /// <summary>A generated type, referred to by its (PascalCase) name.</summary>
        public sealed record Named(string Name) : TypeRef;

        /// <summary>
        /// No type of its own. Used by synthetic children that exist only to carry a
        /// <see cref="ValueEncoding"/> — the inline-choice placeholder, whose branches are
        /// each their own field, so the placeholder itself is never dereferenced.
        /// </summary>
        public sealed record NoType : TypeRef;

        public static readonly TypeRef None = new NoType();
    }
}
