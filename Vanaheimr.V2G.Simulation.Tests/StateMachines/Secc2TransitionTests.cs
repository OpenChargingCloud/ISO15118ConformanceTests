using NUnit.Framework;

using Vanaheimr.V2G.Iso15118_2;
using Vanaheimr.V2G.Iso15118_2.Generated;
using Vanaheimr.V2G.Simulation.Session;
using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.StateMachines.Iso2;

namespace Vanaheimr.V2G.Simulation.Tests.StateMachines;

/// <summary>
/// Direct (no-socket) tests for <see cref="Secc2.Handle"/> — the phase-guarded dispatch that decides
/// what's a legal next request. Exercises the full AC and DC happy paths plus the sequence guard's
/// rejection of an out-of-order request.
/// </summary>
[TestFixture]
public class Secc2TransitionTests
{
    private static V2G_Message Wrap(byte[] sid, BodyBaseType body) =>
        new(new MessageHeaderType(sid, Notification: null, Signature: null), new BodyType(body));

    [Test]
    public void AcHappyPath_ReachesDone()
    {
        var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
        var sid = new byte[8];

        var setupRes = (SessionSetupResType)secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 }))).Body.BodyElement!;
        Assert.That(setupRes.ResponseCode, Is.EqualTo(ResponseCode.OK_NewSessionEstablished));

        secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null)));
        secc.Handle(Wrap(sid, new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
            new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
        secc.Handle(Wrap(sid, new AuthorizationReqType(null, null)));
        secc.Handle(Wrap(sid, new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.AC_three_phase_core,
            new AC_EVChargeParameterType(null, PhysicalValue.Of(22_000, UnitSymbol.Wh),
                new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 32),
                new PhysicalValueType(0, UnitSymbol.A, 6)))));
        secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null)));
        secc.Handle(Wrap(sid, new ChargingStatusReqType()));
        secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Stop, 1, null, null)));
        secc.Handle(Wrap(sid, new SessionStopReqType(ChargingSession.Terminate)));

        Assert.That(secc.IsDone, Is.True);
    }

    [Test]
    public void DcHappyPath_ReachesDone()
    {
        var secc = new Secc2(PowerMode.Dc, TimeSpan.FromSeconds(60), TimeProvider.System);
        var sid = new byte[8];
        var evStatus = new DC_EVStatusType(EVReady: true, DC_EVErrorCode.NO_ERROR, EVRESSSOC: 50);

        secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 })));
        secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null)));
        secc.Handle(Wrap(sid, new PaymentServiceSelectionReqType(PaymentOption.ExternalPayment,
            new SelectedServiceListType(new[] { new SelectedServiceType(1, null) }))));
        secc.Handle(Wrap(sid, new AuthorizationReqType(null, null)));
        secc.Handle(Wrap(sid, new ChargeParameterDiscoveryReqType(null, EnergyTransferMode.DC_extended,
            new DC_EVChargeParameterType(null, evStatus, new PhysicalValueType(0, UnitSymbol.A, 200), null,
                new PhysicalValueType(0, UnitSymbol.V, 500), null, null, 100, 80))));
        secc.Handle(Wrap(sid, new CableCheckReqType(evStatus)));
        secc.Handle(Wrap(sid, new PreChargeReqType(evStatus, new PhysicalValueType(0, UnitSymbol.V, 400), new PhysicalValueType(0, UnitSymbol.A, 2))));
        secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null)));
        secc.Handle(Wrap(sid, new CurrentDemandReqType(evStatus, new PhysicalValueType(0, UnitSymbol.A, 120),
            null, null, null, null, false, null, null, new PhysicalValueType(0, UnitSymbol.V, 400))));
        secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Stop, 1, null, null)));
        secc.Handle(Wrap(sid, new WeldingDetectionReqType(evStatus)));
        secc.Handle(Wrap(sid, new SessionStopReqType(ChargingSession.Terminate)));

        Assert.That(secc.IsDone, Is.True);
    }

    [Test]
    public void OutOfOrderRequest_ThrowsSessionAborted()
    {
        var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), TimeProvider.System);
        var sid = new byte[8];

        // Skip straight to PowerDelivery without SessionSetup first.
        Assert.That(() => secc.Handle(Wrap(sid, new PowerDeliveryReqType(ChargeProgress.Start, 1, null, null))),
            Throws.InstanceOf<SessionAborted>().With.Message.Contain("sequence guard"));
    }
}
