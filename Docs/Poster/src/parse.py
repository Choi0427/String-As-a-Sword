import json, re, sys
src, out = sys.argv[1], sys.argv[2]
shapes, sections, attacks = {}, [], []
lines = open(src, encoding='utf-8').read().split('\n')
i = 0
while i < len(lines):
    raw = lines[i].split('#')[0].strip(); i += 1
    if not raw: continue
    if raw.startswith('section '):
        _, name, rng = raw.split(); a, b = rng.split('-'); sections.append({'name': name, 'from': int(a), 'to': int(b)})
    elif raw.startswith('shape '):
        parts = raw.split(); name = parts[1]; meta = dict(p.split('=', 1) for p in parts[2:] if '=' in p)
        grid = []
        while len(grid) < 8:
            row = lines[i].split('#')[0].strip(); i += 1
            if row: grid.append(row)
        shapes[name] = {'name': name, 'grid': grid, 'anchor': meta.get('anchor'), 'piece': meta.get('piece')}
    elif re.match(r'^\d+\s', raw):
        p = raw.split(); kv = dict(x.split('=') for x in p[3:] if '=' in x)
        attacks.append({'note': int(p[0]), 'shape': p[1], 'warning': int(p[2]), 'hits': int(kv.get('hits', 1)), 'every': int(kv.get('every', 1))})
open(out, 'w', encoding='utf-8').write('window.CHART=' + json.dumps({'shapes': shapes, 'sections': sections, 'attacks': attacks}, ensure_ascii=False) + ';')
print(len(shapes), 'shapes', len(attacks), 'attacks', len(sections), 'sections')
