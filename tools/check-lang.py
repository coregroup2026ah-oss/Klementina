"""Checks the language files in lang/ against the English texts in src/Strings.cs.

For every lang/<code>.txt it reports keys that are missing, unknown or duplicated, and texts whose
placeholders ({0}, {1}, ... and §HK§) or line breaks (\\n) differ from English.
Run from the repository root:  python tools/check-lang.py
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BS = chr(92)


def english():
    s = open(os.path.join(ROOT, 'src', 'Strings.cs'), encoding='utf-8').read()
    body = s[s.index('new Dictionary<string, string[]>'):]
    body = body[:body.index('        };')]
    texts = {}
    # entries look like: { "key", new[] { "arabic" + "...", "english" + "..." } },
    for m in re.finditer(r'\{\s*"([^"]+)",\s*new\[\]\s*\{(.*?)\}\s*\}', body, re.S):
        strings = re.findall(r'"((?:[^"' + BS + BS + r']|' + BS + BS + r'.)*)"|(,)', m.group(2))
        parts, cur = [], ''
        for lit, comma in strings:
            if comma:
                parts.append(cur)
                cur = ''
            else:
                cur += lit
        parts.append(cur)
        texts[m.group(1)] = parts[1] if len(parts) > 1 else parts[0]
    ops = open(os.path.join(ROOT, 'src', 'Ops.cs'), encoding='utf-8').read()
    block = ops[ops.index('arSuffix'):ops.index('public static string Suffix')]
    for key in re.findall(r'\{ "([A-Za-z]+)", "[^"]+" \}', block):
        texts['sfx.' + key] = key
    return texts


def marks(t):
    return sorted(re.findall(r'\{\d\}|§HK§', t)), t.count(BS + 'n')


def main():
    en = english()
    folder = os.path.join(ROOT, 'lang')
    problems = 0
    for name in sorted(os.listdir(folder)):
        if not name.endswith('.txt'):
            continue
        seen = {}
        dup = []
        for line in open(os.path.join(folder, name), encoding='utf-8'):
            line = line.strip()
            if not line or line.startswith('#') or '=' not in line:
                continue
            k, v = line.split('=', 1)
            k, v = k.strip(), v.strip()
            if k in seen:
                dup.append(k)
            seen[k] = v
        missing = [k for k in en if k not in seen]
        unknown = [k for k in seen if k not in en]
        bad = [k for k in seen if k in en and marks(seen[k]) != marks(en[k].replace('\n', BS + 'n'))]
        empty = [k for k, v in seen.items() if not v]
        n = len(missing) + len(unknown) + len(bad) + len(dup) + len(empty)
        problems += n
        status = 'ok' if n == 0 else 'PROBLEMS'
        print('%-10s %3d/%d  %s' % (name, len(seen), len(en), status))
        for label, keys in (('missing', missing), ('unknown', unknown), ('placeholders differ', bad), ('duplicated', dup), ('empty', empty)):
            if keys:
                print('    %s: %s' % (label, ', '.join(keys)))
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
