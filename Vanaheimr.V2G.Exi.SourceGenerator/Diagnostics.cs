using Microsoft.CodeAnalysis;

namespace Vanaheimr.V2G.Exi.SourceGenerator
{
    internal static class Diagnostics
    {
        public static readonly DiagnosticDescriptor XsdParseError = new(
            id:                 "EXIGEN001",
            title:              "XSD parse error",
            messageFormat:      "Failed to parse XSD '{0}': {1}",
            category:           "Vanaheimr.V2G.Exi.SourceGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedConstruct = new(
            id:                 "EXIGEN002",
            title:              "Unsupported XSD construct",
            messageFormat:      "XSD '{0}' uses an unsupported construct: {1}",
            category:           "Vanaheimr.V2G.Exi.SourceGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InternalError = new(
            id:                 "EXIGEN003",
            title:              "Internal generator error",
            messageFormat:      "Generator failed for '{0}': {1}",
            category:           "Vanaheimr.V2G.Exi.SourceGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
