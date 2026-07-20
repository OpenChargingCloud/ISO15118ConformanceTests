using CommonHeader = Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated.MessageHeaderType;
using DcHeader = Vanaheimr.V2G.Iso15118_20.DC.Generated.MessageHeaderType;
using AcHeader = Vanaheimr.V2G.Iso15118_20.AC.Generated.MessageHeaderType;

namespace Vanaheimr.V2G.Simulation.Session
{
    /// <summary>
    /// ISO 15118-20's per-schema-set assemblies are self-contained (no cross-references between
    /// CommonMessages/DC/AC), so <c>MessageHeaderType</c> is a structurally identical but distinct CLR
    /// type per message set. This holds the session's actual state (the SECC-assigned SessionID, a
    /// monotonic TimeStamp) and renders it into whichever namespace's header the next outgoing message
    /// needs — the concrete fix for that duplication when a -20 session crosses from a CommonMessages
    /// phase to a DC/AC phase and back.
    /// </summary>
    public sealed class SessionContext(TimeProvider clock)
    {
        public byte[] SessionId { get; set; } = new byte[8];

        private ulong NextTimeStamp() => (ulong)clock.GetUtcNow().ToUnixTimeSeconds();

        public CommonHeader ToCommonHeader() => new(SessionId, NextTimeStamp(), Signature: null);
        public DcHeader ToDcHeader() => new(SessionId, NextTimeStamp(), Signature: null);
        public AcHeader ToAcHeader() => new(SessionId, NextTimeStamp(), Signature: null);
    }
}
