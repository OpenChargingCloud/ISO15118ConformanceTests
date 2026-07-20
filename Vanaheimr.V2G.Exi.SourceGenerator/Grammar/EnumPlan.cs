using System.Collections.Generic;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Grammar;

internal sealed record EnumPlan(string Name, IReadOnlyList<string> Members);
