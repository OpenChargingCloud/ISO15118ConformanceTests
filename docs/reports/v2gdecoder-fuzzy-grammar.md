# Draft report to FlUxIuS (V2Gdecoder) — a decode that cannot fail returns a wrong answer instead

Status: **draft, not sent.** Observed 2026-08-07 against the **v1.1 release jar** (the one
`tools/docker/decoder/Dockerfile` downloads), OpenJDK 21, Debian 13, with the schema sets shipped in
the repository. Post under your own name; see *Before sending* at the bottom.

Two observations, **A** and **B**. They are separate filings: A is grammar *selection*, B is a gap in
the DIN *grammar*, and a fix for either leaves the other standing. They are reported together only
because A is what turns B from a visible error into an invisible one.

First, the context they come out of, because it matters for how seriously to take them. We used
V2Gdecoder as an independent EXI oracle for a conformance suite — 287 frames across two corpora:
186 ISO 15118-2 and SupportedAppProtocol frames, and 101 distinct frames from a captured DIN 70121
session (a Tesla Model 3 at a public charge point). **It read 285 of them correctly**, and 283 of those
also re-encoded — decode to XML, encode back to EXI — to the original octets exactly. That includes
signed Plug & Charge messages, certificate chains, and a complete DC session in a protocol whose
schemas most tools do not ship.

The two frames that did not re-encode identically are not defects and are not reported here: your
encoder used the EXI value partition for a string that occurs twice in the document, where ours writes
the literal a second time. Your output is the shorter one and, as far as we can tell, the better one.
The two issues below are the actual exceptions, and we would not have found either without leaning on
the tool hard enough to trust it.

---

# Issue A — `fuzzyExiDecoded` returns the first grammar that does not throw, so an ambiguous frame is resolved by array position

**Title:** A frame that is valid under two grammars is decoded by whichever comes first in the array,
with no indication that anything was ambiguous

**Version:** V2Gdecoder v1.1 release jar; `dataprocess.fuzzyExiDecoded`, `V2Gdecoder.java:61-78`.

## Summary

`80 40 80` is a three-byte `supportedAppProtocolRes` — `OK_SuccessfulNegotiation`, `SchemaID` absent.
V2Gdecoder decodes it as something else entirely:

```
$ java -jar decoder.jar -e -s 804080
<?xml version="1.0" encoding="UTF-8"?><ns6:Entry xmlns:ns6="urn:iso:15118:2:2013:MsgDataTypes" …>
  <ns6:TimeInterval/></ns6:Entry>
```

An `Entry` from `MsgDataTypes`. Re-encoding that XML through `-x` yields 56 bytes, which `-e` then
cannot read back at all — so the round trip does not merely change the document, it leaves the tool
unable to parse its own output.

## It is not "short frames are ambiguous"

That was our first guess and it is wrong, which is worth showing because it narrows the report. The
other two three-byte responses in the same family decode perfectly:

| input | decoded as |
|---|---|
| `80 40 80` | ❌ `MsgDataTypes:Entry` with an empty `TimeInterval` |
| `80 44 80` | ✅ `supportedAppProtocolRes` — `OK_SuccessfulNegotiationWithMinorDeviation` |
| `80 48 80` | ✅ `supportedAppProtocolRes` — `Failed_NoNegotiation` |
| `80 40 00 40` | ✅ `supportedAppProtocolRes` — `OK_SuccessfulNegotiation`, `SchemaID 1` |

`80 40 80` is simply the one octet string in that set which happens to be well-formed under **both**
grammars. Nothing about its length or its size is the problem.

## The correct grammar was never reached

`V2Gdecoder.java:61-78` builds the array in a fixed order — `[0]` MsgDef, `[1]` AppProtocol,
`[2]` xmldsig — and `fuzzyExiDecoded` tries them in that order, returning the first that does not throw.
Since the paths come from the filesystem, the array can be rigged to test the hypothesis directly.
Same jar, same schemas, only the array position changed:

```bash
cp schemas/V2G_CI_AppProtocol.xsd schemas/V2G_CI_MsgDef.xsd    # AppProtocol now sits at grammars[0]
java -jar decoder.jar -e -s 804080
<ns4:supportedAppProtocolRes …><ResponseCode>OK_SuccessfulNegotiation</ResponseCode></…>
```

Correct. So the frame was always readable by a grammar V2Gdecoder already had loaded; the MsgDef
grammar just answered first.

## Why we think it is worth fixing

Not because the ordering is wrong — any ordering has this property, and reordering would only move the
ambiguity to a different frame. It is worth fixing because **the failure is silent**. A wrong decode is
indistinguishable from a right one in the output: same exit code, same shape, plausible XML. Anyone
scripting over a capture (which is exactly what your README's `tshark … | curl … | tidy` pipeline
does) gets a field tree that looks fine and is not.

`supportedAppProtocolRes` with `OK` and no `SchemaID` is not an exotic frame, either. It is what a
station sends whenever it accepts a car's single-protocol offer without echoing the ID — ordinary
traffic, and the first exchange of every session.

## Suggested fix

Three shapes, and which belongs in your tree is your call:

1. **Let the caller pin the grammar** — a `-g/--grammar msgdef|appproto|xmldsig` flag. Cheapest, and it
   solves the problem outright for anyone converting a capture, who usually knows what they are looking
   at. This is the one we would have used.
2. **Report ambiguity instead of hiding it.** Try all three; if more than one decodes, say so on stderr
   and pick by the current order. Costs two extra decodes on a path that already rebuilds grammars per
   invocation.
3. **Prefer the grammar whose decode is not degenerate** — an `Entry` carrying nothing but an empty
   `TimeInterval` is a weak match for three input bytes. Heuristic, and we would not suggest it alone.

We would not suggest simply reordering the array.

## How to reproduce

```bash
java -jar V2Gdecoder.jar -e -s 804080     # wrong
java -jar V2Gdecoder.jar -e -s 804480     # right
java -jar V2Gdecoder.jar -e -s 804880     # right
```

---

# Issue B — the DIN grammar cannot decode a real `ChargeParameterDiscoveryRes`

**Title:** `schemas_din` rejects a DIN 70121 `ChargeParameterDiscoveryRes` captured from a production
charge point; because of A, the failure surfaces as an xmldsig `SignatureValue`

**Version:** as above, with `schemas_din/` staged as `./schemas`.

## Summary

From a real DIN session — a Tesla Model 3 at `DE*PNX*E12345*1` — the station's answer to
`ChargeParameterDiscoveryReq`, 64 bytes:

```
809a02202c9fff3e37b3d3d0800000040020000405182824138550008000018180c80c1c241380c142101c0c0c0
000c142d0040606002060600003090904e000
```

comes back as:

```xml
<ns4:SignatureValue xmlns:ns4="http://www.w3.org/2000/09/xmldsig#">EA==</ns4:SignatureValue>
```

64 bytes of a DIN response read as a one-byte xmldsig fragment.

## The grammar rejects it; the fallback answers for it

The DIN grammar loads and works — every other message type in the same session decodes correctly
through it, including `SessionSetupReq`, `ServiceDiscoveryRes`, `CableCheckReq`, `PreChargeRes`,
`CurrentDemandReq`/`Res` and `SessionStopRes`. To confirm that this frame is the exception rather than
the fallback being greedy, we put the DIN MsgDef grammar at **both** non-xmldsig array positions:

```bash
cp schemas/V2G_CI_MsgDef.xsd schemas/V2G_CI_AppProtocol.xsd   # grammars[0] and [1] are both DIN now
```

A DIN `SessionSetupReq` still decodes; this frame still comes back as `SignatureValue`. Both DIN
attempts threw, and `grammars[2]` answered in their place.

## The frame is sound

Decoded by tux-evse's `pcap-iso15118` (cbexigen-based, DIN-capable), the same 64 bytes are an
unremarkable station answer:

```
rcode ok · PMax schedule 10 kW over 86,400 s
EVSE DC: max 900 V, min 180 V, max 25 A, ripple 0 A, max 10 kW,
         regulation tolerance 1 A, energy to deliver 10 kWh,
         isolation_status invalid   ← the cable check has not run yet
```

So the message is well-formed DIN from production equipment, and the gap is in the shipped
`schemas_din` set. We have not diagnosed which construct it stumbles on — that is your schema and your
grammar builder, and guessing at it from outside would only waste your time. The frame is the useful
part, and it is above verbatim.

## Why we think it is worth fixing

`ChargeParameterDiscoveryRes` is where a DC station states its power envelope: the voltage and current
limits, the power cap, the schedule. In a DIN capture it is the single most informative message, and no
session reaches a charge loop without one. A DIN capture converted with V2Gdecoder is therefore missing
precisely the frame someone opened the capture to read.

And note the interaction with A: had the decode simply failed, this would have been a five-minute
diagnosis. Because the xmldsig fallback answered, it took a byte-level comparison against a second
implementation to notice anything was wrong at all. **A is what makes B expensive.**

## How to reproduce

```bash
cp -r schemas_din /tmp/din/schemas && cd /tmp/din
java -jar V2Gdecoder.jar -e -s 809a02000000000000000011d01b71109c77351400   # SessionSetupReq — fine
java -jar V2Gdecoder.jar -e -s 809a02202c9fff3e37b3d3d08000000400200004051828241385500080000\
18180c80c1c241380c142101c0c0c0000c142d0040606002060600003090904e000        # this one
```

The capture itself is public: `afb-test/trace-logs/tesla-3-din.pcap` in
[tux-evse/iso15118-simulator-rs](https://github.com/tux-evse/iso15118-simulator-rs).

---

## Also worth saying, and not a defect

Running with the wrong schema set does not fail either — a DIN `SessionSetupReq` read against the
ISO-2 set comes back as an ISO-2 `WeldingDetectionReq` with `EVReady false`, `EVErrorCode Reserved_B`,
`EVRESSSOC 0`. Entirely plausible, entirely wrong. That is the same mechanism as A rather than a third
issue, and it is arguably the user's fault for pointing the tool at the wrong directory — but it is
worth a line in the README, because the two schema sets are one `cd` apart and nothing in the output
says which one produced it.

---

## Before sending

- [x] **Reproduce it with their tool alone.** Done — their jar, their schemas, their release. Nothing
      of ours is in either reproduction; issue A needs only three bytes.
- [x] **Rule out the obvious wrong explanation.** "Short frames are ambiguous" is false: two other
      three-byte responses decode correctly, and the table above is the reason to believe A is about
      one octet string and not about size.
- [x] **Prove the claim rather than infer it.** The AppProtocol-at-`grammars[0]` substitution shows the
      frame was always readable; the DIN-at-both-positions substitution shows the DIN grammar is what
      rejects the other one.
- [x] **Check the source citations.** `V2Gdecoder.java:61-78` and `dataprocess.fuzzyExiDecoded` read
      against `master` on 2026-08-07.
- [ ] **Lead with what worked.** 285 of 287 frames, including a complete DIN session round-tripped
      byte-exact — this is a report from someone who found the tool good enough to depend on, and the
      opening should say so before the two things it got wrong.
- [ ] **File A and B separately.** A fix for either leaves the other standing. Cross-reference them:
      the sentence worth carrying is that A is what made B expensive to find.
- [ ] **Offer the `--grammar` flag only if they want it.** Three shapes are sketched above and the
      choice is an architecture question for a tool whose whole premise is that it guesses well.
- [ ] **Say where the frames come from.** The DIN capture is tux-evse's and public; the SAP vectors are
      ours. Neither needs anything of ours to reproduce, but the provenance should be stated rather
      than left to be asked.
- [ ] **Post under your own name, in your own words.**
