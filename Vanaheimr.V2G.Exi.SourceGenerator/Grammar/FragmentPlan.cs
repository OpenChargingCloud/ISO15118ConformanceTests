namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar
{
    /// <summary>A signable element that gets an EXI fragment encoder/decoder: its fragment-grammar
    /// event code and the generated type that carries its content.</summary>
    internal sealed record FragmentPlan(string ElementName, string TypeName, int EventCode);
}
