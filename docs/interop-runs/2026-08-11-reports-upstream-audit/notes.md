# 2026-08-11 — every citation in `docs/reports/` re-checked, and five findings caught up with

**No rig was started.** Two mechanical passes over the thirty-two drafts in
[`docs/reports/`](../../reports/README.md) and the counterparty trees behind them: first *do the line
numbers still point where they say*, then *has upstream fixed it since we wrote it down*.

The second question is the one that produced results, and it is one this project had been asking per
report and never across all of them at once.

## Pass 1 — the citations

Every `file:line` in the directory, resolved against the checked-out counterparty tree and compared to
what the surrounding report claims is on that line.

**189 citations. All 189 still correct.** Two did not resolve and neither is a defect:
`ssl/statem/statem_srvr.c:3751` is OpenSSL's own tree, quoted in the PaymentDetails crash report as
the caller, and `iso15118-network-rs/src/ipv6-tcp.rs:66-75` is a tux-evse repository that is not
checked out here — the simulator is, the network layer is not.

Two apparent misses were the checker's fault, not the reports':

- `iso15118/evcc/states/iso15118_20_states.py:1934-1940` is out of range in **upstream** Josev (1795
  lines) because the report cites it at the **fork**, `EVerest/ext-switchev-iso15118` @ `26f7988`,
  where the file has 1965 lines and those seven read exactly what the report quotes. The report's own
  checklist already said which tree; the body repeats the path without repeating the tree, which is
  worth a second's thought before posting.
- `types/evse_security.hpp:1056` is a **generated** header, under `build/generated/`, which a source
  tree walk skips. It says what the report says it says.

The trees this was resolved against: `everest-core` @ `2026.02.1` (`b61bb12`) plus its
`build/generated` and its vendored `build/_deps/josev-src` @ `26f7988`, `SwitchEV/iso15118` @
`d645255`, `EDF-Lab/eVDriveFlow` @ `60249c3`, `EVerest/libcbv2g` @ `03350be`, `tux-evse` and
`FlUxIuS/V2Gdecoder` @ `2ee5bd9`.

## Pass 2 — has anybody fixed it since?

Upstream default branches, fetched today:

| project | pinned / cited | upstream today | moved |
|---|---|---|---|
| `EDF-Lab/eVDriveFlow` | `60249c3` | `60249c3` (2023-04-17) | no — **3 y 4 m** |
| `SwitchEV/iso15118` | `d645255` | `d645255` (2026-05-19) | no |
| `EVerest/ext-switchev-iso15118` | `26f7988` | `26f7988` (2026-05-04) | no |
| `EVerest/libcbv2g` | `03350be` | `03350be` (2025-11-10) | no |
| `tux-evse/*` | `fc51088`, `f1ab338` | same | no |
| `FlUxIuS/V2Gdecoder` | `2ee5bd9` | `2ee5bd9` (2026-07-28) | no |
| **`EVerest/everest-core`** | **`2026.02.1` = `b61bb12`** | **`ebcd36d` (2026-08-11)** | **yes** |

So for five of the six counterparties the drafts are written against the current HEAD and nothing
needed re-reading. everest-core is the exception, and **seventeen** of the drafts go to it.

Each EVerest finding was then re-tested at its cited path on `main` — the marker that makes the defect
true, not the line number.

### Three are fixed

| report | `2026.02.1` | `main` |
|---|---|---|
| [`everest-iso20-ac-contactor-latch`](../../reports/everest-iso20-ac-contactor-latch.md) | `ac_connector_closed = control_data;` | `ac_connector_closed = *control_data;` |
| [`everest-loop-shutdown`](../../reports/everest-loop-shutdown.md) | `log_and_raise_openssl_error("Failed to SSL_accept(): …")` | `result_t::closed:` → `logf_error(…); this->close(); return;` |
| [`everest-d20-ac-namespace`](../../reports/everest-d20-ac-namespace.md) | both namespaces into the map unconditionally | `is_dc = … and modes.dc`, `is_ac = … and modes.ac`, and only then into the map |

The contactor one is a single character. The handshake one is a better fix than the report proposed —
it folds every fatal handshake outcome into one teardown instead of scoping the accept call, and the
comment in their tree says so. The namespace one is `[V2G20-169]`'s filter-before-ranking, implemented,
with a log line — *"Selecting supported app protocol namespace based on the supported energy
services"* — that reads like somebody thinking about exactly the requirement the draft cites.

**Nobody was told.** These are drafts; none has been sent. So all three were found on their side
independently, while ours sat here — the loop-shutdown one since 2026-08-05, the contactor one since
2026-08-09.

Which is worth being precise about, because it cuts two ways. It is **not** evidence that filing would
have helped: they got there without us. What it *is* evidence for is that the findings were real —
three drafts written from a source reading and a probe, and in each case the maintainers independently
concluded the same thing and changed the code. That is the strongest external check this directory has
ever had on its own judgement, and it applies to the twelve that are still open: they were produced by
the same method.

What it does **not** show is a cost of not having sent them. The first draft of these notes claimed one
— three fixes sitting in one tree while the other stayed broken — and that claim depended on the stale
mirror mattering. It does not; see *A wrong turn* below. Two of the three are simply closed.

### One is half-overtaken

[`everest-d20-ocsp-absent`](../../reports/everest-d20-ocsp-absent.md) is built on three absences. On
`main`, **two of them are filled**: `SSLConfig` grew a `chains` vector whose `ChainConfig` carries
`ocsp_response_files`, and `connection_ssl.cpp` forwards them into `lib/everest/tls` — the
implementation the draft points at as the one that already works. What remains is the module passing
`include_ocsp = false` and an explicitly empty list, `{}, // ocsp_response_files — none for the
single-chain leaf path`.

The **measurement would be unchanged** — still no staple — but the report as written would be answered
with "that is not where it lives any more", and rightly.

### One had its open question answered

[`everest-d20-client-auth`](../../reports/everest-d20-client-auth.md) §1 asks, in its own checklist,
whether the TLS 1.2 path is deliberate. On `main` the mechanism moved into
`lib/everest/tls/src/tls.cpp:1001-1017` behind a flag named **`verify_client_on_tls13`**, with a comment
explaining it. So: deliberate, and the draft has to argue against a design decision rather than report
an oversight. §2 is unaffected — no sigalg list, no group preference, no client-CA list at the new
location either.

### The other twelve are unchanged on `main`

Sequence timeout (still one 60 s constant), SessionID-zero (still `!= 0`), CertificateUpdate (still the
`TODO`), renegotiation/CableCheck, the metering chain (both halves), the PaymentDetails ordering, the
session-log length, the `to_everest` OCSP drop, the `-20` RNG, the `-20` meter info, the MO trust
anchor, and `IsoMux` (including the ignored header-read result, verbatim). Those drafts are current.

## The part worth keeping

**A wrong turn, corrected the same day, and the correction is the lesson.**

`EVerest/libiso15118` and everest-core's copy of it have diverged.
[`everest-iso20-ac-contactor-latch`](../../reports/everest-iso20-ac-contactor-latch.md) carries an open
checklist item saying its file is byte-identical in both trees — 5663 bytes, same SHA-256 — and asking
which one to file against. It is not byte-identical any more:

| | `everest-core/lib/everest/iso15118` | standalone `EVerest/libiso15118` @ `main` (`5c81c92`, 2025-11-25) |
|---|---|---|
| contactor latch | fixed | still shows the defect |
| TLS accept throw | fixed | still shows the defect |
| AC namespace filter | fixed | still shows the defect |

This audit read that table and concluded: *file all three against the standalone library as
cherry-pick requests.* **That was wrong.** The standalone repository is **not maintained** —
everest-core's `lib/everest/iso15118/` is the live tree, and the mirror's right-hand column is an
artefact of nobody touching it since 2025-11-25, not a live finding. There is nobody there to
cherry-pick. All three are simply **fixed**, and the correct action for two of them is *do not file*.

The mistake is worth writing down because the evidence was **internally consistent and wrong**. A git
remote that answers, has a `main`, and returns the file you asked for looks exactly like a maintained
tree. Nothing in the byte comparison could have said otherwise; only knowing the project could.
`check_upstream.py --lib` still runs, and its output has been demoted from *status* to *history* in
the tool's README.

What survives from the three: [`everest-loop-shutdown`](../../reports/everest-loop-shutdown.md), the
only one with a live defect left in it — the **trigger** is fixed on `main`, the **structure** is not.
`TbdController::loop()` still ends the accept loop on any throw out of `poll()`.

It was **rewritten the same day**, and reading `main` for the rewrite paid twice. It found **eight
throw sites still reaching the loop** — SDP read failures, both `accept()` paths, the handshake
`default:` arm, and six in the `TlsKeyLoggingServer` constructor, which runs inside the accept path
behind a config flag. And it found a **second** fix nobody here knew about: a malformed SDP datagram
no longer throws, and the fix carries the comment `// FIXME (aw): we should not die here immediately`.
That is this report's argument in a maintainer's words, already in their file — seventeen lines below
a call in the same function that still does what it warns against.

Two things that could have gone wrong in the rewrite and did not, both caught by reading rather than
grepping: `log_and_throw("Failed to parse sdp header")` is still in the file and looks like a live
unauthenticated trigger — it is inside `#if 0`. And six of the `sdp_server.cpp` throw sites are in a
constructor, not a per-datagram path, which changes what they are worth claiming.

**The divergence is per file, not wholesale.**
[`everest-d20-rng-entropy`](../../reports/everest-d20-rng-entropy.md) makes the same byte-identical
claim about `authorization_setup.cpp` and it still holds — `std::mt19937 generator(rd())` is in both
trees and, more to the point, in everest-core `main`. Three files moved. That report files once,
against everest-core.

## What moves

Nothing in the interop matrix — no session was run and no capability changed. What moved is the
backlog of unsent filings:

- **Two retired**: [contactor latch](../../reports/everest-iso20-ac-contactor-latch.md) and
  [AC namespace](../../reports/everest-d20-ac-namespace.md) are fixed upstream, nothing to send. Kept
  in the directory because the runs behind them stand as facts about `2026.02.1`.
- **One re-pitched and rewritten**: [loop-shutdown](../../reports/everest-loop-shutdown.md) leads with
  the loop instead of the handshake, cites both of their own fixes as agreement, and tables the eight
  sites that still reach it. It is the one report here that cites **two revisions** — 2026.02.1 for
  what was measured, `main` for what survives — and labels every citation with its tree.
- **Two need their argument rewritten** before they are sendable:
  [ocsp-absent](../../reports/everest-d20-ocsp-absent.md) and
  [client-auth](../../reports/everest-d20-client-auth.md).
- **Twelve confirmed current**, verbatim on `main`.

Eleven checklist items answered across ten drafts, and
[`reports/README.md`](../../reports/README.md) records the pass.

## Reproduce

```bash
# the roots go in as TREE_* variables; see the tool's README
python3 tools/reports-audit/check_citations.py
python3 tools/reports-audit/check_upstream.py          # EVerest/everest-core @ main
python3 tools/reports-audit/check_upstream.py --lib    # EVerest/libiso15118 @ main
```

The citation count moves as reports are edited — this pass covered the 189 that existed on the day,
and the status boxes added since have taken it to 191.
