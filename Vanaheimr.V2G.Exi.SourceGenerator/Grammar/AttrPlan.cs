namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar
{
    /// <summary>An attribute (AT event) of a complex type.</summary>
    internal sealed record AttrPlan(string FieldName, TypeRef Type, ValueEncoding Value, bool Required = false);
}
