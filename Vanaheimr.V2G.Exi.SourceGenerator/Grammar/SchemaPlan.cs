using System.Collections.Generic;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar;

/// <summary>
/// The full plan for one schema, ready for emission.
/// </summary>
internal sealed record SchemaPlan(
    string                          TargetNamespace,
    IReadOnlyList<GlobalElementPlan> GlobalElements,
    IReadOnlyDictionary<string, SequencePlan> ComplexTypes,
    IReadOnlyList<EnumPlan>         Enums,
    IReadOnlyList<string>           OpaqueTypes,   // empty placeholder records for opaque refs
    int                             DocumentSelectorBits, // width of the document element selector
    int                             FragmentSelectorBits, // width of the EXI fragment element selector
    int                             FragmentEndCode,      // "End Fragment" (ED) event code
    IReadOnlyList<FragmentPlan>     Fragments);    // signable elements to emit fragment codecs for
