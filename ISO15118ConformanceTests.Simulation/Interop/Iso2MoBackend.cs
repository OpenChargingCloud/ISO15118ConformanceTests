/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using cloud.charging.open.protocols.ISO15118_2;
using cloud.charging.open.protocols.ISO15118_2.Generated;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// Stands in as the <b>mobility operator / certificate provisioning backend</b> behind a charging
/// station: takes the EXI of an ISO 15118-2 <c>CertificateInstallationReq</c>, issues a contract, and
/// hands back the EXI of the signed <c>CertificateInstallationRes</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> EVerest's <c>EvseV2G</c> does not answer a provisioning request itself — it
/// publishes the EV's EXI on its <c>iso15118_extensions</c> interface and waits <b>4 500 ms</b> for
/// somebody to publish a response back (<c>iso_server.cpp:30</c>,
/// <c>V2G_SECC_MSG_CERTINSTALL_TIME</c>). Their SIL ships nobody, so the run on 2026-08-11 could only
/// prove the <i>forward</i> half: the request reaches their bus byte-identical, and then the session
/// dies on the timeout. This closes the loop by being the missing half, which turns "their plumbing
/// carries our bytes" into "their whole path works".
/// </para>
/// <para>
/// <b>It answers with our own station's issuer, deliberately.</b> Nothing here re-implements
/// provisioning: it drives a real <see cref="Secc2"/> to the phase where a
/// <c>CertificateInstallationReq</c> is the expected next message and hands it the request their
/// station forwarded. The contract, the ECDH-wrapped private key and the four-reference response
/// signature are the ones a loopback session would have produced — so what the run measures is
/// <i>their</i> transport, with a known-good answer travelling through it.
/// </para>
/// <para>
/// <b>The bridge is theirs, not ours.</b> MQTT is spoken by their own <c>mosquitto_sub</c>/<c>_pub</c>
/// in a shell script (<c>tools/interop-everest/mo-backend-bridge.sh</c>), which drops the base64
/// request in a directory and picks the base64 answer up. This project has no MQTT client and does not
/// need one for a single request-response.
/// </para>
/// </remarks>
internal static class Iso2MoBackend
{

    public const String RequestFile  = "request.b64";
    public const String ResponseFile = "response.b64";

    /// <summary>
    /// Waits for one request to appear in <paramref name="directory"/>, answers it, and returns a
    /// one-line description of what was issued — or null if none arrived before cancellation.
    /// </summary>
    public static async Task<String?> RunOnceAsync(String directory, CancellationToken ct)
    {

        Directory.CreateDirectory(directory);

        var requestPath  = Path.Combine(directory, RequestFile);
        var responsePath = Path.Combine(directory, ResponseFile);

        // Left over from an earlier run, either of them, would answer this one instantly with the wrong
        // bytes — and the bridge would publish it inside the window, so nothing would look wrong.
        File.Delete(requestPath);
        File.Delete(responsePath);

        Byte[]? exi = null;
        while (!ct.IsCancellationRequested)
        {
            if (File.Exists(requestPath))
            {
                // The bridge writes then renames, so a partial read is not expected — but a base64 body
                // that does not decode is worth one retry rather than an exception.
                try { exi = Convert.FromBase64String((await File.ReadAllTextAsync(requestPath, ct)).Trim()); }
                catch (FormatException) { await Task.Delay(20, ct); continue; }
                break;
            }
            await Task.Delay(25, ct);
        }

        if (exi is null)
            return null;

        var (responseExi, description) = Answer(exi);

        // Written to a temporary name and moved, for the same reason the bridge does: the reader must
        // never see half a file, and the window is 4,5 s wide.
        var temporary = responsePath + ".tmp";
        await File.WriteAllTextAsync(temporary, Convert.ToBase64String(responseExi), ct);
        File.Move(temporary, responsePath, overwrite: true);

        return description;

    }


    /// <summary>Issues the contract: decodes the request, drives a station to the provisioning phase and
    /// returns its answer, encoded.</summary>
    private static (Byte[] Exi, String Description) Answer(Byte[] requestExi)
    {

        var request = (V2G_Message) Iso2Codec.DecodeAny(requestExi, out _);

        if (request.Body.BodyElement is not CertificateInstallationReqType install)
            throw new InvalidOperationException(
                $"the station forwarded a {request.Body.BodyElement?.GetType().Name ?? "(null)"}, " +
                 "not a CertificateInstallationReq.");

        // The session id is the station's, and the answer has to carry it back. FixedSessionId is the
        // recording seam; here it is what makes an out-of-band issuer speak inside somebody else's
        // session at all.
        var sessionId = request.Header.SessionID;

        var secc = new Secc2(PowerMode.Dc, TimeSpan.FromSeconds(60), TimeProvider.System)
        {
            FixedSessionId         = sessionId,
            OfferCertificateService = true,
        };

        V2G_Message Wrap(BodyBaseType body) =>
            new(new MessageHeaderType(sessionId, Notification: null, Signature: null), new BodyType(body));

        // Three messages to reach the phase in which a CertificateInstallationReq is expected. They are
        // synthesized rather than replayed: their station already ran this part of the session with the
        // real car, and this station exists only to issue the contract.
        secc.Handle(Wrap(new SessionSetupReqType(new Byte[] { 1 })));
        secc.Handle(Wrap(new ServiceDiscoveryReqType(null, null)));
        secc.Handle(Wrap(new PaymentServiceSelectionReqType(
                             PaymentOption.ExternalPayment,
                             new SelectedServiceListType(new[]
                             {
                                 new SelectedServiceType(1, null),
                                 new SelectedServiceType(Secc2.CertificateServiceId,
                                                         Secc2.CertificateInstallationParameterSetId),
                             }))));

        // The request goes in **as it arrived**, header and all. Re-wrapping it in a fresh header — which
        // this did on its first attempt — drops the car's own signature, and the issuer then reports
        // that the car proved nothing while happily issuing anyway. The session id already matches,
        // because FixedSessionId above was taken from this very header.
        var response = secc.Handle(request);

        if (response.Body.BodyElement is not CertificateInstallationResType issued)
            throw new InvalidOperationException(
                $"our own station answered a {response.Body.BodyElement?.GetType().Name ?? "(null)"}.");

        var buffer = new Byte[8192];
        if (!Iso2Codec.TryEncode(response, buffer, out var length))
            throw new InvalidOperationException("the CertificateInstallationRes did not encode.");

        var verdict = secc.CertInstall;
        var description =
            $"issued {issued.EMAID.Value} to {verdict?.ReceiverSubject ?? "?"}; " +
            $"the car's own signature {(verdict?.SignatureOk == true ? "verified" : "did NOT verify")}, " +
            $"answer wrapped for its key: {(verdict?.EncryptedForReceiver == true ? "yes" : "NO")}; " +
            $"{length} EXI bytes back";

        TestContext.Out.WriteLine($"MO backend: {description}");

        return (buffer.AsSpan(0, length).ToArray(), description);

    }

}
