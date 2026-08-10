"""Read every grammar cbexigen generated, and check it against the schema model it printed itself.

Above each generated function cbexigen writes the content model it derived:

    // Element: definition=complex; name={...}Body; type={...}BodyType; content type=ELEMENT-ONLY;
    // Particle: AuthorizationReq, AuthorizationReqType (0, 1); AuthorizationRes, ... (0, 1); ...

and inside it writes the state machine, state by state:

    case 179:
        // Grammar: ID=179; read/write bits=2; LOOP (VendorSpecificDataContainer), START (WPT_LF_DataPackageList), END Element
            // Event: LOOP (VendorSpecificDataContainer, ...); next=180
            // Event: END Element; next=3
            done = 1;

Both halves parse. Every check below compares the generator against itself, so all of it is
reproducible from the libcbv2g checkout alone — no ISO schemas, no corpus, nothing to run.

Usage: python3 cbsweep.py <libcbv2g checkout> [--json out.json]
"""
import json
import math
import os
import re
import sys
from collections import OrderedDict

RE_FUNC = re.compile(r'^(?:static\s+)?int\s+((?:en|de)code_\w+)\s*\(')
RE_CASE = re.compile(r'^\s*case\s+(\d+):\s*$')
RE_GRAM = re.compile(r'//\s*Grammar:\s*ID=(\d+);\s*(?:read/write|read|write) bits=(\d+);\s*(.*)$')
RE_EVENT = re.compile(r'//\s*Event:\s*(.*?)(?:;\s*next=(\d+))?\s*$')
RE_GID = re.compile(r'\bgrammar_id\s*=\s*(\d+)\s*;')
RE_DONE = re.compile(r'\bdone\s*=\s*1\s*;')
RE_ELEM = re.compile(r'//\s*Element:\s*definition=(\w+);\s*name=\{([^}]*)\}(\S+);\s*type=\{([^}]*)\}(\S*);'
                     r'.*?content type=([\w-]+);')
RE_FLAGS = re.compile(r'abstract=(True|False);\s*final=(True|False);(?:\s*choice=(True|False);)?')
RE_PART = re.compile(r'//\s*Particle:\s*(.*)$')
RE_NBIT_W = re.compile(r'exi_basetypes_encoder_nbit_uint\(stream,\s*(\d+),\s*(\d+)\)')
RE_PROD = re.compile(r'^(START|LOOP|END)\b\s*(?:\(([^,)]+))?')


def split_semis(text):
    out, depth, cur = [], 0, ''
    for ch in text:
        if ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
        if ch == ';' and depth == 0:
            out.append(cur.strip())
            cur = ''
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


def split_commas(text):
    out, depth, cur = [], 0, ''
    for ch in text:
        if ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
        if ch == ',' and depth == 0:
            out.append(cur.strip())
            cur = ''
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


def parse_particles(text):
    """'Name, TypeName (min, max)' -> [(name, type, min, max)] in declaration order."""
    out = []
    for item in split_semis(text):
        m = re.match(r'^([\w:.-]+),\s*(.*?)\s*\((\d+),\s*(\d+)\)\s*$', item)
        if m:
            out.append((m.group(1), m.group(2), int(m.group(3)), int(m.group(4))))
    return out


def kind_and_name(prod):
    m = RE_PROD.match(prod)
    if not m:
        return ('OTHER', prod.strip())
    kind = m.group(1)
    name = (m.group(2) or '').strip()
    return ('END', None) if kind == 'END' else (kind, name or 'ANY')


class Func:
    def __init__(self, name, file, line):
        self.name, self.file, self.line = name, file, line
        self.states = OrderedDict()
        self.start = None
        self.elem = None          # (definition, ns, name, typens, typename)
        self.particles = []
        self.body_from = line


def parse(path):
    src = open(path, encoding='utf-8', errors='replace').read().splitlines()
    funcs, cur, cur_state, pre_case = [], None, None, True
    pending_elem, pending_part = None, None
    for i, line in enumerate(src, 1):
        me = RE_ELEM.search(line)
        if me:
            pending_elem = dict(definition=me.group(1), ns=me.group(2), name=me.group(3),
                                tns=me.group(4), tname=me.group(5), content=me.group(6),
                                abstract=False, choice=False)
        mf = RE_FLAGS.search(line)
        if mf and pending_elem is not None:
            pending_elem['abstract'] = mf.group(1) == 'True'
            pending_elem['choice'] = mf.group(3) == 'True'
        mp = RE_PART.search(line)
        if mp:
            pending_part = parse_particles(mp.group(1))
        m = RE_FUNC.match(line)
        if m and not line.rstrip().endswith(';'):
            cur = Func(m.group(1), os.path.basename(path), i)
            cur.elem, cur.particles = pending_elem, pending_part or []
            pending_elem, pending_part = None, None
            funcs.append(cur)
            cur_state, pre_case = None, True
            continue
        if cur is None:
            continue
        mc = RE_CASE.match(line)
        if mc:
            mg = RE_GRAM.search(src[i]) if i < len(src) else None
            if mg:
                pre_case = False
                cur_state = dict(id=int(mg.group(1)), bits=int(mg.group(2)),
                                 prods=split_commas(mg.group(3)), line=i,
                                 events=[], done=False, gids=[], codes=[])
                cur.states[int(mg.group(1))] = cur_state
            continue
        if pre_case:
            g = RE_GID.search(line)
            if g and cur.start is None:
                cur.start = int(g.group(1))
            continue
        if cur_state is None:
            continue
        mw = RE_NBIT_W.search(line)
        if mw and int(mw.group(1)) == cur_state['bits']:
            cur_state['codes'].append(('W', int(mw.group(2)), i))
        mcase = re.match(r'^\s*case\s+(\d+):\s*$', line)
        if mcase and 'Decoder' in os.path.basename(path):
            cur_state['codes'].append(('R', int(mcase.group(1)), i))
        mev = RE_EVENT.search(line)
        if mev:
            cur_state['events'].append((mev.group(1).strip(),
                                        int(mev.group(2)) if mev.group(2) else None, i))
        if RE_DONE.search(line):
            cur_state['done'] = True
        g = RE_GID.search(line)
        if g:
            cur_state['gids'].append((int(g.group(1)), i))
    return funcs


def gids(st):
    return [t for (t, _ln) in st['gids']]


def event_target(st, label_line, nxt):
    """Where a production goes. Most say `next=`; the choice-of-messages grammars do not, and the
    transition has to be read off the `grammar_id = N` the branch assigns."""
    if nxt is not None:
        return nxt
    later = [t for (t, ln) in st['gids'] if ln > label_line]
    return later[0] if later else None


# ---------------------------------------------------------------- the checks

def add(findings, check, f, state, line, detail):
    findings.append(dict(check=check, func=f.name, file=f.file, state=state, line=line, detail=detail))


def reachable(func):
    start = func.start if func.start in func.states else (min(func.states) if func.states else None)
    if start is None:
        return None, set()
    seen, stack = set(), [start]
    while stack:
        s = stack.pop()
        if s in seen or s not in func.states:
            continue
        seen.add(s)
        stack.extend(gids(func.states[s]))
    return start, seen


def check_dead_end(func, findings):
    """A reachable state from which no accepting state can be reached: the codec can never finish."""
    start, live = reachable(func)
    if start is None:
        return
    ok, changed = set(s for s in live if func.states[s]['done']), True
    while changed:
        changed = False
        for s in live - ok:
            if any(t in ok for t in gids(func.states[s]) if t in func.states):
                ok.add(s)
                changed = True
    for s in sorted(live - ok):
        add(findings, 'dead-end', func, s, func.states[s]['line'], '; '.join(func.states[s]['prods']))


def elem_starts(st):
    """Every element name this state can begin — from the grammar comment and from its events.

    The two have to be read together: where a type is a plain choice of namespace elements (the
    `Body` of -2 and DIN) the grammar comment names only the first alternative and the rest appear
    solely as `// Event: <Name>` lines.
    """
    out = set(n for k, n in map(kind_and_name, st['prods']) if k in ('START', 'LOOP') and n)
    for (label, _nxt, _ln) in st['events']:
        k, n = kind_and_name(label)
        if k in ('START', 'LOOP') and n:
            out.add(n)
        elif k == 'OTHER' and re.fullmatch(r'[A-Za-z_]\w*', label):
            out.add(label)
    return out


def check_unreachable_particle(func, findings):
    """A particle no *reachable* state offers.

    The state that would carry it is usually generated — it simply cannot be got to from the start,
    so the struct has the field and no document can ever carry it. Say "unreachable" and not
    "missing": the difference matters when someone goes looking for the state.
    """
    if not func.particles or not func.states:
        return
    _, live = reachable(func)
    offered = set()
    for s in live:
        offered |= elem_starts(func.states[s])
    for (name, typ, lo, hi) in func.particles:
        if name not in offered and name not in ('ANY',):
            add(findings, 'unreachable-particle', func, -1, func.line,
                f"particle '{name}' ({typ}, {lo}..{hi}) is in the content model and no state "
                f"reachable from the start offers it")



def truncated(st):
    """True where the grammar comment lists fewer productions than the state actually has.

    The choice-of-messages grammars (`BodyType` in -2 and DIN) name only their first alternative in
    the comment and carry the other thirty-odd as events. Any check that counts productions has to
    leave those alone or it reports the comment rather than the code.
    """
    return len(st['events']) > len(st['prods'])


def has_wildcard(st):
    return any(n == 'ANY' for _k, n in map(kind_and_name, st['prods']))


def check_codes(func, findings):
    """The event code each production writes must be its index in the production list.

    Skipped where the state has a wildcard: an `ANY` production belongs to the second level of the
    code space, so its value is legitimately out of sequence with the first-level ones.
    """
    for s, st in func.states.items():
        vals = [v for (_kind, v, _ln) in st['codes']]
        if not vals or len(st['prods']) <= 1 or truncated(st) or has_wildcard(st):
            continue
        if vals != list(range(len(vals))):
            add(findings, 'event-codes', func, s, st['line'],
                f"codes {vals} for productions {st['prods']}")


def check_width(func, findings):
    """cbexigen's own width rule is ceil(log2(n+1)) over the listed productions."""
    for s, st in func.states.items():
        n = len(st['prods'])
        if n == 0 or truncated(st) or any(kind_and_name(p)[0] == 'OTHER' for p in st['prods']):
            continue
        want = max(1, math.ceil(math.log2(n + 1)))
        if want != st['bits']:
            add(findings, 'code-width', func, s, st['line'],
                f"{n} production(s), bits={st['bits']}, expected {want}: " + '; '.join(st['prods']))



def check_symmetry(enc, dec, findings):
    def by_type(funcs):
        return {f.name.split('_', 1)[1]: f for f in funcs if f.states}
    e, d = by_type(enc), by_type(dec)
    for t in sorted(set(e) & set(d)):
        ef, df = e[t], d[t]
        for s in sorted(set(ef.states) | set(df.states)):
            es, ds = ef.states.get(s), df.states.get(s)
            if es is None or ds is None:
                add(findings, 'asymmetry', ef, s, 0,
                    f"{t}: state only in the {'decoder' if es is None else 'encoder'}")
                continue
            # The per-event comments cannot be compared: the encoder names the type it writes
            # (`START (string)`) where the decoder names the element (`START (EVProcessing, …)`).
            # The grammar comment is the common ground — except where it is truncated, which is the
            # choice-of-messages grammars and nothing else.
            if truncated(es) or truncated(ds):
                continue
            if es['prods'] != ds['prods'] or es['bits'] != ds['bits']:
                add(findings, 'asymmetry', ef, s, es['line'],
                    f"{t}: encoder [{es['bits']}b] {'; '.join(es['prods'])} != "
                    f"decoder [{ds['bits']}b] {'; '.join(ds['prods'])}")


def main():
    root = sys.argv[1]
    lib = os.path.join(root, 'lib', 'cbv2g')
    findings, stats, parsed = [], dict(files=0, funcs=0, states=0, particles=0), {}
    for fam in sorted(os.listdir(lib)):
        d = os.path.join(lib, fam)
        if not os.path.isdir(d) or fam == 'common':
            continue
        for fn in sorted(os.listdir(d)):
            if not fn.endswith('.c') or 'Datatypes' in fn:
                continue
            path = os.path.join(d, fn)
            src = open(path, encoding='utf-8', errors='replace').read().splitlines()
            funcs = parse(path)
            parsed[fn] = funcs
            stats['files'] += 1
            stats['funcs'] += sum(1 for f in funcs if f.states)
            stats['states'] += sum(len(f.states) for f in funcs)
            stats['particles'] += sum(len(f.particles) for f in funcs)
            for f in funcs:
                if not f.states:
                    continue
                check_dead_end(f, findings)
                check_unreachable_particle(f, findings)
                check_codes(f, findings)
                check_width(f, findings)
    for fn, funcs in parsed.items():
        peer = fn.replace('Encoder', 'Decoder')
        if 'Encoder' in fn and peer in parsed:
            check_symmetry(funcs, parsed[peer], findings)

    print(f"parsed {stats['files']} generated files, {stats['funcs']} functions with a grammar, "
          f"{stats['states']} grammar states, {stats['particles']} declared particles\n")
    for c in ['dead-end', 'unreachable-particle', 'event-codes', 'code-width', 'asymmetry']:
        rows = [f for f in findings if f['check'] == c]
        print(f"### {c}: {len(rows)}")
        for r in rows:
            print(f"  {r['file']}:{r['line']}  {r['func']}  ID={r['state']}\n      {r['detail']}")
        print()
    if '--json' in sys.argv:
        json.dump(findings, open(sys.argv[sys.argv.index('--json') + 1], 'w'), indent=1)


if __name__ == '__main__':
    main()
