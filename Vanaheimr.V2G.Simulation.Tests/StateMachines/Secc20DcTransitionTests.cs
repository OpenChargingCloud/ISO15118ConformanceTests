using NUnit.Framework;

using Vanaheimr.V2G.Iso15118_20.CommonMessages.Generated;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso20;
using Vanaheimr.V2G.Tp;

using Dc20 = Vanaheimr.V2G.Iso15118_20.DC.Generated;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines
{
    /// <summary>
    /// Direct (no-socket) tests for <see cref="Secc20Dc"/>'s phase machine, focused on the DC poll-loop
    /// self-looping: a real EV polls CableCheck / PreCharge / WeldingDetection repeatedly (each a separate
    /// request) before sending the next-phase message. The SECC must answer every poll in place and only
    /// advance when the next-phase message arrives — a live Josev EVCC → our SECC interop run surfaced the
    /// earlier one-shot behaviour ("DC_PreChargeReq not allowed in phase PowerOn" on the second poll).
    /// See docs/interop-runs/2026-07-21-iso20-dc-tcp-reverse/notes.md.
    /// </summary>
    [TestFixture]
    public class Secc20DcTransitionTests
    {
        private readonly SessionContext _ctx = new(TimeProvider.System);
        private MessageHeaderType Common => _ctx.ToCommonHeader();
        private Dc20.MessageHeaderType Dc => _ctx.ToDcHeader();

        /// <summary>Drives the SECC through the shared CommonMessages setup up to (not including) CableCheck.</summary>
        private void RunSetup(Secc20Dc secc)
        {
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionSetupReq(Common, "EVCC01"));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            secc.Handle(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM, new EIM_AReqAuthorizationModeType(), null));
            var disc = (ServiceDiscoveryRes)secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null)).Response;
            ushort svc = disc.EnergyTransferServiceList.Service[0].ServiceID;
            var detail = (ServiceDetailRes)secc.Handle(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, svc)).Response;
            ushort psid = detail.ServiceParameterList.ParameterSet[0].ParameterSetID;
            secc.Handle(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common, new SelectedServiceType(svc, psid), null));

            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Dc,
                new Dc20.DC_CPDReqEnergyTransferModeType(Rat(5_000, 1), Rat(0), Rat(200), Rat(0), Rat(500), Rat(50), TargetSOC: 80)));
            secc.Handle(MessageSet.Iso20CommonMessages, new ScheduleExchangeReq(Common, 12, null,
                new Scheduled_SEReqControlModeType(null, null, null, null, null)));
        }

        [Test]
        public void DcSession_WithPolledCableCheckPreChargeAndWelding_ReachesDone()
        {
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            RunSetup(secc);

            // CableCheck: three polls in a row — each must answer with a CableCheckRes and stay in phase.
            for (int i = 0; i < 3; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
                Assert.That(set, Is.EqualTo(MessageSet.Iso20DC));
                Assert.That(resp, Is.InstanceOf<Dc20.DC_CableCheckRes>());
            }

            // PreCharge: the first DC_PreChargeReq ends the CableCheck loop; three polls total, all answered.
            for (int i = 0; i < 3; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20DC,
                    new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Finished, EVPresentVoltage: Rat(0), EVTargetVoltage: Rat(400)));
                Assert.That(resp, Is.InstanceOf<Dc20.DC_PreChargeRes>());
            }

            // PowerDelivery(Start) ends the PreCharge loop and enters the charge loop.
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, BuildProfile(), null));

            for (int i = 0; i < 4; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false,
                    EVPresentVoltage: Rat(400), CLReqControlMode: new Dc20.Scheduled_DC_CLReqControlModeType(null, null, null, Rat(120), Rat(400), null, null, null, null, null)));
                Assert.That(resp, Is.InstanceOf<Dc20.DC_ChargeLoopRes>());
            }

            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Stop, null, null));

            // WeldingDetection: three polls, all answered in place.
            for (int i = 0; i < 3; i++)
            {
                var (set, resp) = secc.Handle(MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionReq(Dc, Dc20.Processing.Finished));
                Assert.That(resp, Is.InstanceOf<Dc20.DC_WeldingDetectionRes>());
            }

            secc.Handle(MessageSet.Iso20CommonMessages, new SessionStopReq(Common, ChargingSession.Terminate, null, null));
            Assert.That(secc.IsDone, Is.True);
        }

        [Test]
        public void DcSession_SinglePollEach_StillReachesDone()
        {
            // The loopback EVCC sends exactly one CableCheck/PreCharge/Welding — the self-loop must not break that.
            var secc = new Secc20Dc(TimeSpan.FromSeconds(60), TimeProvider.System);
            RunSetup(secc);

            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Dc));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_PreChargeReq(Dc, Dc20.Processing.Finished, Rat(0), Rat(400)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Start, BuildProfile(), null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_ChargeLoopReq(Dc, null, false, Rat(400),
                new Dc20.Scheduled_DC_CLReqControlModeType(null, null, null, Rat(120), Rat(400), null, null, null, null, null)));
            secc.Handle(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished, ChargeProgress.Stop, null, null));
            secc.Handle(MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionReq(Dc, Dc20.Processing.Finished));
            secc.Handle(MessageSet.Iso20CommonMessages, new SessionStopReq(Common, ChargingSession.Terminate, null, null));

            Assert.That(secc.IsDone, Is.True);
        }

        private EVPowerProfileType BuildProfile() => new(
            TimeAnchor: 0, Dynamic_EVPPTControlMode: null,
            Scheduled_EVPPTControlMode: new Scheduled_EVPPTControlModeType(1, PowerToleranceAcceptance.PowerToleranceConfirmed),
            EVPowerProfileEntries: new EVPowerProfileEntryListType(new[]
            {
                new PowerScheduleEntryType(Duration: 3600, Power: new RationalNumberType(3, 10), Power_L2: null, Power_L3: null),
            }));

        private static Dc20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);
    }
}
