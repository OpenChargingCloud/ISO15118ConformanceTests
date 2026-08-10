"""What order does cbexigen put the document element codes in, and by what key?

EXI 1.0 Second Edition §8.5.1 builds one SE production per global element declaration, over the qnames
sorted lexicographically by local-name and then by uri. This extracts the order cbexigen actually
emitted, for every document grammar it generates, and tests it against two candidate keys: the element
name (what §8.5.1 says) and the *type* name.

Usage: python3 cbdoc.py <libcbv2g checkout>
"""
import os
import re
import sys

RE_DOC = re.compile(r'^int decode_(\w+)_exiDocument\(')
RE_CALL = re.compile(r'decode_(\w+)\(stream,\s*&exiDoc->(\w+)\)')
RE_SIMPLE = re.compile(r'//\s*simple type!\s*decode_(\w+);')

# the family prefix the generator puts on every symbol; longest first, so iso20_ac_ wins over iso20_
PREFIXES = ['iso20_acdp_', 'iso20_wpt_', 'iso20_ac_', 'iso20_dc_', 'iso20_',
            'iso2_', 'din_', 'appHand_']


def unprefix(sym):
    for p in PREFIXES:
        if sym.startswith(p):
            return sym[len(p):]
    return sym


def document_order(path):
    src = open(path, encoding='utf-8', errors='replace').read().splitlines()
    out, inside = {}, None
    for line in src:
        m = RE_DOC.match(line)
        if m:
            inside, cur = m.group(1), []
            out[inside] = cur
            continue
        if inside is None:
            continue
        if line.startswith('int ') or line.startswith('static int '):
            inside = None
            continue
        mc = RE_CALL.search(line)
        if mc:
            cur.append((mc.group(2), unprefix(mc.group(1))))          # (element, type)
            continue
        ms = RE_SIMPLE.search(line)
        if ms:
            # a global element of simple type: cbexigen names no type, so its own name is the key
            cur.append((unprefix(ms.group(1)), None))
    return out


def report(name, items, full=False):
    names = [e for e, _t in items]
    by_name = sorted(names)
    # the candidate rule: sort by the *type*, falling back to the element name where there is none
    by_type = [e for e, _t in sorted(items, key=lambda x: (x[1] or x[0], x[0]))]
    moved = [(i, names[i], by_name.index(names[i]))
             for i in range(len(names)) if names[i] != by_name[i]]
    print(f"== {name}: {len(items)} global elements")
    print(f"   EXI §8.5.1 order (element qname):        {'MATCHES' if not moved else 'deviates'}")
    print(f"   order by type name, element name second: "
          f"{'MATCHES' if by_type == names else 'deviates'}")
    if moved:
        print(f"   {len(moved)} element(s) carry a code other than §8.5.1's:")
        for (got, el, want) in moved:
            t = dict(items)[el]
            print(f"       {el:<34} code {got:>3}   §8.5.1: {want:>3}   (type {t or '-'})")
    if full:
        print("   full order as generated:")
        for i, (e, t) in enumerate(items):
            print(f"       {i:>3}  {e:<34} {t or '(simple type)'}")
    print()


def main():
    lib = os.path.join(sys.argv[1], 'lib', 'cbv2g')
    for fam in sorted(os.listdir(lib)):
        d = os.path.join(lib, fam)
        if not os.path.isdir(d) or fam == 'common':
            continue
        for fn in sorted(os.listdir(d)):
            if fn.endswith('.c') and 'Decoder' in fn:
                for name, items in document_order(os.path.join(d, fn)).items():
                    if items:
                        report(f"{fn}  {name}", items, full=('--full' in sys.argv and 'ACDP' in fn))


if __name__ == '__main__':
    main()
