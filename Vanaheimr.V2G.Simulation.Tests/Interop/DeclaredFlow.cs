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

using Vanaheimr.V2G.Simulation.Tests.Traces;

namespace Vanaheimr.V2G.Simulation.Tests.Interop;

/// <summary>
/// A sequence of messages a live run can be held against — whatever it came from.
/// </summary>
/// <remarks>
/// <para>
/// Two counterparties, two kinds of reference, and the comparison is the same either way.
/// </para>
/// <list type="bullet">
///   <item><b>Their scenario.</b> tux-evse ships transaction lists lifted from packet captures, so the
///         reference is <i>a real car's</i> route through a session.</item>
///   <item><b>Our own recording.</b> eVDriveFlow is a state machine and publishes no such file, so the
///         only reference available is one of our <c>Session.*.trace.json</c> corpus entries. Circular as
///         a correctness check and not meant as one: what it answers is "did this live session take the
///         same route as the one we recorded", which for a foreign peer is the interesting question. A
///         -20 session against a stack in Dynamic control mode has every reason to diverge, and the
///         divergence is the finding.</item>
/// </list>
/// </remarks>
/// <param name="Name">What the reference is called, for the report.</param>
/// <param name="Source">Where it came from, in words — a reader of an interop write-up should never have
/// to work out whether the expected column was somebody else's capture or our own recording.</param>
/// <param name="Messages">The messages expected on the wire, EV → station, in order.</param>
/// <param name="Responses">The same for station → EV, when the source declares them. Empty when it does
/// not, and then that half of the comparison is simply not made — which is better than comparing against
/// an assumption. <b>This half is the interesting one for a station-side counterparty</b>: against
/// EVerest's <c>EvseV2G</c> what is new is not what our car sends but what their charger answers.</param>
/// <param name="UnknownVerbs">Anything in the source this build could not map, named rather than dropped.</param>
internal sealed record DeclaredFlow(String                Name,
                                    String                Source,
                                    IReadOnlyList<String> Messages,
                                    IReadOnlyList<String> Responses,
                                    IReadOnlyList<String> UnknownVerbs)
{

    /// <summary>
    /// Reads whichever of the two a file happens to be.
    /// </summary>
    /// <remarks>
    /// Sniffed by structure rather than by file name or by an extra environment variable: one variable
    /// with two accepted contents is one thing to get right, and the two structures cannot be confused —
    /// a trace has <c>exchanges</c>, a scenario has <c>scenarios</c>. A file that is neither is refused
    /// by name, because the alternative is a comparison silently made against nothing.
    /// </remarks>
    public static DeclaredFlow FromFile(String path)
    {

        var json = File.ReadAllText(path);

        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("exchanges", out _))
                    return FromSessionTrace(json);

                if (root.TryGetProperty("scenarios", out _) || root.TryGetProperty("binding", out _))
                    return FromTuxEvseScenario(json);
            }
        }

        throw new InvalidDataException(
            $"'{path}' is neither a session trace (it has no 'exchanges') nor a tux-evse scenario " +
             "(no 'scenarios' and no 'binding'), so there is nothing to compare a run against.");

    }


    public static DeclaredFlow FromTuxEvseScenario(String json)
    {
        var scenario = TuxEvseScenario.Parse(json);

        return new DeclaredFlow(scenario.Name,
                                "a tux-evse scenario — a real session, captured and replayed",
                                scenario.ExpectedMessages,
                                // Req → Res is mechanical in ISO 15118's own naming, and this substitution
                                // stays inside *our* vocabulary rather than guessing at theirs: the request
                                // name came from the hand-kept table, and only the suffix is changed.
                                scenario.ExpectedMessages
                                        .Select(m => m.EndsWith("Req", StringComparison.Ordinal)
                                                         ? $"{m[..^3]}Res"
                                                         : m)
                                        .ToList(),
                                scenario.UnknownVerbs);
    }


    public static DeclaredFlow FromSessionTrace(String json)
    {
        var trace = SessionTrace.FromJson(json);

        return new DeclaredFlow($"{trace.Name} ({trace.Protocol}, {trace.Mode})",
                                "our own recorded session — the route this stack takes, not a conformance claim",
                                trace.Exchanges.Select(e => FrameLabel.Canonical(e.Request.Message)).ToList(),
                                trace.Exchanges.Select(e => FrameLabel.Canonical(e.Response.Message)).ToList(),
                                []);
    }

}
