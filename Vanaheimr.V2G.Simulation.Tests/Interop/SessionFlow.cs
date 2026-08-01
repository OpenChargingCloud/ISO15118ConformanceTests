using System.Text;

using Vanaheimr.V2G.Simulation.Tests.Traces;
using Vanaheimr.V2G.Tp;

namespace Vanaheimr.V2G.Simulation.Tests.Interop;

/// <summary>One frame, as something to read rather than as bytes.</summary>
internal sealed record FlowStep(Int32 Index, String PayloadType, String Message, String? ResponseCode, Int32 Bytes);


/// <summary>How a step of the recorded flow relates to the expected one.</summary>
internal enum FlowDiff
{
    /// <summary>In both, in this order.</summary>
    Same,
    /// <summary>On the wire, not in the expected flow.</summary>
    Extra,
    /// <summary>In the expected flow, never on the wire.</summary>
    Missing,
}


/// <summary>
/// The high-level shape of a session: which messages, in which order, with which response codes — and
/// where that differs from the flow a counterparty's scenario declared.
/// </summary>
/// <remarks>
/// <para>
/// This is the layer interop is actually for. A vector corpus pins single messages and a trace pins one
/// implementation's whole session, but neither can see the thing two independent stacks most often
/// disagree about: the order, the phase transitions, the polling, the terminal state. Those are also
/// where every live-interop conformance fix this project has ever earned turned out to live
/// (<c>docs/CONCEPT.md</c> §1.3).
/// </para>
/// </remarks>
internal static class SessionFlow
{

    /// <summary>
    /// Names each frame of one direction.
    /// </summary>
    /// <param name="firstIsSap">Whether the first frame of this direction is the SupportedAppProtocol
    /// handshake, which shares payload id <c>0x8001</c> with every -2 message and is told apart by
    /// position. True for a whole session; false for a recording that began later.</param>
    public static IReadOnlyList<FlowStep> Of(IReadOnlyList<Byte[]> frames, Boolean firstIsSap = true)
    {

        var steps = new List<FlowStep>();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            V2GTP.TryReadHeader(frame, out var payloadType, out _);
            var (message, responseCode) = FrameLabel.Describe(frame, isSap: firstIsSap && i == 0);
            steps.Add(new FlowStep(i, $"0x{payloadType:X4}", FrameLabel.Canonical(message), responseCode,
                                   frame.Length));
        }

        return steps;

    }


    /// <summary>
    /// Consecutive repeats collapsed to one entry and a count.
    /// </summary>
    /// <remarks>
    /// <b>The comparison is worthless without this.</b> A charging session polls — our recorded DC
    /// sessions send CurrentDemandReq until the loop ends — while their scenarios are <i>compacted</i>:
    /// their own documentation says <c>basic</c> plays unique request/query combinations once and
    /// <c>strong</c> plays unique requests once. So the same session is forty messages on the wire and one
    /// line in the scenario. A diff that did not know this would report the poll loop as thirty-nine
    /// insertions and bury whatever real difference there was underneath it.
    /// </remarks>
    public static IReadOnlyList<(String Message, Int32 Count)> Collapse(IEnumerable<String> messages)
    {

        var collapsed = new List<(String Message, Int32 Count)>();

        foreach (var message in messages)
            if (collapsed.Count > 0 && collapsed[^1].Message == message)
                collapsed[^1] = (message, collapsed[^1].Count + 1);
            else
                collapsed.Add((message, 1));

        return collapsed;

    }


    /// <summary>
    /// Aligns what happened against what was expected, on collapsed sequences.
    /// </summary>
    /// <remarks>
    /// A longest-common-subsequence alignment rather than a walk down both lists in step. One inserted
    /// message shifts everything after it, and a positional comparison would then call every remaining
    /// message wrong — the same failure the trace replayer avoids by stopping at the first divergence,
    /// except here we want to see the rest of the session rather than stop.
    /// </remarks>
    public static IReadOnlyList<(FlowDiff Kind, String Message)> Align(IReadOnlyList<String> actual,
                                                                       IReadOnlyList<String> expected)
    {

        var lengths = new Int32[actual.Count + 1, expected.Count + 1];

        for (var i = actual.Count - 1; i >= 0; i--)
            for (var j = expected.Count - 1; j >= 0; j--)
                lengths[i, j] = actual[i] == expected[j]
                                    ? lengths[i + 1, j + 1] + 1
                                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        var result = new List<(FlowDiff, String)>();
        var a = 0;
        var e = 0;

        while (a < actual.Count && e < expected.Count)
        {
            if (actual[a] == expected[e])
            {
                result.Add((FlowDiff.Same, actual[a]));
                a++; e++;
            }
            else if (lengths[a + 1, e] >= lengths[a, e + 1])
                result.Add((FlowDiff.Extra,   actual[a++]));
            else
                result.Add((FlowDiff.Missing, expected[e++]));
        }

        while (a < actual.Count)   result.Add((FlowDiff.Extra,   actual[a++]));
        while (e < expected.Count) result.Add((FlowDiff.Missing, expected[e++]));

        return result;

    }


    /// <summary>The whole report, as markdown, for pasting into an interop run's <c>notes.md</c>.</summary>
    public static String Report(IReadOnlyList<Byte[]> evToStation,
                                IReadOnlyList<Byte[]> stationToEv,
                                DeclaredFlow?         expected)
    {

        var requests  = Of(evToStation);
        var responses = Of(stationToEv);

        var report = new StringBuilder();

        report.AppendLine("# Session flow");
        report.AppendLine();
        report.AppendLine($"{requests.Count} request frame(s), {responses.Count} response frame(s).");
        report.AppendLine();
        report.AppendLine("No timings: the recorder keeps two octet streams and no clock, so the order within");
        report.AppendLine("each direction is real and the pairing below is by position.");
        report.AppendLine();
        report.AppendLine("| # | EV → station | station → EV | code |");
        report.AppendLine("|---|---|---|---|");

        for (var i = 0; i < Math.Max(requests.Count, responses.Count); i++)
        {
            var request  = i < requests.Count  ? requests[i]  : null;
            var response = i < responses.Count ? responses[i] : null;

            report.AppendLine($"| {i} " +
                              $"| {request?.Message  ?? "— (unanswered)"} " +
                              $"| {response?.Message ?? "— (no answer)"} " +
                              $"| {response?.ResponseCode ?? ""} |");
        }

        // A response code that is not the ordinary one is the single most useful line in an interop
        // write-up, and it is easy to miss in a table of thirty rows.
        var notable = responses.Where(s => s.ResponseCode is not null and not "OK" and not "OK_NewSessionEstablished")
                               .ToList();
        if (notable.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Response codes other than OK");
            report.AppendLine();
            foreach (var step in notable)
                report.AppendLine($"- `[{step.Index}] {step.Message}` → **{step.ResponseCode}**");
        }

        if (expected is not null)
            AppendScenarioComparison(report, requests, expected);

        return report.ToString();

    }


    private static void AppendScenarioComparison(StringBuilder report, IReadOnlyList<FlowStep> requests,
                                                 DeclaredFlow expected)
    {

        var actual   = Collapse(requests.Select(s => s.Message));
        var declared = Collapse(expected.Messages);
        var aligned  = Align(actual.Select(x => x.Message).ToList(),
                             declared.Select(x => x.Message).ToList());

        report.AppendLine();
        report.AppendLine($"## Against the declared flow — `{expected.Name}`");
        report.AppendLine();
        report.AppendLine($"Reference: {expected.Source}.");
        report.AppendLine();
        report.AppendLine("Consecutive repeats are collapsed on both sides: a session polls, and a compacted");
        report.AppendLine("scenario names each request once, so the counts are compared separately from the order.");
        report.AppendLine();

        foreach (var (kind, message) in aligned)
            report.AppendLine(kind switch
            {
                FlowDiff.Same    => $"      {message}",
                FlowDiff.Extra   => $"  +   {message}   (on the wire, not in the scenario)",
                _                => $"  -   {message}   (in the scenario, never on the wire)",
            });

        var counts = actual.Where(a => declared.Any(d => d.Message == a.Message && d.Count != a.Count))
                           .ToList();

        if (counts.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("Repeat counts (a difference here is usually their compaction, not a defect):");
            report.AppendLine();
            foreach (var (message, count) in counts)
                report.AppendLine($"- {message}: {count}× on the wire, " +
                                  $"{declared.First(d => d.Message == message).Count}× in the scenario");
        }

        if (expected.UnknownVerbs.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("Verbs this build has no mapping for, so they are absent from the comparison:");
            report.AppendLine();
            foreach (var verb in expected.UnknownVerbs)
                report.AppendLine($"- `{verb}`");
            report.AppendLine();
            report.AppendLine("Add them to `TuxEvseScenario.Vocabulary` once their spelling is confirmed —");
            report.AppendLine("guessing it from a pattern is how a comparison starts agreeing with itself.");
        }

        var divergences = aligned.Count(x => x.Kind != FlowDiff.Same);

        report.AppendLine();
        report.AppendLine(divergences == 0
                              ? "**The order matches the declared flow exactly.**"
                              : $"**{divergences} divergence(s) in the order.** Each one is a question for the " +
                                 "write-up: our state machine, their capture, or a real disagreement?");

    }

}
