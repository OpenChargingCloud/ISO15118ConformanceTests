"""Hold every grammar cbexigen generated against the content model in the schema it was generated from.

The `// Particle:` comment cbexigen prints is a flattened list — it records which elements a type has
and how often, but not whether they sit in a sequence or a choice. So a check built on that comment
alone reports every `xs:choice` as a defect. This reads the schema itself instead.

For each complex type it enumerates the element sequences the content model permits (bounded: each
optional taken and skipped, each repeat at its minimum and one above), and runs each of them through
the state machine in the generated C. A sequence the schema allows and the state machine cannot walk
is a document that cannot be encoded or decoded.

The schemas are ISO's and are not redistributed; this reads the local copies that
`tools/download-schemas.sh` puts in place, and prints element names and occurrence bounds only —
which the generated C already states in the open.

Usage: python3 cbschema.py <libcbv2g checkout> <WWCP_ISO15118 checkout> [--json out.json]
"""
import itertools
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cbsweep import parse, kind_and_name, event_target          # noqa: E402

XS = '{http://www.w3.org/2001/XMLSchema}'
MAX_SEQS = 4000
REPEAT_CAP = 2          # counts tried per repeated particle: min, min+1 (capped by max)

# the generated families, and the project whose Schemas/ directory holds their input
FAMILIES = {
    'appHand': 'WWCP_ISO15118_EXI',
    'iso2': 'WWCP_ISO15118_2',
    'iso20': 'WWCP_ISO15118_20.CommonMessages',
    'iso20_ac': 'WWCP_ISO15118_20.AC',
    'iso20_acdp': 'WWCP_ISO15118_20.ACDP',
    'iso20_dc': 'WWCP_ISO15118_20.DC',
    'iso20_wpt': 'WWCP_ISO15118_20.WPT',
}


# ------------------------------------------------------------------ the schema side

class Schema:
    def __init__(self, paths):
        self.types = {}       # (ns, name) -> element defining the complexType
        self.groups = {}
        self.elements = {}    # (ns, name) -> (element name, type qname, abstract)
        self.by_name = {}     # name -> (ns, name), last one wins; a fallback only
        self.subs = {}        # (ns, head) -> [member element names]
        pending = []
        for p in paths:
            root = ET.parse(p).getroot()
            ns = root.get('targetNamespace', '')
            prefixes = dict(re.findall(r'xmlns:([\w.-]+)="([^"]+)"', open(p, encoding='utf-8').read()))
            for node in root:
                nm = node.get('name')
                if nm is None:
                    continue
                if node.tag == XS + 'complexType':
                    self.types[(ns, nm)] = (node, prefixes, ns)
                    self.by_name[nm] = (ns, nm)
                elif node.tag == XS + 'group':
                    self.groups[(ns, nm)] = (node, prefixes, ns)
                elif node.tag == XS + 'element':
                    self.elements[(ns, nm)] = (nm, node.get('type'),
                                               node.get('abstract') == 'true')
                    if node.get('substitutionGroup'):
                        pending.append((node.get('substitutionGroup'), prefixes, ns, nm))
        for (head, prefixes, ns, member) in pending:
            self.subs.setdefault(self.resolve(head, prefixes, ns), []).append(member)

    def attributes(self, key, depth=0):
        """(name, required) for every attribute of a complexType, base types included."""
        if key not in self.types or depth > 12:
            return []
        node, prefixes, ns = self.types[key]
        out, scopes = [], [node]
        for child in node:
            if child.tag in (XS + 'complexContent', XS + 'simpleContent'):
                for cc in child:
                    if cc.tag in (XS + 'extension', XS + 'restriction'):
                        base = self.resolve(cc.get('base'), prefixes, ns)
                        out += self.attributes(base, depth + 1) if base else []
                        scopes.append(cc)
        for scope in scopes:
            for a in scope:
                if a.tag == XS + 'attribute' and a.get('name'):
                    out.append((a.get('name'), a.get('use') == 'required'))
        return out

    def substitutes(self, ns, name):
        """The element names that may actually appear where this one is referenced.

        The head drops out when it is abstract, and also when its *type* is — ISO's control-mode and
        charge-parameter heads are declared concrete over an abstract complexType, so they can only
        appear carrying an xsi:type, which is not a plain SE production.
        """
        members = self.subs.get((ns, name), [])
        decl = self.elements.get((ns, name))
        usable = decl is not None and not decl[2]
        if usable and decl[1]:
            tkey = self.resolve(decl[1], {}, ns)
            if tkey and tkey[1] and (ns, tkey[1]) in self.types:
                node = self.types[(ns, tkey[1])][0]
                usable = node.get('abstract') != 'true'
        if not members:
            return [name] if usable or decl is None else []
        return ([name] if usable else []) + members

    def resolve(self, qname, prefixes, ns):
        if qname is None:
            return None
        if ':' in qname:
            pfx, local = qname.split(':', 1)
            return (prefixes.get(pfx, ''), local)
        return (ns, qname)

    def model(self, key, depth=0):
        """The content model of a complexType, as ('seq'|'choice'|'all', [children], min, max)."""
        if key not in self.types or depth > 12:
            return None
        node, prefixes, ns = self.types[key]
        return self._content(node, prefixes, ns, depth)

    def _content(self, node, prefixes, ns, depth):
        for child in node:
            if child.tag == XS + 'complexContent':
                for cc in child:
                    if cc.tag == XS + 'extension':
                        base = self.resolve(cc.get('base'), prefixes, ns)
                        bm = self.model(base, depth + 1) if base else None
                        own = self._particles(cc, prefixes, ns, depth)
                        parts = [p for p in (bm, own) if p]
                        return ('seq', parts, 1, 1) if parts else None
                    if cc.tag == XS + 'restriction':
                        return self._particles(cc, prefixes, ns, depth)
            if child.tag == XS + 'simpleContent':
                return None
        return self._particles(node, prefixes, ns, depth)

    def _particles(self, node, prefixes, ns, depth):
        for child in node:
            if child.tag in (XS + 'sequence', XS + 'choice', XS + 'all'):
                return self._group(child, prefixes, ns, depth)
            if child.tag == XS + 'group':
                ref = self.resolve(child.get('ref'), prefixes, ns)
                if ref in self.groups:
                    gnode, gp, gns = self.groups[ref]
                    return self._particles(gnode, gp, gns, depth + 1)
        return None

    def _group(self, node, prefixes, ns, depth):
        kind = {XS + 'sequence': 'seq', XS + 'choice': 'choice', XS + 'all': 'all'}[node.tag]
        items = []
        for child in node:
            if child.tag == XS + 'element':
                lo, hi = occurs(child)
                name = child.get('name')
                if name is None:
                    ref = self.resolve(child.get('ref'), prefixes, ns)
                    names = self.substitutes(*ref) if ref else ['?']
                    if not names:
                        continue                     # an abstract head with no members: unusable
                    if len(names) == 1:
                        items.append(('e', names[0], lo, hi))
                    else:
                        # a substitution group: any member may stand where the head is referenced
                        items.append(('choice', [('e', n, 1, 1) for n in names], lo, hi))
                    continue
                items.append(('e', name, lo, hi))
            elif child.tag in (XS + 'sequence', XS + 'choice', XS + 'all'):
                sub = self._group(child, prefixes, ns, depth)
                lo, hi = occurs(child)
                items.append((sub[0], sub[1], lo, hi))
            elif child.tag == XS + 'any':
                lo, hi = occurs(child)
                items.append(('any', None, lo, hi))
            elif child.tag == XS + 'group':
                ref = self.resolve(child.get('ref'), prefixes, ns)
                if ref in self.groups:
                    gnode, gp, gns = self.groups[ref]
                    sub = self._particles(gnode, gp, gns, depth + 1)
                    if sub:
                        lo, hi = occurs(child)
                        items.append((sub[0], sub[1], lo, hi))
        lo, hi = occurs(node)
        return (kind, items, lo, hi)


def occurs(node):
    lo = int(node.get('minOccurs', '1'))
    hi = node.get('maxOccurs', '1')
    return lo, (1 << 20) if hi == 'unbounded' else int(hi)


def counts(lo, hi):
    out = [lo]
    if hi > lo:
        out.append(lo + 1)
    return out[:REPEAT_CAP]


def expand(model, budget=[MAX_SEQS]):
    """Every element-name sequence the model permits, bounded. Immediate children only."""
    if model is None or budget[0] <= 0:
        return [()]
    kind, items, lo, hi = model
    if kind == 'e':
        base = [(items,)] if items else [()]
        return [tuple(x for _ in range(n) for x in base[0]) for n in counts(lo, hi)]
    if kind == 'any':
        return [()]
    if kind == 'choice':
        once = []
        for it in items:
            once.extend(expand(it, budget))
        once = once or [()]
    elif kind in ('seq', 'all'):
        once = [()]
        for it in items:
            sub = expand(it, budget)
            nxt = []
            for a in once:
                for b in sub:
                    nxt.append(a + b)
                    if len(nxt) > MAX_SEQS:
                        break
                if len(nxt) > MAX_SEQS:
                    break
            once = nxt
    else:
        return [()]
    out = []
    for n in counts(lo, hi):
        for combo in itertools.product(once, repeat=min(n, 3)):
            out.append(tuple(x for part in combo for x in part))
            if len(out) > MAX_SEQS:
                budget[0] = 0
                return out
    return out


# ------------------------------------------------------------------ the generated side

def automaton(func):
    """state -> [(element name or None for END, next state)] from the generated state machine."""
    trans = {}
    for s, st in func.states.items():
        edges = []
        for (label, nxt, ln) in st['events']:
            k, name = kind_and_name(label)
            if k == 'END':
                edges.append((None, None))
            elif k in ('START', 'LOOP') and name:
                edges.append((name, event_target(st, ln, nxt)))
            elif k == 'OTHER' and re.fullmatch(r'[A-Za-z_]\w*', label):
                edges.append((label, event_target(st, ln, nxt)))
        trans[s] = edges
    return trans


def accepts(func, trans, seq):
    start = func.start if func.start in func.states else min(func.states)
    cur = {start}
    for name in seq:
        nxt = set()
        for s in cur:
            for (n, t) in trans.get(s, []):
                if n == name and t is not None and t in func.states:
                    nxt.add(t)
        if not nxt:
            return False
        cur = nxt
    return any(n is None for s in cur for (n, _t) in trans.get(s, []))


def main():
    cb, stack = sys.argv[1], sys.argv[2]
    schemas = {}
    for fam, proj in FAMILIES.items():
        d = os.path.join(stack, proj, 'Schemas')
        if os.path.isdir(d):
            schemas[fam] = Schema([os.path.join(d, f) for f in sorted(os.listdir(d))
                                   if f.endswith('.xsd')])

    findings, checked, skipped = [], 0, {}
    lib = os.path.join(cb, 'lib', 'cbv2g')
    for fam_dir in sorted(os.listdir(lib)):
        d = os.path.join(lib, fam_dir)
        if not os.path.isdir(d) or fam_dir == 'common':
            continue
        for fn in sorted(os.listdir(d)):
            if not fn.endswith('.c') or 'Decoder' not in fn:
                continue
            for func in parse(os.path.join(d, fn)):
                if not func.states or func.elem is None:
                    continue
                tns, tname = func.elem['tns'], func.elem['tname']
                if not tname or 'xmldsig' in tns or func.elem['content'] != 'ELEMENT-ONLY':
                    continue
                if func.elem['abstract']:
                    skipped['abstract'] = skipped.get('abstract', 0) + 1
                    continue
                fam = next((f for f in sorted(FAMILIES, key=len, reverse=True)
                            if func.name.startswith('decode_' + f + '_')), None)
                if fam is None or fam not in schemas:
                    skipped[fam_dir] = skipped.get(fam_dir, 0) + 1
                    continue
                sch = schemas[fam]
                key = (tns, tname) if (tns, tname) in sch.types else sch.by_name.get(tname)
                if key is None:
                    skipped['type-not-found'] = skipped.get('type-not-found', 0) + 1
                    continue
                model = sch.model(key)
                if model is None:
                    continue
                seqs = expand(model, [MAX_SEQS])
                if not seqs:
                    continue
                # cbexigen walks the attributes first, as productions of their own
                attrs = sch.attributes(key)
                if attrs:
                    prefixes = [()]
                    for (an, required) in attrs:
                        prefixes = [p + (an,) for p in prefixes] + \
                                   ([] if required else list(prefixes))
                    seqs = [p + s for p in prefixes for s in seqs]
                trans = automaton(func)
                bad = [s for s in dict.fromkeys(seqs) if not accepts(func, trans, s)]
                checked += 1
                if bad:
                    bad.sort(key=len)
                    findings.append(dict(check='schema-reject', func=func.name, file=fn,
                                         type=tname, line=func.line,
                                         total=len(set(seqs)), rejected=len(bad),
                                         examples=[list(b) for b in bad[:3]]))
    print(f"checked {checked} types against their content model\n")
    print(f"### schema-reject: {len(findings)}")
    for r in sorted(findings, key=lambda r: (r['file'], r['type'])):
        print(f"  {r['file']}:{r['line']}  {r['type']}  "
              f"{r['rejected']} of {r['total']} permitted child sequences rejected")
        for ex in r['examples']:
            print(f"      cannot walk: {ex}")
    if skipped:
        print(f"\nskipped: {skipped}")
    if '--json' in sys.argv:
        json.dump(findings, open(sys.argv[sys.argv.index('--json') + 1], 'w'), indent=1)


main()
