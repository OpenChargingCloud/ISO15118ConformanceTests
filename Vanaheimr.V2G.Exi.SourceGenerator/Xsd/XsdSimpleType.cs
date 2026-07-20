using System.Collections.Generic;

namespace Vanaheimr.V2G.Exi.SourceGenerator.Xsd
{
    /// <summary>
    /// Simple type derived by restriction from a single base. Value-space facets cover
    /// only what AppProtocol uses: integer min/max bounds, string maxLength, and enumeration.
    /// </summary>
    internal sealed class XsdSimpleType
    {
        public string Name { get; set; } = "";
        public string Base { get; set; } = "";   // e.g. "xs:unsignedByte" or "xs:string"

        public long?  MinInclusive { get; set; }
        public long?  MaxInclusive { get; set; }
        public int?   MaxLength    { get; set; }

        /// <summary>Lexicographically sorted (per EXI canonical ordering).</summary>
        public List<string>? Enumeration { get; set; }
    }
}
