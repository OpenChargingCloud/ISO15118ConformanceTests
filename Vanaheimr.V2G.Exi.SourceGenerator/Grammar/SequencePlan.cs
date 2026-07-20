using System.Collections.Generic;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar
{
    internal sealed record SequencePlan(
        string                   CSharpRecordName,  // e.g. "AppProtocolEntry"
        IReadOnlyList<ChildPlan> Children,
        int                      ListMin = 0,
        int                      ListMax = 0,
        bool                     IsAbstract = false, // emit as `abstract record`
        string?                  BaseRecordName = null, // extension/substitution base record
        IReadOnlyList<AttrPlan>? Attributes = null,    // AT events (sorted by name), before content
        bool                     IsChoice = false,      // Children are mutually-exclusive xs:choice alternatives
        ValueEncoding?           SimpleContent = null,  // xs:simpleContent: the single content value's encoding
        string?                  SimpleContentType = null);
}
