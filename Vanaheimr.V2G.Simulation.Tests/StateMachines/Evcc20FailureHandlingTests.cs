using System.Net;

using NUnit.Framework;

using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Sap;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Simulation.Tests.Timing;
using Vanaheimr.V2G.Simulation.Transport;
using Vanaheimr.V2G.Tp;

using Dc20 = Vanaheimr.V2G.Iso15118_20.DC.Generated;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{

    /// <summary>
    /// What our -20 EVCC does when the station answers with a <c>FAILED</c> code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until 2026-08-01 the answer was: nothing. Nothing in the -20 EVCC read a response code at all —
    /// <c>Expect&lt;T&gt;</c> checks the message set and the type, and the cable-check loop watched only
    /// <c>EVSEProcessing</c> — so a station could answer FAILED to every message and our car would drive
    /// the session to completion.
    /// </para>
    /// <para>
    /// <b>It took a live peer to see it.</b> eVDriveFlow answered <c>DC_CableCheckRes</c> with
    /// <c>FAILED</c> and we charged on through PreCharge and PowerDelivery
    /// (<c>docs/interop-runs/2026-08-01-edf-iso20-dc-notls/</c>). The loopback suite could not have found
    /// it and neither could the trace corpus: our own SECC never says FAILED, so no recording contains
    /// one. Hence this fixture, which is the FAILED-saying station the suite did not have.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Evcc20FailureHandlingTests
    {

        /// <summary>
        /// The ordering the <c>&gt;= FAILED</c> comparison rests on.
        /// </summary>
        /// <remarks>
        /// The check in <c>Evcc20Base.RefuseOnFailure</c> is a range test, which is only sound while the
        /// schema keeps its three families contiguous and in order. A regenerated enum that interleaved
        /// them would silently turn some failures into successes — the exact shape of bug this whole
        /// fixture exists because of — so the property is pinned rather than assumed.
        /// </remarks>
        [Test]
        public void TheResponseCodeFamiliesAreContiguousAndOrdered()
        {
            foreach (ResponseCode code in Enum.GetValues<ResponseCode>())
            {
                var name = code.ToString();

                if (name.StartsWith("FAILED", StringComparison.Ordinal))
                    Assert.That(code, Is.GreaterThanOrEqualTo(ResponseCode.FAILED), $"{name} sorts below FAILED");
                else
                    Assert.That(code, Is.LessThan(ResponseCode.FAILED),
                                $"{name} is not a failure but sorts at or above FAILED");
            }

            // And the same three enums exist per message set, generated separately from the same schema.
            Assert.That((int) Dc20.ResponseCode.FAILED, Is.EqualTo((int) ResponseCode.FAILED));
        }


        /// <summary>
        /// A station that fails the cable check: the exact shape eVDriveFlow presented.
        /// </summary>
        private sealed class FailingStation(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                var (responseSet, response) = base.Handle(set, request);

                // Everything up to the cable check is answered normally, so the session gets far enough
                // for the answer to matter — which is precisely why the old code walked past it.
                return response is Dc20.DC_CableCheckRes res
                           ? (responseSet, res with { ResponseCode = Dc20.ResponseCode.FAILED })
                           : (responseSet, response);
            }
        }


        /// <summary>
        /// The finding itself, as a test: a FAILED cable check ends the session.
        /// </summary>
        /// <remarks>
        /// Before the fix this ran to a full, "successful" charging session — PreCharge, PowerDelivery,
        /// charge loop, welding detection, SessionStop — against a station that had said the cable check
        /// failed.
        /// </remarks>
        [Test]
        public async Task AFailedCableCheckEndsTheSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new FailingStation(TimeSpan.FromSeconds(60), TimeProvider.System);
                try { await secc.RunAsync(seccStream, cts.Token); }
                catch { /* the EV hangs up on us; that is the point */ }
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                   (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                    LoopbackTimeouts.PerMessage);

            var aborted = Assert.ThrowsAsync<SessionAborted>(async () => await evcc.RunAsync(cts.Token));

            Assert.Multiple(() =>
            {
                // The message has to name both halves: which message failed, and with what. A live run is
                // read from this line.
                Assert.That(aborted!.Message, Does.Contain("DC_CableCheckRes"));
                Assert.That(aborted!.Message, Does.Contain("FAILED"));

                // And it stopped *there*, rather than pressing on into the charging phases.
                Assert.That(evcc.Exchanges, Is.LessThan(12),
                            "the session should end at the cable check, not run its full course");
            });

            // Hang up before waiting on the station: our EV aborted rather than sending SessionStop, so
            // the SECC is still blocked on a read and would otherwise sit there until the token fires.
            evccStream.Dispose();
            await seccTask;
        }


        /// <summary>
        /// A WARNING is not a failure, and treating it as one would be its own defect.
        /// </summary>
        /// <remarks>
        /// The specification has three families for a reason: <c>WARNING*</c> is the code for "something
        /// is off and the session continues". A check that aborted on anything not <c>OK</c> would turn
        /// an expiring certificate into a refused charge.
        /// </remarks>
        [Test]
        public async Task AWarningDoesNotEndTheSession()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpV2GListener(new IPEndPoint(IPAddress.Loopback, 0));

            var seccTask = Task.Run(async () =>
            {
                using var seccStream = await listener.AcceptAsync(cts.Token);
                await SapHandshake.RunSeccSideAsync(seccStream, ProtocolVariant.Iso15118_20, cts.Token);
                var secc = new WarningStation(TimeSpan.FromSeconds(60), TimeProvider.System);
                await secc.RunAsync(seccStream, cts.Token);
                return secc;
            }, cts.Token);

            using var evccStream = await TcpV2GClient.ConnectAsync("localhost", listener.LocalEndpoint.Port,
                                                                   (TlsOptions?) null, cts.Token);
            await SapHandshake.RunEvccSideAsync(evccStream, ProtocolVariant.Iso15118_20, cts.Token);

            var evcc = new Evcc20Dc(evccStream, TimeProvider.System, new ImmediateAsyncDelay(),
                                    LoopbackTimeouts.PerMessage);
            await Task.WhenAll(evcc.RunAsync(cts.Token), seccTask);

            Assert.That((await seccTask).IsDone, Is.True, "a warned session still completes");
        }


        private sealed class WarningStation(TimeSpan sequenceTimeout, TimeProvider clock)
            : Secc20Dc(sequenceTimeout, clock)
        {
            public override (MessageSet Set, object Response) Handle(MessageSet set, object request)
            {
                var (responseSet, response) = base.Handle(set, request);

                return response is Dc20.DC_CableCheckRes res
                           ? (responseSet, res with { ResponseCode = Dc20.ResponseCode.WARNING_CertificateExpired })
                           : (responseSet, response);
            }
        }

    }

}
