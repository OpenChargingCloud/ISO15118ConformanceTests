using System.Diagnostics;

using Vanaheimr.V2G.Iso15118_2.Generated;

namespace Vanaheimr.V2G.Exi.Simulation;

/// <summary>
/// The wire: it serialises a <see cref="V2G_Message"/> to ISO 15118-2 EXI, hands the octets to the
/// SECC, and parses the reply back — the only part that actually touches the codec. It also enforces
/// the EV-side message timeout: if the SECC takes longer than the budget to answer, the exchange
/// fails (a real EVCC would drop the session). In production these octets travel over TCP/TLS after
/// SDP discovery; here the SECC is an in-process object.
/// </summary>
public sealed class Wire(Secc secc, bool verbose = true)
{
    private readonly byte[] _buf = new byte[4096];

    public int Exchanges { get; private set; }
    public long BytesOnWire { get; private set; }

    public V2G_Message Exchange(V2G_Message request, TimeSpan timeout)
    {
        if (!request.TryEncode(_buf, out int reqLen))
            throw new InvalidOperationException("EXI encode failed (buffer too small?)");
        var atSecc = (V2G_Message)Iso2Codec.DecodeAny(_buf.AsSpan(0, reqLen), out _);

        var sw = Stopwatch.StartNew();
        var reply = secc.Handle(atSecc);                       // SECC state machine (may stall)
        if (sw.Elapsed > timeout)
            throw new SessionAborted(
                $"{Name(request)}: no response within {timeout.TotalMilliseconds:0} ms (took {sw.ElapsedMilliseconds} ms)");

        if (!reply.TryEncode(_buf, out int resLen))
            throw new InvalidOperationException("EXI encode failed (buffer too small?)");

        Exchanges++;
        BytesOnWire += reqLen + resLen;
        if (verbose)
            Console.WriteLine($"  {Name(request),-27} →{reqLen,4} B    ←{resLen,4} B  {Name(reply)}");

        return (V2G_Message)Iso2Codec.DecodeAny(_buf.AsSpan(0, resLen), out _);
    }

    internal static string Name(V2G_Message m) =>
        m.Body.BodyElement?.GetType().Name.Replace("Type", "") ?? "(empty)";
}
