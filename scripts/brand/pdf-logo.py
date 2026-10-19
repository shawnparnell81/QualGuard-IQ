#!/usr/bin/env python3
"""Converts the QualGuard IQ logo SVG (web/public/brand/logo-full.svg) into PDF path operators so generated PDFs carry the
logo as vectors. Output: src/QualGuardIQ.Infrastructure/Services/Brand/logo-lockup.pdfops (mark + wordmark, no tagline;
first line is the view box). The ring cut-out is painted white, so the logo is meant for white page backgrounds.
The dot uses the flat brand blue of mark.svg (the gradient is for screens). Run from the repository root."""
import math, re, sys

SRC = 'web/public/brand/logo-full.svg'
OUT = 'src/QualGuardIQ.Infrastructure/Services/Brand/logo-lockup.pdfops'
svg = open(SRC).read()

def rgb(h):
    h = h.lstrip('#'); return ' '.join(f'{int(h[i:i+2], 16) / 255:.4f}'.rstrip('0').rstrip('.') or '0' for i in (0, 2, 4))

def fmt(v): return f'{v:.2f}'.rstrip('0').rstrip('.')

# group transform of the mark: translate(280 281) scale(0.3636) translate(-270 -265)
gm = re.search(r'<g transform="translate\(([\d.]+) ([\d.]+)\) scale\(([\d.]+)\) translate\((-?[\d.]+) (-?[\d.]+)\)">', svg)
tx, ty, sc, ux, uy = map(float, gm.groups())
def T(x, y): return (tx + sc * (x + ux), ty + sc * (y + uy))

bounds = []
def note(x, y): bounds.append((x, y))

def path_ops(d, transform=None):
    """Absolute M/L/H/V/C/Q/Z path data to PDF operators (quadratics raised to cubics)."""
    toks = re.findall(r'[MLHVCQZ]|-?\d+(?:\.\d+)?(?:e-?\d+)?', d)
    out, i, cmd = [], 0, None
    cx = cy = sx = sy = 0.0
    P = transform or (lambda x, y: (x, y))
    def num():
        nonlocal i; v = float(toks[i]); i += 1; return v
    while i < len(toks):
        t = toks[i]
        if t.isalpha():
            cmd = t; i += 1
            if cmd == 'Z': out.append('h'); cx, cy = sx, sy
            continue
        if cmd == 'M':
            cx, cy = num(), num(); sx, sy = cx, cy; X, Y = P(cx, cy); note(X, Y); out.append(f'{fmt(X)} {fmt(Y)} m'); cmd = 'L'
        elif cmd in ('L', 'H', 'V'):
            if cmd == 'L': cx, cy = num(), num()
            elif cmd == 'H': cx = num()
            else: cy = num()
            X, Y = P(cx, cy); note(X, Y); out.append(f'{fmt(X)} {fmt(Y)} l')
        elif cmd == 'C':
            raise SystemExit('cubic segments are not expected in the glyph runs')
        elif cmd == 'Q':
            qx, qy, ex, ey = num(), num(), num(), num()
            c1 = (cx + 2 / 3 * (qx - cx), cy + 2 / 3 * (qy - cy)); c2 = (ex + 2 / 3 * (qx - ex), ey + 2 / 3 * (qy - ey))
            pts = [P(*c1), P(*c2), P(ex, ey)]
            out.append(' '.join(f'{fmt(a)} {fmt(b)}' for a, b in pts) + ' c'); note(*pts[2]); cx, cy = ex, ey
        else:
            raise SystemExit(f'unsupported command {cmd}')
    return out

# C commands only occur in the mark's tail; parse them with source tracking
def path_ops_mark(d):
    toks = re.findall(r'[MLHVCQZ]|-?\d+(?:\.\d+)?', d); out = []; i = 0; cmd = None
    while i < len(toks):
        t = toks[i]
        if t.isalpha():
            cmd = t; i += 1
            if cmd == 'Z': out.append('h')
            continue
        n = {'M': 2, 'L': 2, 'C': 6}[cmd]
        vals = [float(v) for v in toks[i:i + n]]; i += n
        pts = [T(vals[k], vals[k + 1]) for k in range(0, n, 2)]
        for p in pts: note(*p)
        out.append(' '.join(f'{fmt(a)} {fmt(b)}' for a, b in pts) + {'M': ' m', 'L': ' l', 'C': ' c'}[cmd])
        if cmd == 'M': cmd = 'L'
    return out

def circle(cx, cy, r):
    k = 0.5523 * r
    return [f'{fmt(cx + r)} {fmt(cy)} m',
            f'{fmt(cx + r)} {fmt(cy + k)} {fmt(cx + k)} {fmt(cy + r)} {fmt(cx)} {fmt(cy + r)} c',
            f'{fmt(cx - k)} {fmt(cy + r)} {fmt(cx - r)} {fmt(cy + k)} {fmt(cx - r)} {fmt(cy)} c',
            f'{fmt(cx - r)} {fmt(cy - k)} {fmt(cx - k)} {fmt(cy - r)} {fmt(cx)} {fmt(cy - r)} c',
            f'{fmt(cx + k)} {fmt(cy - r)} {fmt(cx + r)} {fmt(cy - k)} {fmt(cx + r)} {fmt(cy)} c', 'h']

ops = []
charcoal = '#29333E'; blue = '#1257C4'; green = '#0E8550'
tail = re.search(r'<path d="(M298 335[^"]*)" fill="#29333E"', svg).group(1)
ccx, ccy = T(270, 265)
# ring
for x, y in [(ccx - (192 + 23) * sc, ccy - (192 + 23) * sc), (ccx + (192 + 23) * sc, ccy + (192 + 23) * sc)]: note(x, y)
ops += [f'{rgb(charcoal)} RG {fmt(46 * sc)} w'] + circle(ccx, ccy, 192 * sc) + ['S']
# cut-out where the tail crosses the ring (white fill plus a 26-unit white stroke, as in the SVG mask)
ops += [f'1 1 1 rg 1 1 1 RG {fmt(26 * sc)} w 1 j'] + path_ops_mark(tail) + ['B', '0 j']
# stems and tail
for x, y, w, h in [(243, 128, 54, 62), (243, 322, 54, 86)]:
    X, Y = T(x, y); ops.append(f'{rgb(charcoal)} rg {fmt(X)} {fmt(Y)} {fmt(w * sc)} {fmt(h * sc)} re f')
ops += [f'{rgb(charcoal)} rg'] + path_ops_mark(tail) + ['f']
# dot
ops += [f'{rgb(blue)} rg'] + circle(ccx, ccy, 58 * sc) + ['f']
# wordmark ("qualguard" charcoal, "IQ" in the accent green: the screen gradient runs blue to green); the tagline (third glyph run) is left out
runs = re.findall(r'<path d="([^"]{3000,})" fill="(#[0-9A-Fa-f]{6}|url\(#iq\))"/>', svg)
for d, fill in runs[:2]:
    if fill.startswith('url'): fill = green
    ops += [f'{rgb(fill)} rg'] + path_ops(d) + ['f']
xs = [b[0] for b in bounds]; ys = [b[1] for b in bounds]
x0, y0, x1, y1 = min(xs) - 1, min(ys) - 1, max(xs) + 1, max(ys) + 1
open(OUT, 'w').write(f'viewBox {fmt(x0)} {fmt(y0)} {fmt(x1 - x0)} {fmt(y1 - y0)}\n' + '\n'.join(ops) + '\n')
print(OUT, f'viewBox {x0:.1f} {y0:.1f} {x1 - x0:.1f} {y1 - y0:.1f}', len(ops), 'operators')
