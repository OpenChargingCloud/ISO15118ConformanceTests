using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Tests.Traces;

namespace Vanaheimr.V2G.Simulation.Tests.Interop;

/// <summary>
/// Reading a session as a flow, and comparing it against the flow a counterparty's scenario declares.
/// </summary>
/// <remarks>
/// <para>
/// Offline, and about the layer interop is actually for. The scenario documents below are written here
/// rather than vendored — their files are Apache-2.0 and could be checked in, but a comparison whose only
/// input is a copy of their file would be checking that a copy still parses. What is checked instead is
/// the shape their format has (both of them), the three verbs whose names a mechanical conversion would
/// get wrong, and the alignment itself against a session we recorded.
/// </para>
/// <para>
/// The last test is the one worth reading: it says what a real run should produce, before there has been
/// one.
/// </para>
/// </remarks>
[TestFixture]
public class SessionFlowTests
{

    /// <summary>
    /// Their shipped shape, with the verb order of <c>audi-dc-iso2-compact.json</c>: a DC EIM session
    /// captured from an Audi against an ABB charger, compacted so each unique request appears once.
    /// </summary>
    private const String ShippedShape = """
    {
      "binding": [{
        "uid": "iso15118-simulator",
        "compact": "basic",
        "scenarios": [{
          "uid": "audi-dc-iso2:1",
          "timeout": 748,
          "transactions": [
            { "uid": "sdp-evse",         "verb": "iso2:sdp_evse_req", "injector_only": true,
              "query": { "action": "discover" } },
            { "uid": "app-set-protocol", "verb": "iso2:app_proto_req", "injector_only": true },
            { "uid": "pkg:51",   "verb": "iso2:session_setup_req",     "delay": 56,
              "expect": { "id": "DE*PNX*E12345*1", "rcode": "new_session" } },
            { "uid": "pkg:56",   "verb": "iso2:service_discovery_req", "delay": 111,
              "expect": { "rcode": "ok", "transfers": ["dc_extended"], "payments": ["external"] } },
            { "uid": "pkg:60",   "verb": "iso2:payment_selection_req", "delay": 40 },
            { "uid": "pkg:64",   "verb": "iso2:authorization_req",     "delay": 40 },
            { "uid": "pkg:70",   "verb": "iso2:param_discovery_req",   "delay": 40 },
            { "uid": "pkg:120",  "verb": "iso2:cable_check_req",       "delay": 40 },
            { "uid": "pkg:200",  "verb": "iso2:pre_charge_req",        "delay": 40 },
            { "uid": "pkg:322",  "verb": "iso2:power_delivery_req",    "delay": 100 },
            { "uid": "pkg:400",  "verb": "iso2:current_demand_req",    "delay": 30 },
            { "uid": "pkg:4156", "verb": "iso2:power_delivery_req",    "delay": 30 },
            { "uid": "pkg:4200", "verb": "iso2:welding_detection_req", "delay": 30 },
            { "uid": "pkg:4300", "verb": "iso2:session_stop_req",      "delay": 30 }
          ]
        }]
      }]
    }
    """;

    /// <summary>What <c>pcap-iso15118</c> writes: scenarios at the top level, verbs without the prefix.</summary>
    private const String PcapShape = """
    {
      "uid": "./afb-test/trace-logs/abb-normal-din.pcap",
      "api": "pcap-simu",
      "scenarios": [{
        "uid": "scenario-1",
        "target": "iso15118-din",
        "transactions": [
          { "uid": "pkg:42", "verb": "session_setup_req", "delay": 16,
            "expect": { "rcode": "ok", "tagid": "session_setup_res" } },
          { "uid": "pkg:44", "verb": "some_future_req" }
        ]
      }]
    }
    """;


    private static SessionTrace Corpus(String name)
        => SessionTrace.ReadFrom(Path.Combine(TestContext.CurrentContext.TestDirectory,
                                              "Vectors", $"Session.{name}.trace.json"));


    [Test]
    public void BothOfTheirFileShapesAreRead()
    {
        var shipped = TuxEvseScenario.Parse(ShippedShape);
        var pcap    = TuxEvseScenario.Parse(PcapShape);

        Assert.Multiple(() =>
        {
            Assert.That(shipped.Name,         Is.EqualTo("audi-dc-iso2:1"));
            Assert.That(shipped.Transactions, Has.Count.EqualTo(14));
            Assert.That(pcap.Name,            Is.EqualTo("scenario-1"));
            Assert.That(pcap.Transactions,    Has.Count.EqualTo(2));

            // The prefix is theirs, not part of the verb.
            Assert.That(pcap.ExpectedMessages, Is.EqualTo(new[] { "SessionSetupReq" }));
        });
    }


    /// <summary>
    /// The three names a snake_case-to-PascalCase converter would have got wrong.
    /// </summary>
    /// <remarks>
    /// This is why <c>Vocabulary</c> is a table of things seen in their files rather than a function.
    /// A converter would have produced PaymentSelectionReq, ParamDiscoveryReq and AppProtoReq — three
    /// messages that do not exist — and the comparison would then have reported them missing from every
    /// session for ever, which reads exactly like a real finding.
    /// </remarks>
    [Test]
    public void TheVerbsAMechanicalConversionWouldGetWrongAreMappedByHand()
    {
        var scenario = TuxEvseScenario.Parse(ShippedShape);

        Assert.Multiple(() =>
        {
            Assert.That(scenario.ExpectedMessages, Does.Contain("PaymentServiceSelectionReq"));
            Assert.That(scenario.ExpectedMessages, Does.Contain("ChargeParameterDiscoveryReq"));
            Assert.That(scenario.ExpectedMessages, Does.Contain("SupportedAppProtocolReq"));
        });
    }


    /// <summary>
    /// SDP is injector-only and never a frame; the app-protocol handshake is injector-only and always one.
    /// </summary>
    [Test]
    public void TheHandshakeIsExpectedOnTheWireAndSdpIsNot()
    {
        var scenario = TuxEvseScenario.Parse(ShippedShape);

        Assert.Multiple(() =>
        {
            Assert.That(scenario.ExpectedMessages[0], Is.EqualTo("SupportedAppProtocolReq"));
            Assert.That(scenario.ExpectedMessages,    Has.None.Contains("Sdp"));
            Assert.That(scenario.UnknownVerbs,        Has.None.EqualTo("sdp_evse_req"),
                        "a verb that is injector-only and maps to nothing is not an unknown message");
        });
    }


    [Test]
    public void AVerbWithNoMappingIsNamedRatherThanGuessedAt()
    {
        var scenario = TuxEvseScenario.Parse(PcapShape);

        Assert.Multiple(() =>
        {
            Assert.That(scenario.UnknownVerbs,     Is.EqualTo(new[] { "some_future_req" }));
            Assert.That(scenario.ExpectedMessages, Has.Count.EqualTo(1), "and it is left out of the comparison");
        });
    }


    [Test]
    public void AFileWithNoScenariosIsRefused()
        => Assert.Throws<InvalidDataException>(() => TuxEvseScenario.Parse("""{"binding":[{"uid":"x"}]}"""));


    // ── the alignment ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void ConsecutiveRepeatsCollapseWithTheirCount()
        => Assert.That(SessionFlow.Collapse(["A", "B", "B", "B", "C", "B"]),
                       Is.EqualTo(new[] { ("A", 1), ("B", 3), ("C", 1), ("B", 1) }));


    /// <summary>
    /// One inserted message must not make everything after it look wrong.
    /// </summary>
    [Test]
    public void OneExtraMessageIsOneDivergence()
    {
        var aligned = SessionFlow.Align(["A", "X", "B", "C"], ["A", "B", "C"]);

        Assert.Multiple(() =>
        {
            Assert.That(aligned.Count(x => x.Kind != FlowDiff.Same), Is.EqualTo(1));
            Assert.That(aligned.Single(x => x.Kind == FlowDiff.Extra).Message, Is.EqualTo("X"));
        });
    }


    [Test]
    public void AMessageThatNeverCameIsReportedAsMissing()
    {
        var aligned = SessionFlow.Align(["A", "C"], ["A", "B", "C"]);

        Assert.That(aligned.Single(x => x.Kind == FlowDiff.Missing).Message, Is.EqualTo("B"));
    }


    [Test]
    public void IdenticalFlowsHaveNoDivergences()
        => Assert.That(SessionFlow.Align(["A", "B"], ["A", "B"]).All(x => x.Kind == FlowDiff.Same), Is.True);


    // ── the two together ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What a -2 DC run against their shipped scenario should produce — worked out before there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Our recorded DC EIM session and their captured Audi session are the <b>same sequence of messages</b>:
    /// handshake, setup, service discovery, payment selection, authorization, charge-parameter discovery,
    /// cable check, pre-charge, power delivery, current demand, power delivery, welding detection, session
    /// stop. So a clean run should report no divergence in the order, and anything else is worth a look.
    /// </para>
    /// <para>
    /// The one difference that is <i>not</i> a finding is the count: our session polls CurrentDemand while
    /// their compacted scenario names it once. That is what <see cref="SessionFlow.Collapse"/> is for, and
    /// this test is where it is proved to work on a real session rather than on a list of letters.
    /// </para>
    /// </remarks>
    [Test]
    public void OurRecordedDcSessionMatchesTheOrderTheirAudiScenarioDeclares()
    {

        var trace  = Corpus("iso2-dc-eim");
        var frames = trace.Exchanges.Select(e => e.Request.Bytes).ToList();

        var report = SessionFlow.Report(frames,
                                        trace.Exchanges.Select(e => e.Response.Bytes).ToList(),
                                        TuxEvseScenario.Parse(ShippedShape));

        Assert.That(report, Does.Contain("The order matches the declared flow exactly."),
                    $"the flows should agree; the report was:{Environment.NewLine}{report}");

        // And the poll loop is reported as a count, not as divergences.
        Assert.That(report, Does.Contain("CurrentDemandReq:").And.Contain("in the scenario"));

    }


    /// <summary>The same comparison, with a message removed from the wire — it has to bite.</summary>
    [Test]
    public void AMissingPhaseShowsUpAsADivergence()
    {

        var trace  = Corpus("iso2-dc-eim");

        // Drop the cable check, the way a station that skipped a phase would.
        var frames = trace.Exchanges
                          .Where (e => !e.Request.Message.StartsWith("CableCheck", StringComparison.Ordinal))
                          .Select(e => e.Request.Bytes)
                          .ToList();

        var report = SessionFlow.Report(frames, [], TuxEvseScenario.Parse(ShippedShape));

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain("CableCheckReq   (in the scenario, never on the wire)"));
            Assert.That(report, Does.Contain("1 divergence(s) in the order."));
        });

    }

}
