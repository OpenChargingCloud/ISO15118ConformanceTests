namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar
{
    /// <summary>
    /// Top-level plan for a global element.
    /// </summary>
    internal sealed record GlobalElementPlan(
        string        XsdName,            // e.g. "supportedAppProtocolReq"
        string        TypeName,           // "SupportedAppProtocolReq"
        SequencePlan  Body,
        int           DocumentIndex);     // production index in the (full) document grammar
}
