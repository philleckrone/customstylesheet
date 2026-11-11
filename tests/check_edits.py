"""Paratext's own parser on the files Harness.exe edits wrote: the edit must add no stylesheet
errors, the new marker zzEditorTest must exist, and the first marker must now be underlined."""
import glob, os, re, subprocess
HERE = os.path.dirname(os.path.abspath(__file__))
ORACLE = os.path.join(HERE, 'oracle', 'Oracle.exe')
ROOT = r'C:\My Paratext 8 Projects'
BASE = os.path.join(ROOT, 'usfm.sty')

def run(custom, *markers):
    out = subprocess.run([ORACLE, BASE, custom] + list(markers), stdout=subprocess.PIPE).stdout.decode('utf-8', 'replace')
    errs = re.search(r'^AltErrors=(.*)$', out, re.M).group(1)
    errs = re.sub(r'Line \d+ - ', '', errs)   # line numbers shift after an insert
    return out, sorted(set(re.findall(r'\[([^\]]*)\]', errs)))

bad = 0
files = sorted(glob.glob(os.path.join(HERE, 'out', 'edits', '*.custom.sty')))
for f in files:
    tag = os.path.basename(f)[:-len('.custom.sty')]
    orig = os.path.join(ROOT, tag, 'custom.sty')
    first = open(f[:-len('.custom.sty')] + '.marker.txt', encoding='utf-8').read().strip()
    _, before = run(orig)
    out, after = run(f, first, 'zzEditorTest')
    new_errors = [e for e in after if e not in before]
    underline = re.search(r'^<' + re.escape(first) + r'>.* Underline=True', out, re.M) is not None
    zz = re.search(r'^<zzEditorTest> Name=zzEditorTest - test StyleType=scCharacterStyle', out, re.M) is not None
    if new_errors or not zz or not underline:
        bad += 1
        print('FAIL', tag, 'first=', first, 'underline=', underline, 'zz=', zz, 'new errors=', new_errors)
print('%d files checked, %d failed' % (len(files), bad))
