/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

using Ac20 = cloud.charging.open.protocols.ISO15118_20.AC.Generated;
using Dc20 = cloud.charging.open.protocols.ISO15118_20.DC.Generated;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// Which control-mode parameter set the EV selects — and therefore the shape every later message
    /// takes, since the SECC must answer strictly in kind ([V2G20-1600]). The value is the
    /// <c>ParameterSetID</c> the SECC advertises in ServiceDetailRes.
    /// </summary>
    public enum Iso20ControlMode
    {
        Scheduled = 1,
        Dynamic   = 2,
    }

    /// <summary>
    /// An EV that drives a <see cref="Secc20Base"/> through a whole -20 session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// -2's equivalent is six lines inlined in a test, because -2 has one message set and one shape.
    /// -20 does not: the phases interleave <b>three</b> codecs, AC and DC diverge in the middle
    /// (CableCheck/PreCharge/WeldingDetection are DC-only), and each of the four control-mode
    /// variants needs its own request types. Written out per test that is thirty-odd lines before
    /// the first assertion, which is why the tests around it inline their own — and why the -20
    /// signed meter reading shipped with its two call sites checked by <em>reading</em> the code
    /// rather than by running it. The sequence was never unknown; it just had no reusable shape.
    /// </para>
    /// <para>
    /// The sibling fixtures still inline their setups deliberately: they vary the middle in ways
    /// this driver does not parameterise (renegotiation mid-loop, two control modes in one session,
    /// a mismatched charge-parameter shape). This is the straight path, not a replacement for them.
    /// </para>
    /// <para>
    /// The split mirrors the SECC's own: the CommonMessages phases live here, the diverging middle
    /// is delegated to <see cref="Ac20SessionDriver"/>/<see cref="Dc20SessionDriver"/>. That is not
    /// symmetry for its own sake — it is what keeps the driver honest about where the two protocols
    /// actually differ, instead of hiding a DC-shaped sequence behind an AC-shaped name.
    /// </para>
    /// <para>
    /// It drives the SECC through <see cref="Secc20Base.Handle"/>, not over a socket: what is under
    /// test is the state machine's sequencing and the content of its responses. Where the codec
    /// matters — a signature surviving the wire — a test encodes that one response itself, which is
    /// the narrower and more diagnosable check.
    /// </para>
    /// </remarks>
    public abstract class Iso20SessionDriver
    {
        private readonly Secc20Base secc;
        private readonly ushort serviceId;

        /// <summary>The EV's own header state. Separate from the SECC's on purpose: a driver that
        /// borrowed the SECC's context could not notice it echoing the wrong session id.</summary>
        protected readonly SessionContext Ev = new(TimeProvider.System);

        protected MessageHeaderType Common => Ev.ToCommonHeader();

        /// <summary>The control mode this EV selected; the charge-loop hooks answer in its shape.</summary>
        protected Iso20ControlMode Mode { get; }

        protected Iso20SessionDriver(Secc20Base secc, ushort serviceId, Iso20ControlMode mode)
        {
            this.secc      = secc;
            this.serviceId = serviceId;
            Mode           = mode;
        }

        protected (MessageSet Set, object Response) Send(MessageSet set, object request) =>
            secc.Handle(set, request);

        protected T Send<T>(MessageSet set, object request) =>
            (T) secc.Handle(set, request).Response;

        // ── The diverging middle ────────────────────────────────────────────────────────────────

        protected abstract void ChargeParameterDiscovery();

        /// <summary>DC's CableCheck + PreCharge, between ScheduleExchange and PowerDelivery(Start).</summary>
        protected virtual void PreChargeSequence() { }

        /// <summary>DC's WeldingDetection, between PowerDelivery(Stop) and SessionStop.</summary>
        protected virtual void PostChargeSequence() { }

        // ── The session ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything before the charge loop: SessionSetup through PowerDelivery(Start). Afterwards
        /// the SECC is in its Charging phase and <c>ChargeLoop()</c> on the concrete driver answers.
        /// </summary>
        /// <remarks>Each concrete driver shadows this with its own return type, so a test can write
        /// <c>new Ac20SessionDriver(secc).ToChargeLoop().ChargeLoop()</c> in one expression.</remarks>
        public Iso20SessionDriver ToChargeLoop()
        {
            Send(MessageSet.Iso20CommonMessages, new SessionSetupReq(Common, "EVCC01"));
            Send(MessageSet.Iso20CommonMessages, new AuthorizationSetupReq(Common));
            Send(MessageSet.Iso20CommonMessages, new AuthorizationReq(Common, Authorization.EIM,
                                                                      new EIM_AReqAuthorizationModeType(), null));
            Send(MessageSet.Iso20CommonMessages, new ServiceDiscoveryReq(Common, null));
            Send(MessageSet.Iso20CommonMessages, new ServiceDetailReq(Common, serviceId));
            Send(MessageSet.Iso20CommonMessages, new ServiceSelectionReq(Common,
                                                                         new SelectedServiceType(serviceId, (ushort) Mode),
                                                                         null));
            ChargeParameterDiscovery();
            Send(MessageSet.Iso20CommonMessages, ScheduleExchange());
            PreChargeSequence();
            Send(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished,
                                                                      ChargeProgress.Start, null, null));
            return this;
        }

        /// <summary>
        /// Ends the session properly: PowerDelivery(Stop), the post-charge sequence if this mode has
        /// one, then SessionStop.
        /// </summary>
        /// <remarks>
        /// Worth having even when a test's assertion is about the charge loop. A driver that stops
        /// at the first interesting response leaves the SECC in a state a real EV never produces, so
        /// the sequence guard never gets asked whether the rest of the session would have worked.
        /// </remarks>
        public SessionStopRes Stop(ChargingSession how = ChargingSession.Terminate)
        {
            Send(MessageSet.Iso20CommonMessages, new PowerDeliveryReq(Common, Processing.Finished,
                                                                      ChargeProgress.Stop, null, null));
            PostChargeSequence();
            return Send<SessionStopRes>(MessageSet.Iso20CommonMessages,
                                        new SessionStopReq(Common, how, null, null));
        }

        private ScheduleExchangeReq ScheduleExchange() =>
            Mode is Iso20ControlMode.Dynamic
                ? new ScheduleExchangeReq(Common, 12,
                      new Dynamic_SEReqControlModeType(
                          DepartureTime: 7200, MinimumSOC: 30, TargetSOC: 80,
                          EVTargetEnergyRequest:  new RationalNumberType(3, 40),
                          EVMaximumEnergyRequest: new RationalNumberType(1, 6_000),
                          EVMinimumEnergyRequest: new RationalNumberType(0, -20_000),
                          EVMaximumV2XEnergyRequest: null, EVMinimumV2XEnergyRequest: null),
                      null)
                : new ScheduleExchangeReq(Common, 12, null,
                      new Scheduled_SEReqControlModeType(null, null, null, null, null));
    }

    /// <summary>The AC half: AC charge-parameter discovery and the AC charge loop, no DC sequences.</summary>
    public sealed class Ac20SessionDriver(Secc20Ac secc,
                                          ushort serviceId = 1,
                                          Iso20ControlMode mode = Iso20ControlMode.Scheduled)
        : Iso20SessionDriver(secc, serviceId, mode)
    {
        private Ac20.MessageHeaderType Header => Ev.ToAcHeader();
        private static Ac20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);

        /// <summary>Service 5 is AC_BPT; a BPT service takes BPT-shaped parameters throughout.</summary>
        private bool Bpt => serviceId is 5;

        protected override void ChargeParameterDiscovery()
        {
            Ac20.AC_CPDReqEnergyTransferModeType mode = Bpt
                ? new Ac20.BPT_AC_CPDReqEnergyTransferModeType(
                      EVMaximumChargePower: Rat(2_200, 1), null, null,
                      EVMinimumChargePower: Rat(0), null, null,
                      EVMaximumDischargePower: Rat(2_200, 1), null, null,
                      EVMinimumDischargePower: Rat(0), null, null)
                : new Ac20.AC_CPDReqEnergyTransferModeType(
                      EVMaximumChargePower: Rat(2_200, 1), null, null,
                      EVMinimumChargePower: Rat(0), null, null);

            Send(MessageSet.Iso20AC, new Ac20.AC_ChargeParameterDiscoveryReq(Header, mode));
        }

        public new Ac20SessionDriver ToChargeLoop()
        {
            base.ToChargeLoop();
            return this;
        }

        /// <summary>One charge-loop iteration, in the shape this session's control mode requires.</summary>
        public Ac20.AC_ChargeLoopRes ChargeLoop(bool meterInfoRequested = false) =>
            Send<Ac20.AC_ChargeLoopRes>(MessageSet.Iso20AC,
                new Ac20.AC_ChargeLoopReq(Header, null, meterInfoRequested, ControlMode()));

        private Ac20.CLReqControlModeType ControlMode() => (Mode, Bpt) switch
        {
            (Iso20ControlMode.Dynamic, true) => new Ac20.BPT_Dynamic_AC_CLReqControlModeType(
                DepartureTime: 7200,
                EVTargetEnergyRequest: Rat(40, 3), EVMaximumEnergyRequest: Rat(60, 3), EVMinimumEnergyRequest: Rat(-20, 3),
                EVMaximumChargePower: Rat(300, 3), null, null, EVMinimumChargePower: Rat(100), null, null,
                EVPresentActivePower: Rat(200, 3), null, null, EVPresentReactivePower: Rat(20, 3), null, null,
                EVMaximumDischargePower: Rat(11, 3), null, null, EVMinimumDischargePower: Rat(1, -3), null, null,
                EVMaximumV2XEnergyRequest: null, EVMinimumV2XEnergyRequest: null),

            (Iso20ControlMode.Dynamic, false) => new Ac20.Dynamic_AC_CLReqControlModeType(
                DepartureTime: 7200,
                EVTargetEnergyRequest: Rat(40, 3), EVMaximumEnergyRequest: Rat(60, 3), EVMinimumEnergyRequest: Rat(-20, 3),
                EVMaximumChargePower: Rat(300, 3), null, null, EVMinimumChargePower: Rat(100), null, null,
                EVPresentActivePower: Rat(200, 3), null, null, EVPresentReactivePower: Rat(20, 3), null, null),

            (_, true) => new Ac20.BPT_Scheduled_AC_CLReqControlModeType(
                EVTargetEnergyRequest: null, EVMaximumEnergyRequest: null, EVMinimumEnergyRequest: null,
                EVMaximumChargePower: Rat(2_200, 1), null, null, EVMinimumChargePower: Rat(0), null, null,
                EVPresentActivePower: Rat(2_000, 1), null, null, EVPresentReactivePower: null, null, null,
                EVMaximumDischargePower: Rat(2_200, 1), null, null, EVMinimumDischargePower: Rat(0), null, null),

            _ => new Ac20.Scheduled_AC_CLReqControlModeType(
                EVTargetEnergyRequest: null, EVMaximumEnergyRequest: null, EVMinimumEnergyRequest: null,
                EVMaximumChargePower: Rat(2_200, 1), null, null, EVMinimumChargePower: Rat(0), null, null,
                EVPresentActivePower: Rat(2_000, 1), null, null, EVPresentReactivePower: null, null, null),
        };
    }

    /// <summary>The DC half: DC charge-parameter discovery, CableCheck/PreCharge, the DC charge loop
    /// and WeldingDetection.</summary>
    public sealed class Dc20SessionDriver(Secc20Dc secc,
                                          ushort serviceId = 2,
                                          Iso20ControlMode mode = Iso20ControlMode.Scheduled)
        : Iso20SessionDriver(secc, serviceId, mode)
    {
        private Dc20.MessageHeaderType Header => Ev.ToDcHeader();
        private static Dc20.RationalNumberType Rat(short value, sbyte exponent = 0) => new(exponent, value);

        /// <summary>Service 6 is DC_BPT.</summary>
        private bool Bpt => serviceId is 6;

        protected override void ChargeParameterDiscovery()
        {
            Dc20.DC_CPDReqEnergyTransferModeType mode = Bpt
                ? new Dc20.BPT_DC_CPDReqEnergyTransferModeType(
                      Rat(5_000, 1), Rat(0), Rat(200), Rat(0), Rat(500), Rat(50), TargetSOC: 80,
                      EVMaximumDischargePower: Rat(5_000, 1), EVMinimumDischargePower: Rat(0),
                      EVMaximumDischargeCurrent: Rat(200), EVMinimumDischargeCurrent: Rat(0))
                : new Dc20.DC_CPDReqEnergyTransferModeType(
                      Rat(5_000, 1), Rat(0), Rat(200), Rat(0), Rat(500), Rat(50), TargetSOC: 80);

            Send(MessageSet.Iso20DC, new Dc20.DC_ChargeParameterDiscoveryReq(Header, mode));
        }

        protected override void PreChargeSequence()
        {
            Send(MessageSet.Iso20DC, new Dc20.DC_CableCheckReq(Header));
            Send(MessageSet.Iso20DC, new Dc20.DC_PreChargeReq(Header, Dc20.Processing.Finished, Rat(0), Rat(400)));
        }

        protected override void PostChargeSequence() =>
            Send(MessageSet.Iso20DC, new Dc20.DC_WeldingDetectionReq(Header, Dc20.Processing.Finished));

        public new Dc20SessionDriver ToChargeLoop()
        {
            base.ToChargeLoop();
            return this;
        }

        /// <summary>One charge-loop iteration, in the shape this session's control mode requires.</summary>
        public Dc20.DC_ChargeLoopRes ChargeLoop(bool meterInfoRequested = false) =>
            Send<Dc20.DC_ChargeLoopRes>(MessageSet.Iso20DC,
                new Dc20.DC_ChargeLoopReq(Header, null, meterInfoRequested, Rat(400), ControlMode()));

        private Dc20.CLReqControlModeType ControlMode() => (Mode, Bpt) switch
        {
            (Iso20ControlMode.Dynamic, true) => new Dc20.BPT_Dynamic_DC_CLReqControlModeType(
                DepartureTime: 7200,
                EVTargetEnergyRequest: Rat(40, 3), EVMaximumEnergyRequest: Rat(6_000, 1), EVMinimumEnergyRequest: Rat(-20_000),
                EVMaximumChargePower: Rat(300, 3), EVMinimumChargePower: Rat(100),
                EVMaximumChargeCurrent: Rat(300), EVMaximumVoltage: Rat(1_000), EVMinimumVoltage: Rat(10),
                EVMaximumDischargePower: Rat(11, 3), EVMinimumDischargePower: Rat(1, -3),
                EVMaximumDischargeCurrent: Rat(11), EVMaximumV2XEnergyRequest: null, EVMinimumV2XEnergyRequest: null),

            (Iso20ControlMode.Dynamic, false) => new Dc20.Dynamic_DC_CLReqControlModeType(
                DepartureTime: 7200,
                EVTargetEnergyRequest: Rat(40, 3), EVMaximumEnergyRequest: Rat(6_000, 1), EVMinimumEnergyRequest: Rat(-20_000),
                EVMaximumChargePower: Rat(300, 3), EVMinimumChargePower: Rat(100),
                EVMaximumChargeCurrent: Rat(300), EVMaximumVoltage: Rat(1_000), EVMinimumVoltage: Rat(10)),

            (_, true) => new Dc20.BPT_Scheduled_DC_CLReqControlModeType(
                null, null, null, EVTargetCurrent: Rat(120), EVTargetVoltage: Rat(400),
                null, null, null, null, null, null, null, null),

            _ => new Dc20.Scheduled_DC_CLReqControlModeType(
                null, null, null, Rat(120), Rat(400), null, null, null, null, null),
        };
    }
}
