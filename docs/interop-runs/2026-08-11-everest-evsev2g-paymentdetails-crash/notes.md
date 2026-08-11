# 2026-08-11 — a use-before-check in `EvseV2G`'s PaymentDetails crashes the module on a malformed contract cert

Fourth EVerest finding of the day and the first that is a **crash** rather than a conformance point.
It came out of the same thread as the other three: our `-2` stack learned Plug & Charge this morning
(`WWCP_ISO15118` `c1a7989`), which is what makes a `PaymentDetailsReq` with a contract certificate a
thing this project can now send — and reading their handler for that message turned up a null
dereference reachable pre-authentication.

| | |
|---|---|
| Kind | Remotely-triggerable denial of service (null-pointer dereference). **No ISO clause** — a robustness bug, stands on the crash and its reachability |
| Read | [everest-core](https://github.com/EVerest/everest-core) **2026.02.1** (`b61bb12b8`), `EvseV2G`; contrast against [SwitchEV/iso15118](https://github.com/SwitchEV/iso15118) `d645255` and our own `Secc2` |
| Measured | the crash, **in isolation** — `certificate_subject`'s first two lines on a null `X509*`; not yet against a running station |
| Outcome | EVerest **crashes**, Josev and ours **catch and answer `FAILED`**. Filed: [`everest-evsev2g-paymentdetails-crash.md`](../../reports/everest-evsev2g-paymentdetails-crash.md) |
| Artifacts | [`nullderef.c`](nullderef.c) · [`repro.log`](repro.log) |

## The bug in one sentence

`handle_iso_payment_details` parses the EV's contract certificate at `iso_server.cpp:982`, **uses** it
at `:990` (`getEmaidFromContractCert(contract_crt)`), and only **checks** whether the parse succeeded
at `:1006` — sixteen lines too late. On unparseable DER the parse returns a null `certificate_ptr`, so
line 990 runs `certificate_subject(nullptr)`.

Chain, each link one grep:

- `parse_contract_certificate` → `der_to_certificate` returns a null pointer on failed `d2i_X509`
  (`openssl_util.cpp:653-663`), and `-1`.
- `getEmaidFromContractCert(crt)` is `certificate_subject(crt.get())` (`crypto_openssl.cpp:166`).
- `certificate_subject` opens `assert(cert != nullptr)` then calls `X509_get_subject_name(cert)`
  (`openssl_util.cpp:774-776`).

The `bytesLen != 0` guard at line 981 rejects an *empty* certificate field but nothing between empty
and valid — any non-empty garbage passes it and fails the parse.

## Why it is a security finding, not a style note

**The peer is unauthenticated when it happens.** ISO 15118-2 TLS is unilateral — the SECC presents a
certificate, the EV does not — and the crash is *during parsing*, before any signature or chain
validation. So the sequence is: `-2` TLS handshake (server-auth only), SAP, `PaymentServiceSelection`
selecting `Contract`, then one `PaymentDetailsReq` whose `ContractSignatureCertChain.Certificate` is
non-empty rubbish. No credential, no authorization, one crafted message, module down — repeatably.

That is a harder version of the [loop-shutdown finding](../../reports/everest-loop-shutdown.md): there a
downed accept loop stalls silently; here the process crashes, and a front-end that a single packet can
cycle is one an attacker can hold down in a loop.

## Measured — both build modes crash

Their CMake defaults to a `Debug` build, so the `assert` is usually live; a release build compiles it
out and the crash lands one call deeper. The isolated reproduction runs `certificate_subject`'s first
two lines on a null `X509*` and shows both:

```
### release semantics (-DNDEBUG): assert compiled out, crash reaches OpenSSL
  Segmentation fault (core dumped)      exit=139   (128 + SIGSEGV)
### debug semantics (assert live): the assert fires first
  nd_dbg: Assertion `cert != NULL' failed.
  Aborted (core dumped)                 exit=134   (128 + SIGABRT)
```

`X509_get_subject_name` dereferences its argument with no null check — confirmed by the SIGSEGV, which
is inside OpenSSL, not in the assert. So a release build (the reproduction's `-DNDEBUG` arm) is not
saved by the assert; it just crashes differently.

## The three-stack contrast

The reason this is filable rather than a shrug is that the other two stacks meet the identical bytes
and answer:

- **Josev** wraps the whole `PaymentDetails.process_message` body in one `try:`
  (`iso15118_2_states.py:944`); the certificate is parsed inside `verify_certs`, whose failure is a
  caught exception that becomes a `FAILED` response.
- **Ours** loads the cert with `X509CertificateLoader.LoadCertificate` inside `try { … } catch
  (Exception ex)` (`Secc2.PaymentDetails`), so bad DER throws `CryptographicException` and is recorded,
  never dereferenced.

Both parse defensively; only `EvseV2G` reaches OpenSSL with a null. And EVerest's own intent for this
input is `FAILED_CertChainError` — it is set at every neighbouring error exit in the same function. One
misplaced check is the whole distance between "answer FAILED" and "crash".

## The fix is one reordering

Move the `err != 0` test (and a `contract_crt == nullptr` belt) above line 990. It is the same
response the surrounding code already produces; it just has to run before the certificate is touched.
Separately, `certificate_subject` guarding null only with an `assert` means any future caller on an
unchecked parse hits the same wall — a defensive early return there is cheap insurance, though the
ordering is the real fix.

## What this does not decide

- **Not run against a live station.** The crash is isolated and the source path is exact, but nobody
  has sent the frame to a running `EvseV2G`. Our `-2` PnC EVCC (new this morning) can do it over TLS;
  the filing's first unticked item is that probe, and it expects the process to exit.
- **Not a controllable write.** The pointer is null, not attacker-chosen. Severity is availability, not
  integrity or code execution — the report says so rather than leaving it to be inferred the scary way.
- **Recovery depends on supervision**, which was not tested. everest's manager may restart the module;
  the in-progress session on that connection dies regardless, and a loop of the frame is a sustained
  DoS whatever the restart policy.
- **DIN and the `-20` module were not checked** for the same shape. `EvseV2G`'s DIN path and
  `libiso15118` parse certificates too; whether either has an equivalent use-before-check is a separate
  read.

## Reproduce

```bash
cd docs/interop-runs/2026-08-11-everest-evsev2g-paymentdetails-crash
cc -O2 -DNDEBUG nullderef.c -o nd && ./nd     # SIGSEGV (release semantics)
cc -O2         nullderef.c -o nd && ./nd     # SIGABRT via assert (debug semantics)
```

The C file reproduces `certificate_subject`'s first two lines on a null `X509*`; it links only
`-lcrypto` and needs no everest build. Against a running station the trigger is a `PaymentDetailsReq`
carrying a non-empty, non-certificate `ContractSignatureCertChain.Certificate` over a `-2` Contract
session — which is the probe still owed.
