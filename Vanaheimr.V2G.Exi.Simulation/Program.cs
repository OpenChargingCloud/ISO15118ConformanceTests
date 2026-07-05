using System.Diagnostics;

using Vanaheimr.V2G.Exi.Simulation;

// Usage: dotnet run [ac|dc] [--slow] [--break-sequence]
//   ac | dc          which energy-transfer mode to simulate (default: dc)
//   --slow           SECC stalls on AuthorizationReq → trips the EV message timeout
//   --break-sequence EV sends PowerDeliveryReq out of order → trips the SECC sequence guard

var mode = args.Contains("ac", StringComparer.OrdinalIgnoreCase) ? ChargingMode.Ac : ChargingMode.Dc;
var slow = args.Contains("--slow");
var breakSeq = args.Contains("--break-sequence");

Console.WriteLine($"ISO 15118-2 {mode.ToString().ToUpperInvariant()} charging session — every line is a real EXI round-trip\n");

var secc = new Secc(mode,
    sequenceTimeout: TimeSpan.FromSeconds(60),
    authDelay: slow ? TimeSpan.FromSeconds(3) : TimeSpan.Zero);
var wire = new Wire(secc);
var evcc = new Evcc(wire, mode, breakSequence: breakSeq);

var sw = Stopwatch.StartNew();
try
{
    evcc.Run();
    Console.WriteLine(
        $"\n✓ Session complete — {wire.Exchanges} exchanges, {wire.BytesOnWire} bytes on the wire, {sw.ElapsedMilliseconds} ms.");
}
catch (SessionAborted ex)
{
    Console.WriteLine($"\n✗ Session aborted: {ex.Message}");
    Environment.ExitCode = 1;
}
