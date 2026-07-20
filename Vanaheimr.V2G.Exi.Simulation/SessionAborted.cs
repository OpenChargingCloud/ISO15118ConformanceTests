namespace Vanaheimr.V2G.Exi.Simulation
{
    /// <summary>Raised when a session ends abnormally — a sequence-guard rejection or a timeout.</summary>
    public sealed class SessionAborted(string reason) : Exception(reason);
}
