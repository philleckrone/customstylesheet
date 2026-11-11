# -*- coding: utf-8 -*-
r"""Keep the C# sources pure ASCII: replace every non-ASCII character in
CustomStyEditor/**/*.cs and CustomStyPlugin/**/*.cs with a \uXXXX escape (safe inside
C# string/char literals and harmless in comments). Prints the files it changed.

    python Custom_Stylesheet_Editor\tools\asciify_cs.py
"""
import io, os, re
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
for project in ('CustomStyEditor', 'CustomStyPlugin'):
    for dirpath, dirs, files in os.walk(os.path.join(ROOT, project)):
        if os.sep + 'obj' in dirpath or os.sep + 'bin' in dirpath:
            continue
        for fn in files:
            if not fn.endswith('.cs'):
                continue
            p = os.path.join(dirpath, fn)
            s = io.open(p, encoding='utf-8-sig').read()
            t = re.sub(u'[^\x00-\x7f]', lambda m: (u'\x5c' + u'u%04X') % ord(m.group(0)), s)
            if t != s:
                io.open(p, 'w', encoding='utf-8', newline='').write(t)
                print(os.path.relpath(p, ROOT))
