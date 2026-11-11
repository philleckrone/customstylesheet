r"""Checks that every Resources\Strings.*.xaml has the same keys as Strings.en.xaml and the
same {n} placeholders in each string, and that every key the C# code asks for exists.

    python Custom_Stylesheet_Editor\tools\check_strings.py
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'CustomStyEditor')
RES = os.path.join(ROOT, 'Resources')
PAT = re.compile(r'<sys:String x:Key="([^"]+)">(.*?)</sys:String>', re.S)


def load(lang):
    text = io.open(os.path.join(RES, 'Strings.%s.xaml' % lang), encoding='utf-8-sig').read()
    return dict(PAT.findall(text))


def placeholders(s):
    return sorted(set(re.findall(r'\{\d+\}', s)))


en = load('en')
bad = 0
for lang in ('es', 'fr', 'pt', 'zh-Hans'):
    other = load(lang)
    for k in sorted(set(en) - set(other)):
        print('%s: missing %s' % (lang, k)); bad += 1
    for k in sorted(set(other) - set(en)):
        print('%s: extra %s' % (lang, k)); bad += 1
    for k in sorted(set(en) & set(other)):
        if placeholders(en[k]) != placeholders(other[k]):
            print('%s: placeholders differ in %s' % (lang, k)); bad += 1

# Keys used by code: Str("..."), StrStatic("..."), SetResourceReference(..., "...") and {DynamicResource ...}.
used = set()
for dirpath, dirs, files in os.walk(ROOT):
    if os.sep + 'obj' in dirpath or os.sep + 'bin' in dirpath:
        continue
    for fn in files:
        if not (fn.endswith('.cs') or fn.endswith('.xaml')) or fn.startswith('Strings.'):
            continue
        s = io.open(os.path.join(dirpath, fn), encoding='utf-8-sig').read()
        used.update(re.findall(r'Str(?:Static)?\("([A-Za-z_]+)"\)', s))
        used.update(re.findall(r'SetResourceReference\([^,]+,\s*"([A-Za-z_]+)"\)', s))
        used.update(re.findall(r'\{DynamicResource ([A-Za-z_]+)\}', s))
        used.update(re.findall(r'Label\("([A-Za-z_]+)"\)', s))
        used.update(re.findall(r'base\(owner, "([A-Za-z_]+)"', s))
# Keys built at run time.
used.update('KwTip_' + k for k in re.findall(r'Def\("([A-Za-z]+)"', io.open(os.path.join(ROOT, 'Services', 'StyleKeywords.cs')).read()))
used.update('Group_' + g for g in ('Identity', 'TextFormat', 'Font', 'Paragraph', 'Placement', 'Other'))
used.update(re.findall(r'"(Unit_[A-Za-z]+)"', io.open(os.path.join(ROOT, 'Services', 'StyleKeywords.cs')).read()))
for k in sorted(used - set(en)):
    print('used but not defined: %s' % k); bad += 1
for k in sorted(set(en) - used):
    print('defined but unused: %s' % k)
print('OK' if bad == 0 else '%d problem(s)' % bad)
sys.exit(1 if bad else 0)
