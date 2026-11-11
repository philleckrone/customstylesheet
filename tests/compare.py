"""Compare Harness.exe dump (CustomStyEditor merge logic) with Oracle.exe --dump (Paratext's
ScrStylesheet) for every project custom.sty, or for the files given on the command line."""
import glob, os, re, subprocess, sys, collections
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = r'C:\My Paratext 8 Projects'
ORACLE = os.path.join(HERE, 'oracle', 'Oracle.exe')
HARNESS = os.path.join(HERE, 'harness', 'Harness.exe')
OUT = os.path.join(HERE, 'out')
if not os.path.isdir(OUT): os.makedirs(OUT)

def base_for(proj_dir):
    name = 'usfm.sty'
    s = os.path.join(proj_dir, 'Settings.xml')
    if os.path.exists(s):
        m = re.search(r'<StyleSheet>(.*?)</StyleSheet>', open(s, encoding='utf-8', errors='replace').read())
        if m: name = m.group(1).strip()
    for cand in (os.path.join(proj_dir, name), os.path.join(ROOT, name), os.path.join(ROOT, 'usfm.sty')):
        if os.path.exists(cand): return cand

def parse(text):
    d = {}
    for line in text.splitlines():
        if not line.strip() or '\t' not in line: continue
        parts = line.split('\t')
        d[parts[0]] = dict(p.split('=', 1) for p in parts[1:])
    return d

def norm_tp(t):
    # Paratext adds level_N from the marker number and 'publishable' when neither value is given.
    w = set(x for x in (t or '').split() if not x.startswith('level_'))
    if 'publishable' not in w and 'nonpublishable' not in w: w.add('publishable')
    return ' '.join(sorted(w))

def compare(custom, base):
    tag = os.path.basename(os.path.dirname(custom))
    ofile = os.path.join(OUT, tag + '.oracle.tsv')
    subprocess.run([ORACLE, base, custom, '--dump', ofile], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if not os.path.exists(ofile): return tag, None
    oracle = parse(open(ofile, encoding='utf-8').read())
    mine = parse(subprocess.run([HARNESS, 'dump', base, custom], stdout=subprocess.PIPE).stdout.decode('utf-8'))
    diffs = []
    for m in sorted(set(oracle) | set(mine)):
        if m not in oracle: diffs.append((m, 'only-mine', '')); continue
        if m not in mine:
            if not m.endswith('*') and not (m.endswith('-e') and m[:-2] + '-s' in mine):
                diffs.append((m, 'only-paratext', ''))
            continue
        for k, v in oracle[m].items():
            mv = mine[m].get(k)
            if mv == '?' : continue
            if k == 'TextType' and (mv, v) in (('chapternumber', 'other'), ('versenumber', 'versetext')): continue
            if k == 'TP': v, mv = norm_tp(v), norm_tp(mv)
            if k == 'Endmarker' and mv == '' and v == m + '*': continue
            if mv != v: diffs.append((m, k, 'pt=%r mine=%r' % (v, mv)))
    return tag, diffs

files = sys.argv[1:] or sorted(set(p for p in glob.glob(os.path.join(ROOT, '*', 'custom.sty'))))
total = collections.Counter()
for f in files:
    tag, diffs = compare(f, base_for(os.path.dirname(f)))
    if diffs is None: print('ORACLE FAILED', f); continue
    for d in diffs: total[d[1]] += 1
    if diffs:
        print('==', tag, len(diffs))
        for d in (diffs if os.environ.get("VERBOSE") else diffs[:8]): print('   ', d)
print('fields differing:', dict(total))
