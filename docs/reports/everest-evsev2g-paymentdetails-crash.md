# Draft report to EVerest (`EvseV2G`) — a malformed contract certificate in `PaymentDetailsReq` crashes the V2G module (null dereference)

Status: **draft, not sent** — and **reproduced against a running station on 2026-08-15**, twice. One
frame from an unauthenticated peer aborts the module, and their manager then terminates every other
module and exits. This is a **remotely-triggerable denial of service**; treat it as the security report
it is, and see *Before sending* about how to send it. Post it under your own name.

Evidence in this repository:
[`2026-08-15-everest-paymentdetails-crash-live`](../interop-runs/2026-08-15-everest-paymentdetails-crash-live/notes.md)
— the live runs, their manager's logs, the config as run, with a control arm — and
[`2026-08-11-everest-evsev2g-paymentdetails-crash`](../interop-runs/2026-08-11-everest-evsev2g-paymentdetails-crash/notes.md),
the source reading and a self-contained C reproduction.

---

**Title:** `handle_iso_payment_details` uses the parsed contract certificate before checking whether the
parse succeeded, so an unparseable `ContractSignatureCertChain.Certificate` reaches
`certificate_subject(nullptr)` — SIGABRT in a debug build, SIGSEGV in a release build

**Version:** everest-core **2026.02.1** (`b61bb12b8`), `modules/EVSE/EvseV2G/`.

## The defect

`handle_iso_payment_details`, for a `Contract` (Plug & Charge) session:

```cpp
// modules/EVSE/EvseV2G/iso_server.cpp — line numbers are 2026.02.1
978  certificate_ptr contract_crt{nullptr, nullptr};
981  if (req->ContractSignatureCertChain.Certificate.bytesLen != 0) {
982      err = parse_contract_certificate(contract_crt,
                 req->ContractSignatureCertChain.Certificate.bytes,
                 req->ContractSignatureCertChain.Certificate.bytesLen);
     } else {
         res->ResponseCode = iso2_responseCodeType_FAILED_CertChainError;
         goto error_out;
     }

990  auto cert_emaid = getEmaidFromContractCert(contract_crt);   // <-- uses contract_crt
     …                                                           //     (emaid compare, etc.)
1006 if (err != 0) {                                             // <-- err checked here, too late
         memset(res, 0, sizeof(*res));
         res->ResponseCode = iso2_responseCodeType_FAILED_CertChainError;
         goto error_out;
     }
```

The guard at line 981 checks only that the certificate field is **non-empty**, not that it is valid
DER. When the bytes are non-empty but not a certificate:

- `parse_contract_certificate` (`crypto/crypto_openssl.cpp:161`) calls `der_to_certificate`, which on
  a failed `d2i_X509` returns a **null** `certificate_ptr` (`lib/everest/tls/src/openssl_util.cpp:653`),
  and returns `-1`. So `contract_crt.get() == nullptr` and `err == -1`.
- Line 990 then calls `getEmaidFromContractCert(contract_crt)`, which is
  `certificate_subject(crt.get())` — i.e. **`certificate_subject(nullptr)`**.
- `certificate_subject` (`openssl_util.cpp:774`) opens `assert(cert != nullptr)` and then calls
  `X509_get_subject_name(cert)`.

`err` — the value that says the parse failed — is not looked at until line 1006, sixteen lines after
the certificate it describes has already been used.

## What happens, in both build modes

`X509_get_subject_name` dereferences its argument; OpenSSL does not null-check it. So:

| build | `assert` | outcome |
|---|---|---|
| debug (their CMake **default** is `Debug`) | live | **SIGABRT** at the assert in `certificate_subject` |
| release (`-DNDEBUG`) | compiled out | **SIGSEGV** in `X509_get_subject_name(nullptr)` |

Either way the `EvseV2G` process dies. Reproduced in isolation — the C program in the run directory
runs `certificate_subject`'s first two lines on a null `X509*`:

```
### release semantics (-DNDEBUG): assert compiled out, crash reaches OpenSSL
  Segmentation fault (core dumped)      exit=139
### debug semantics (assert live): the assert fires first
  nd_dbg: Assertion `cert != NULL' failed.
  Aborted (core dumped)                 exit=134
```

## On your own station, twice

2026-08-15, everest-core **2026.02.1** (`b61bb12`), your `-2` Plug & Charge SIL config with **one line
changed** (`tls_security: allow` → `force`), your own PKI, nothing of ours on the wire but the frames.
Your log, both runs identical but for timestamps and pid:

```
13:48:46  [INFO] iso15118_charge :: SelectedPaymentOption: Contract
          iso15118_charger:EvseV2G: …/lib/everest/tls/src/openssl_util.cpp:775:
              openssl::certificate_subject(const X509*): Assertion `cert != nullptr' failed.
13:48:47  [CRIT] manager :: Module iso15118_charger (pid: 2802) exited with status: 134.
                            Terminating all modules.
13:48:47  [CRIT] manager :: Exiting manager.
```

Status **134** is SIGABRT, at the assert on the line this report named four days before the run. The
sequence was TCP connect → TLS 1.2 (server-auth only, **we presented no client certificate**) → SAP →
`SessionSetup` → `ServiceDiscovery` → `PaymentServiceSelection(Contract)` → `PaymentDetailsReq`. Five
frames, no credential of any kind.

**Two arms, and the only variable is whether the bytes parse.** Both send a certificate you have no
reason to accept:

| arm | `ContractSignatureCertChain.Certificate` | your station |
|---|---|---|
| **control** | a well-formed self-signed P-256 leaf, chaining to nothing | **answered `OK`**, stayed up |
| **probe** | 64 random bytes, leading `0xFF` — cannot begin a DER `SEQUENCE` | **SIGABRT** |
| **liveness** | the control's certificate again, three seconds later | `Connection refused` |

**And the third row is the part we would most want you to see.** This report used to say the module dies
and that your manager *may* restart it. It does not: your own two lines are *Terminating all modules*
and *Exiting manager*. One frame from an unauthenticated peer takes the whole charger down, and it does
not come back.

## Who can trigger it, and why that is the concerning part

The path is inside `if (iso_selected_payment_option == Contract)`, so the peer must select Plug &
Charge — which runs over TLS. But **ISO 15118-2 TLS is unilateral**: the SECC presents a certificate,
the EV does not. So from the station's point of view the peer is *unauthenticated* at this point —
there has been no client certificate, and the crash happens **during parsing, before any signature or
chain check**. The sequence to reach it is the ordinary one:

1. Complete the `-2` TLS handshake (server-auth only) and the SAP handshake.
2. `PaymentServiceSelectionReq` selecting `Contract`.
3. `PaymentDetailsReq` with `ContractSignatureCertChain.Certificate` set to any non-empty bytes that
   are not a valid certificate.

No valid credential, no authorization, no prior state beyond the handshake. A single crafted message
takes the module down, repeatably — and since 2026-08-15 that is measured on your station rather than
reasoned from your source.

## What this is, and what it is not

- **It is a null-pointer dereference — a crash, a denial of service.** The pointer is null, not
  attacker-chosen, so this is not a controllable write and there is no path to code execution here.
  Saying so is part of the report: the severity is *availability*, not integrity.
- **There is no ISO requirement being violated.** The standard has no *shall not crash* clause; the
  code's own intent for this case is `FAILED_CertChainError`, which it sets at every neighbouring
  error exit. The finding stands on the crash and the reachability, not on a clause — the same footing
  as any robustness bug.
- **The charger does not recover, and that is measured.** We expected a supervision question here and
  there is none: the manager answers the module's exit with *Terminating all modules* and *Exiting
  manager*, and a connection attempt three seconds later is refused. The session drops, an in-progress
  charge on that connection dies, and nothing is left to restart anything. The
  [loop-shutdown filing](everest-loop-shutdown.md) recorded that a downed `-20` accept loop goes
  unnoticed because the process stays alive; this is the opposite failure and the louder one — the whole
  process tree goes, and any supervisor built on process liveness *will* see it.

## Suggested fix

Check the parse result before using the certificate. The minimal move is to lift the `err` test above
the use:

```cpp
     err = parse_contract_certificate(contract_crt, …);
 }   // (the bytesLen == 0 branch already goto's error_out)

 if (err != 0 || contract_crt == nullptr) {
     res->ResponseCode = iso2_responseCodeType_FAILED_CertChainError;
     goto error_out;
 }

 auto cert_emaid = getEmaidFromContractCert(contract_crt);   // now guaranteed non-null
```

That is the same `FAILED_CertChainError` the surrounding code already produces for every other bad-cert
case; it just has to be reached before the certificate is touched. Independently, `certificate_subject`
having only an `assert` for its null guard means the same shape can bite anywhere it is called on an
unchecked parse — worth a defensive `if (cert == nullptr) return {};` there so a future caller's
mistake is an empty map rather than a crash, though the real fix is the ordering above.

## Context: three ISO 15118-2 stacks meet the same malformed certificate

The bar that matters is *does it survive the same bytes* — and the answer separates EVerest from both
others. The **second** column, what each then does, is worth stating precisely, because none of the
three sends the `FAILED_CertChainError` the message table provides for, and one of the three is us:

| stack | survives? | and then |
|---|---|---|
| Josev (SwitchEV) | **yes** | the parse-level `ValueError` (from `load_der_x509_certificate`, reached via `log_certs_details`/`verify_certs`) is **not** in the state's own `except` — that list is cert-*verification* exceptions, which do produce `FAILED`. It falls through to the framework's rcv-loop catch-all `except (… ValueError, … Exception)` (`shared/comm_session.py:510-516`), which calls `self.stop(...)` (`:543`) and **terminates the session with no `PaymentDetailsRes`** |
| **EVerest `EvseV2G`** | **no — crash** | used before the parse result is checked; SIGABRT / SIGSEGV |
| *(ours)* | **yes** | `X509CertificateLoader.LoadCertificate` throws `CryptographicException`, caught by `try { … } catch (Exception ex)` in `Secc2.PaymentDetails` — but we then return `PaymentDetailsRes(OK)` with no contract key extracted, so the bad cert fails one message later at the **signed `AuthorizationReq`**, not here |

So the sharp claim is narrow and holds: **only `EvseV2G` crashes; the other two survive the identical
bytes.** The tidier "both answer `FAILED`" would be wrong — Josev terminates the session, we return
`OK` and fail a message later — and neither sends `FAILED_CertChainError` for the specifically
*unparseable* case, which is a smaller shared imperfection worth naming rather than papering over.
`EvseV2G`'s own surrounding code *wants* to answer `FAILED_CertChainError` (it sets it at every
neighbouring exit); one misplaced check turns that intent into a crash, and that is what makes this a
security report rather than a style note.

---

## Before sending

- [x] **Demonstrate the crash.** Isolated C reproduction of `certificate_subject`'s first two lines on
      a null `X509*`: SIGSEGV under `-DNDEBUG`, SIGABRT with the assert live. In the run directory.
- [x] **Locate it exactly, at the current head.** `iso_server.cpp:978-1006`,
      `crypto_openssl.cpp:161`, `openssl_util.cpp:653` and `:774`, everest-core 2026.02.1.
- [x] **Say what it is not.** A null dereference, not a controllable write; availability, not code
      execution. And no ISO clause — it stands on the crash.
- [x] **Show it is not universal.** Josev and our own station both **survive** the same bytes — Josev
      terminates the session via the framework catch-all, we return `OK` and fail a message later.
      Neither crashes, and neither sends `FAILED_CertChainError` for the unparseable case; the report
      says so rather than tidying it into "both answer FAILED".
- [x] **Put it on a running station — done 2026-08-15, twice.** SIGABRT at `openssl_util.cpp:775`, on
      their own SIL config with one line changed, from a peer that presented no client certificate. With
      a **control arm** whose only difference is that its certificate parses: answered `OK`, station
      alive. And a **liveness arm** afterwards, which is what turned "the module dies" into "the manager
      terminates every module and exits":
      [`…-paymentdetails-crash-live`](../interop-runs/2026-08-15-everest-paymentdetails-crash-live/notes.md).
- [ ] **Report it the way a crash should be reported.** It is remotely triggerable and pre-auth at the
      application layer; use their security policy / private disclosure before a public issue. This is
      the box that now decides when it goes out — everything technical is closed, and it is number **1**
      in [`sending-order.md`](sending-order.md) for that reason.
- [x] **Re-read the citations against the tree before posting — done 2026-08-11.** All six verified
      against the built 2026.02.1 source in the sweep over all 189 `file:line` references in this
      directory, and the ordering is **unchanged on everest-core `main`** (`ebcd36d`): the certificate
      still reaches `getEmaidFromContractCert` before the parse result is checked.
- [ ] **Post under your own name, in your own words.**
