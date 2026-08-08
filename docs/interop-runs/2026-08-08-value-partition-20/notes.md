# 2026-08-08 — the last eight, measured instead of attributed

**Result: all eight remaining `-20` length differences are the EXI value partition, shown by
substitution rather than by arithmetic. Nothing in the corpus is now unexplained.**

After the [ACDP and WPT decision](../2026-08-08-schema-conformant-acdp-wpt/notes.md) the `-20` corpus
stood at 339 of 347 byte-exact, with eight frames EXIficient re-encodes shorter than we write them.
They were all *attributed* to the string table — seven `ServiceDetailRes` and one `AuthorizationReq` —
and the run notes said so honestly: **"Not confirmed to the byte, unlike the `-2` case."** The
`AuthorizationReq` delta was 34 bytes against a 35-character URI, off by one, and nobody had explained
the one.

That is now measured. The one turned out to be real, and so did something nobody had looked for.

| | |
|---|---|
| Tool | [`tools/interop-exificient/valuepartition20.py`](../../../tools/interop-exificient/valuepartition20.py) |
| Method | substitute every repeat with a same-length unique value; their encoding must land on our length |
| Corpus | the 7 `-20` session traces, 240 frames, 8 of them mismatching |
| Offline guard | [`Interop/ExiStringTable20Tests.cs`](../../../ISO15118ConformanceTests.Simulation/Interop/ExiStringTable20Tests.cs) |

## Why substitution and not arithmetic

Our encoder is deliberately miss-only: it always writes the literal, never a compact identifier. So
replacing a repeated value with a *different value of the same length* cannot change our output at all,
and removes theirs. If the repeats are the whole difference, then

```
len(their encoding of the substituted document)  ==  len(our original frame)
```

exactly. Any residue is something else, and its size is the finding. This is the same experiment that
closed `-2`; what is new is doing it per repeated value as well as for all of them at once.

## What it found

**All eight: accounted for, to the byte.** Seven `ServiceDetailRes` (138 B against their 95) and one
`AuthorizationReq` (913 against 879) all come back to our own length once the repeats are gone.

```
-- Session.iso20-dc-pnc.trace.json#05.res ServiceDetailRes
     repeated x2,  17 chars: MobilityNeedsMode  -> that repeat is worth 17 B to them
     repeated x2,  11 chars: ControlMode        -> that repeat is worth 11 B to them
     repeated x2,   9 chars: Connector          -> that repeat is worth  9 B to them
     repeated x2,   7 chars: Pricing            -> that repeat is worth  6 B to them
     ours 138 B, theirs 95 B, delta 43
     repeats cost us 44 B if each second occurrence is a literal
     substituted: 138 B against our 138 B  ->  ACCOUNTED FOR
```

### The identifier is not free — which is the "off by one"

Look at the last line of the four: `Pricing` is seven characters and the repeat is worth **six**. Three
of the four are worth exactly their length and one is a byte short. Nothing is wrong with it — a
compact identifier occupies bits of its own, and whether that shows up as a whole byte depends on where
the run lands in the packing.

Which is exactly the `AuthorizationReq` mystery: the URI is 35 characters and the repeat is worth
**34**. The `-2` case happened to come out even, so the naive subtraction worked there, and carrying it
over here is what produced an "off by one" that was never an anomaly. **The sum of string lengths is
not the prediction; the substitution is the measurement.**

### A repeated certificate is worth nothing at all

The finding nobody was looking for. `AuthorizationReq` repeats *two* values, and the other one is a
400-character certificate:

```
     repeated x2, 400 chars: MIIBJjCBzaADAgECAgkA4zZ5NwvweswwCgYIKoZIzj0EAwIwGTEXM...
                             -> that repeat is worth 0 B to them
     repeated x2,  35 chars: http://www.w3.org/TR/canonical-exi/
                             -> that repeat is worth 34 B to them
```

Zero. If both repeats were hits the naive cost would be 435 bytes and the observed delta is 34, which
is the arithmetic that never added up. It adds up now: the EXI value partitions hold **string** values,
and both occurrences sit under `Certificate`, whose `certificateType` is `xs:base64Binary` — encoded as
a Binary datatype, never entering the table, no identifier possible for anyone.

**Worth stating positively: the largest values ISO 15118 puts on the wire cost our miss-only encoder
nothing.** Certificates and chains, which dominate every Plug & Charge message, are outside the string
table entirely. What miss-only costs us is repeated *short strings* — and the worst case in the whole
corpus is 43 bytes on a 138-byte message.

## One measurement error, caught and corrected

The first run of this reported `RESIDUE -2 B` on `AuthorizationReq` — their substituted encoding two
bytes *longer* than ours — and that was the tool's fault, not a finding. The substitution replaced the
last four characters of the 400-character certificate, `==` padding included. A base64 value's decoded
length depends on that padding, so the binary grew by two bytes and so did the encoding. Varying the
characters *before* any padding fixes it, and the residue goes to zero.

Recorded because it is the shape of mistake this method is prone to: "same length" has to mean same
length *in the domain the encoder measures*, and for `base64Binary` that is octets, not characters.

## What is now pinned offline

The rig half needs Java and stays a tool. The half that can run in `dotnet test` is the one that
matters most — **reading** the compact form:
[`ExiStringTable20Tests`](../../../ISO15118ConformanceTests.Simulation/Interop/ExiStringTable20Tests.cs)
takes EXIficient's `ServiceDetailRes` with all four identifiers, decodes it, re-encodes it, and demands
our own 138 octets back. That is the direction the risk runs in: writing literals costs bytes and
nothing else, but any peer whose encoder uses the partitions *will* send us identifiers, and before
this run no `-20` frame from a foreign encoder had ever exercised that path.

## Reproducing

```bash
python3 tools/interop-exificient/valuepartition20.py
```

## Where the `-20` corpus stands

| | |
|---|---:|
| byte-exact | 339 of 347 |
| length differences, all explained | 8 |
| unreadable by an independent codec | 0 |
| unexplained | **0** |

## Next

- **The same question for `-2`.** `ExiStringTableTests` pins two frames and the naive arithmetic worked
  there; after today that looks like luck rather than a rule. Running `valuepartition20.py`'s method
  over the `-2` corpus would either confirm the two numbers or find the same one-byte effect hiding in
  them.
- **Nothing else.** For the first time since this oracle was set up, the `-20` corpus has no open
  question in it.
