# 2026-08-12 — the EV asks for root A, the station serves root B

`client-auth` §3 was written from source the same morning and said so on its first line: *the station
never reads the EV's `certificate_authorities`, so it cannot satisfy `[V2G20-1007]`/`[V2G20-2379]` —
source only, latent in every configuration this project has run.* **Measured now, and it is not
latent.**

everest-core `main` **`ebcd36d`**, `Evse15118D20`, `config-sil-dc-d20.yaml` as shipped, in the
worktree build with its own prefix. Two V2G roots installed, one valid SECC chain under each.

## The three arms

Same station, same process, one variable: what the client puts in `certificate_authorities`
([`arms.log`](arms.log)).

| arm | the EV asks for | the station serves |
|---|---|---|
| **A** | root **A** — and the station holds a valid chain under it | `SECCCert-B` ← `CPOSubCA-B` ← **`V2GRootCA-B`** |
| **B** | root **B** | the same chain B |
| **C** (control) | *no extension at all* | the same chain B |

Byte-identical chains in all three. **The served chain does not depend on the request** — and arm A is
the violation on its own: the EV named a root, the station holds a chain under exactly that root, and
sent the other one.

`Verify return code: 20` in every arm is the client refusing what it got, which is the EV's side of
`[V2G20-1007]` arriving as designed.

## Why the right answer was available

Not an impossibility argument. The chain under root A is installed and valid:

```
$ openssl verify -CAfile ca/v2g/V2G_ROOT_CA.pem \
      -untrusted client/cso/CPO_CERT_CHAIN.pem client/cso/SECC_LEAF.pem
client/cso/SECC_LEAF.pem: OK
```

and their own module log says which one it handed to the TLS layer:

```
evse_security:E :: Requesting leaf certificate info: V2G
evse_security:E :: Found valid leaf: [".../client/cso/CPO_CERT_CHAIN_B.pem"]
```

**One leaf, chosen before any `ClientHello` exists.** `get_leaf_certificate_info` runs when the TLS
server is built, in response to the SDP request; the EV's list arrives one flight later and has nowhere
to go. So the two causes compound: the module asks for a single leaf and cannot ask for a matching one,
and `lib/everest/tls` fixes `cfg.chains[0]` and never reads the extension.

## The rig error, and it is worth more than the result

The first attempt at this run crashed the whole manager — every module down, `evse_security` exiting
with **signal 11**, a `nlohmann::json type_error` and `std::future_error: Promise already satisfied`
seconds after *"Starting 18 modules"*.

For a few minutes that looked like a finding: *a second V2G root crashes their security module.* It was
not. **The previous station was still running.** The kill that was supposed to stop it,
`pkill -f "dist-main/bin/manager"`, matched nothing — the process's actual command line is
`./bin/manager --prefix /home/ahzf/everest/dist-main`, so the string `dist-main/bin/manager` never
appears in it. Two managers then shared one MQTT prefix and fought over every promise.

Worse, the *check* that reported it stopped used the same wrong pattern, so it agreed. **A run note in
this directory said the station was stopped when it was not.** Corrected here.

Two rules out of it, both now in [`tools/interop-everest/README.md`](../../../tools/interop-everest/README.md):

- **Kill on `--prefix <path>`, not on a path you assume is in argv.** EVerest's manager execs its
  modules with the prefix as a *flag*.
- **Verify with a self-match-proof pattern.** `pgrep -f "prefix /home/ahzf/…"` matches the shell running
  the `pgrep`, so it answers *"still running"* forever. `pgrep -cf "[d]ist-main"` does not.

And the general one, which is why this section is longer than the result: **an unexplained crash in a
rig you just changed is your rig until proven otherwise.** The PKI was fine — the same two roots came
up cleanly on the next start and produced the measurement above.

## What moves

[`everest-d20-client-auth`](../../reports/everest-d20-client-auth.md) §3 goes from *source only* to
*measured, 3 arms with a control*. Nothing in the interop matrix: this is a defect in their station, not
a capability of ours.

## Reproduce

```bash
bash tools/interop-everest/mint-second-root.sh          # root B + a SECC chain under it
#   install into <prefix>/etc/everest/certs, restart the station, then:
bash tools/interop-everest/chain-selection-arm.sh "ask for A" /path/to/V2G_ROOT_CA.pem
bash tools/interop-everest/chain-selection-arm.sh "ask for B" /path/to/V2G_ROOT_CA_B.pem
bash tools/interop-everest/chain-selection-arm.sh "control"   none
```
