using System.Net;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.E2E;

/// <summary>
/// Real TCP, loopback-only, end-to-end for ISO 15118-20: SAP negotiates -20, then a full DC or AC
/// happy path runs to SessionStop across the three interleaved message sets (CommonMessages/DC/AC),
/// each auto-detected per frame by <see cref="Vanaheimr.V2G.Tp.V2GTPDispatcher"/>.
/// </summary>
[TestFixture]
public class Iso20LoopbackTests
{
    [Test]
    public async Task DcSession_RunsToCompletion()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

        var seccTask = Task.Run(async () =>
        {
            using var seccStream = await listener.AcceptAsync(cts.Token);
            await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            await secc.RunAsync(seccStream, cts.Token);
            return secc;
        }, cts.Token);

        using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
        await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

        var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), TimeSpan.FromSeconds(2));
        var evccTask = evcc.RunAsync(cts.Token);
        await Task.WhenAll(evccTask, seccTask);

        var secc = await seccTask;
        Assert.That(secc.IsDone, Is.True);
        Assert.That(evcc.Exchanges, Is.GreaterThan(0));
    }

    [Test]
    public async Task AcSession_RunsToCompletion()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

        var seccTask = Task.Run(async () =>
        {
            using var seccStream = await listener.AcceptAsync(cts.Token);
            await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var secc = new Secc20Ac(TimeSpan.FromSeconds(60), TimeProvider.System);
            await secc.RunAsync(seccStream, cts.Token);
            return secc;
        }, cts.Token);

        using var evccStream = await TcpV2GClient.ConnectAsync(IPAddress.Loopback.ToString(), listener.LocalEndpoint.Port, ct: cts.Token);
        await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

        var evcc = new Evcc20Ac(evccStream, TimeProvider.System, new ImmediateAsyncDelay(), TimeSpan.FromSeconds(2));
        var evccTask = evcc.RunAsync(cts.Token);
        await Task.WhenAll(evccTask, seccTask);

        var secc = await seccTask;
        Assert.That(secc.IsDone, Is.True);
        Assert.That(evcc.Exchanges, Is.GreaterThan(0));
    }
}
