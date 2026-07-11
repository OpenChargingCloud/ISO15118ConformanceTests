using System.Net;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.E2E;

/// <summary>
/// Real TCP, loopback-only, end-to-end: our EVCC talks to our SECC over an actual socket (OS-assigned
/// free port), starting with the SAP handshake, then a full ISO 15118-2 AC or DC happy path to
/// SessionStop. Proves codec + V2GTP framing + SAP + sequencing all work together over a real
/// transport, not just in-process method calls.
/// </summary>
[TestFixture]
public class Iso2LoopbackTests
{
    [Test]
    public async Task AcSession_RunsToCompletion()
    {
        await RunSessionAsync(PowerMode.Ac);
    }

    [Test]
    public async Task DcSession_RunsToCompletion()
    {
        await RunSessionAsync(PowerMode.Dc);
    }

    private static async Task RunSessionAsync(PowerMode mode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

        var seccTask = Task.Run(async () =>
        {
            using var seccStream = await listener.AcceptAsync(cts.Token);
            await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_2, cts.Token);

            var secc = new Secc2(mode, TimeSpan.FromSeconds(60), TimeProvider.System);
            await secc.RunAsync(seccStream, cts.Token);
            return secc;
        }, cts.Token);

        using var evccStream = await TcpV2GClient.ConnectAsync(
            IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
        await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_2, cts.Token);

        var evcc = new Evcc2(evccStream, mode, TimeProvider.System, new ImmediateAsyncDelay(), TimeSpan.FromSeconds(2));
        await evcc.RunAsync(cts.Token);

        var secc = await seccTask;
        Assert.That(secc.IsDone, Is.True);
        Assert.That(evcc.Exchanges, Is.GreaterThan(0));
    }
}
