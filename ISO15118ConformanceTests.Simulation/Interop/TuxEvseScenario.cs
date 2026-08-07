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

using System.Text.Json;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>One transaction of a tux-evse scenario: a request their injector will send, in order.</summary>
/// <param name="Uid">Their own label, usually the packet number the transaction came from (<c>pkg:51</c>).</param>
/// <param name="Verb">Their vocabulary, e.g. <c>iso2:session_setup_req</c>.</param>
/// <param name="Message">The same message in ours (<c>SessionSetupReq</c>), or <c>null</c> when their verb
/// is not one we have evidence for. Never guessed — see <see cref="TuxEvseScenario.Vocabulary"/>.</param>
/// <param name="IsInjectorOnly">Marked <c>injector_only</c>: it never reaches the wire as a V2G message
/// (SDP discovery, the app-protocol setup their binder does for itself).</param>
/// <param name="DelayMillis">The capture's own gap before this message.</param>
internal sealed record ScenarioTransaction(String  Uid,
                                           String  Verb,
                                           String? Message,
                                           Boolean IsInjectorOnly,
                                           Int32   DelayMillis);


/// <summary>
/// A tux-evse scenario file, read as what it is: <b>a declared sequence of ISO 15118 messages, lifted out
/// of a packet capture</b>.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of that project worth the most to us, and it is not about bytes. Their EXI comes from
/// cbexigen, the same generator as our own vector corpus, so a byte disagreement is nearly impossible by
/// construction. Their <i>scenario</i>, on the other hand, is a real car's message sequence with the real
/// gaps between the messages — which is precisely the layer a corpus of single messages cannot see, and
/// where <c>docs/CONCEPT.md</c> §1.3 puts all ~15 of the conformance fixes that live interop has ever
/// found.
/// </para>
/// <para>
/// So the scenario file is read here as an <b>expected flow</b> and compared against what actually crossed
/// the wire (<see cref="SessionFlow"/>). Their own pass/fail is a different question — it compares
/// response <i>fields</i> against the captured charger's values, which our station is entitled to differ
/// on. The order of the messages is not something anybody is entitled to differ on.
/// </para>
/// </remarks>
internal sealed class TuxEvseScenario
{

    /// <summary>
    /// Their verb vocabulary, mapped to ours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every entry here comes from a scenario they ship (<c>audi-dc-iso2-compact.json</c>). It is not a
    /// mechanical snake_case conversion and must not be written as one: <c>payment_selection_req</c> is
    /// <c>PaymentServiceSelectionReq</c>, <c>param_discovery_req</c> is
    /// <c>ChargeParameterDiscoveryReq</c>, and <c>app_proto_req</c> is the SupportedAppProtocol handshake.
    /// Three names that a conversion function would have got wrong, silently.
    /// </para>
    /// <para>
    /// A verb that is not in this table is reported as unknown rather than guessed at. The messages -2 has
    /// that this table does not — PaymentDetails, CertificateInstallation, CertificateUpdate,
    /// MeteringReceipt, ServiceDetail — are absent because their shipped scenario is a DC
    /// EIM session and does not contain them, so their spelling for those is unknown. Filling them in from
    /// a pattern would be inventing a counterparty's vocabulary and then testing against the invention.
    /// </para>
    /// <para>
    /// <c>charging_status_req</c> was in that list until 2026-08-07 and left it the way the rule says it
    /// must: <b>their</b> tools produced the spelling, not ours. Their <c>pcap-iso15118</c> emitted it
    /// converting the Porsche AC captures, and their injector printed it back in its own TAP output
    /// (<c>ok 0008 - iso2:charging_status_req</c>). Before that no AC session had ever reached the charge
    /// loop against us, so the verb had never been seen — and while it was missing, every AC flow report
    /// counted the station's own <c>ChargingStatusReq</c> as a divergence from the scenario.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<String, String> Vocabulary = new Dictionary<String, String>
    {
        ["app_proto_req"]         = "SupportedAppProtocolReq",
        ["session_setup_req"]     = "SessionSetupReq",
        ["service_discovery_req"] = "ServiceDiscoveryReq",
        ["payment_selection_req"] = "PaymentServiceSelectionReq",
        ["authorization_req"]     = "AuthorizationReq",
        ["param_discovery_req"]   = "ChargeParameterDiscoveryReq",
        ["cable_check_req"]       = "CableCheckReq",
        ["pre_charge_req"]        = "PreChargeReq",
        ["power_delivery_req"]    = "PowerDeliveryReq",
        ["charging_status_req"]   = "ChargingStatusReq",
        ["current_demand_req"]    = "CurrentDemandReq",
        ["welding_detection_req"] = "WeldingDetectionReq",
        ["session_stop_req"]      = "SessionStopReq",
    };


    public String                              Name         { get; }
    public IReadOnlyList<ScenarioTransaction>  Transactions { get; }

    /// <summary>Verbs this build has no mapping for, in the order met, without duplicates. Named in the
    /// report rather than dropped: a scenario exercising a message we did not know they could send is a
    /// finding about coverage, not a parse error.</summary>
    public IReadOnlyList<String>               UnknownVerbs { get; }

    /// <summary>
    /// The messages their injector will put on the wire, in order.
    /// </summary>
    /// <remarks>
    /// Selected by "does this verb name a V2G message", not by their <c>injector_only</c> flag. The two
    /// come apart in exactly one place and it matters: <c>sdp_evse_req</c> is injector-only <i>and</i> not
    /// a V2G message — SDP is UDP discovery and never appears in a V2GTP recording — while
    /// <c>app_proto_req</c> is injector-only and very much does cross the wire, because a session cannot
    /// start without the handshake. Filtering on the flag would have made the SupportedAppProtocol
    /// exchange look like something our EVCC sent unprompted.
    /// </remarks>
    public IReadOnlyList<String> ExpectedMessages
        => Transactions.Where (t => t.Message is not null)
                       .Select(t => t.Message!)
                       .ToList();


    private TuxEvseScenario(String name, IReadOnlyList<ScenarioTransaction> transactions,
                            IReadOnlyList<String> unknownVerbs)
    {
        Name         = name;
        Transactions = transactions;
        UnknownVerbs = unknownVerbs;
    }


    /// <summary>
    /// Reads both shapes they produce: the shipped files, which wrap the scenarios in a
    /// <c>binding</c> array, and <c>pcap-iso15118</c>'s output, which has them at the top level.
    /// </summary>
    public static TuxEvseScenario Parse(String json)
    {

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var scenarios = new List<JsonElement>();

        if (root.TryGetProperty("scenarios", out var top) && top.ValueKind == JsonValueKind.Array)
            scenarios.AddRange(top.EnumerateArray());

        if (root.TryGetProperty("binding", out var bindings) && bindings.ValueKind == JsonValueKind.Array)
            foreach (var binding in bindings.EnumerateArray())
                if (binding.TryGetProperty("scenarios", out var nested) && nested.ValueKind == JsonValueKind.Array)
                    scenarios.AddRange(nested.EnumerateArray());

        if (scenarios.Count == 0)
            throw new InvalidDataException(
                "no scenarios in this file — expected either a top-level 'scenarios' array " +
                "(pcap-iso15118 output) or one under 'binding' (their shipped files).");

        var transactions = new List<ScenarioTransaction>();
        var unknown      = new List<String>();
        var names        = new List<String>();

        foreach (var scenario in scenarios)
        {

            names.Add(Text(scenario, "uid") ?? "<unnamed>");

            if (!scenario.TryGetProperty("transactions", out var list) || list.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var transaction in list.EnumerateArray())
            {

                var verb = Text(transaction, "verb");
                if (verb is null)
                    continue;

                // 'iso2:session_setup_req' in their shipped files, bare in pcap output.
                var bare         = verb[(verb.IndexOf(':') + 1)..];
                var injectorOnly = transaction.TryGetProperty("injector_only", out var only) &&
                                   only.ValueKind == JsonValueKind.True;

                String? message = null;
                if (Vocabulary.TryGetValue(bare, out var mapped))
                    message = mapped;
                else if (!injectorOnly && !unknown.Contains(bare))
                    unknown.Add(bare);

                transactions.Add(new ScenarioTransaction(
                    Text(transaction, "uid") ?? "<no uid>",
                    verb,
                    message,
                    injectorOnly,
                    transaction.TryGetProperty("delay", out var delay) && delay.TryGetInt32(out var ms) ? ms : 0));

            }

        }

        return new TuxEvseScenario(String.Join(" + ", names), transactions, unknown);

    }


    public static TuxEvseScenario ReadFrom(String path)
        => Parse(File.ReadAllText(path));


    private static String? Text(JsonElement element, String property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
               ? value.GetString()
               : null;

}
