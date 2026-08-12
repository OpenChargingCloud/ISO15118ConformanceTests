# `everest-d20-client-auth`, split into the three issues it should be filed as

The [full report](../everest-d20-client-auth.md) is the account: how each finding was reached, what was
measured on which build, what was ruled out, and what we got wrong on the way. **It is not what you
post.** These three are.

| | issue | fix | evidence |
|---|---|---|---|
| 1 | [Client auth is decided by the EV](issue-1-client-auth-decided-by-the-ev.md) | **one line** at the call site | 2 handshake arms with a control, on `main` and on `2026.02.1` |
| 2 | [What the `CertificateRequest` carries](issue-2-certificaterequest-contents.md) | three OpenSSL calls | the message parsed byte for byte, on `main` |
| 3 | [Which chain the server presents](issue-3-server-chain-selection.md) | read the extension; then offer more than one chain | 3 arms with a no-extension control, on `main` |

## Why three and not one

Three directions through the same handshake, three different fixes, and — this is the part that decides
it — **three different answers a maintainer can reasonably give**. Filed together, one reply covers all
three and the weakest answer decides the lot.

- **1** has an answer available: *"the TLS 1.2 path is there so the same library can serve ISO 15118-2."*
  That is true, and it closes issue 1's framing without touching 2 or 3.
- **2** has none — the extensions are missing whatever the verify mode ends up being.
- **3** has none either, and its fix is architecturally the largest of the three: it needs a chain
  selector *and* a way for the module to hold more than one chain.

Each file says in its own words that the other two exist and that fixing it leaves them standing.

## Suggested order

**1, then 3, then 2.** Issue 1 is the smallest fix and the easiest to agree with, and a maintainer who
has just merged a one-line patch is a maintainer who will read the next one. Issue 3 has the strongest
evidence. Issue 2 is the one a test house would raise anyway and the least urgent operationally.

That is a suggestion about how to be heard, not about severity. By severity it is 3, 1, 2.

## What is deliberately not in them

- **The `2026.02.1` measurements**, except where an issue says so explicitly. Everything else is `main`,
  because that is what a maintainer will check.
- **Our checklists and our reasoning about what we got wrong.** Those live in the full report and in the
  [run notes](../../interop-runs/); an issue is not the place to show your work.
- **§1's *what it costs*** — the two-frame replay showing an anonymous peer reaching
  `AuthorizationSetup` — is `2026.02.1` only and issue 1 flags it as such. Decide whether to include it;
  it strengthens the argument and weakens the evidence.

## Before any of them goes out

Each file ends with its own short list. Two apply to all three:

- **Re-read the `main` line numbers on the day you post.** `main` moved three times during the week
  these were written.
- **Post under your own name, in your own words.** These are drafts for a person to send, not messages
  from a test suite.
