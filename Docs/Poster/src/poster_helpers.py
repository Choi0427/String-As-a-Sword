"""Build a revised copy of the Softcon poster: same template and font, four figures added, one page."""
import copy, re, sys
from lxml import etree
from PIL import Image, ImageFont
from pptx import Presentation
from pptx.util import Emu

SRC, OUT, FIGDIR = sys.argv[1], sys.argv[2], sys.argv[3]
BODY = float(sys.argv[4]) if len(sys.argv) > 4 else 30
CAP = BODY - 6
CM = 360000
NS = {'a': 'http://schemas.openxmlformats.org/drawingml/2006/main', 'p': 'http://schemas.openxmlformats.org/presentationml/2006/main'}
FONT = ImageFont.truetype('/root/.fonts/gm-ttf-medium.ttf', 1000)
FACE = 'G마켓 산스 TTF Medium'
LINE = 1.215          # line height / font size, calibrated on the original boxes
INSET = 0.233         # top + bottom inset (cm)
BULLET_MAR = 0.0      # set per size below

prs = Presentation(SRC)
slide = prs.slides[0]
by_name = {}
for sh in slide.shapes:
    by_name.setdefault(sh.name, []).append(sh)
def shape(name, i=0): return by_name[name][i]

def pt_cm(pt): return pt * 0.035278
# PIL reports ~1.2x the advance width that Chromium and LibreOffice lay out for this font; keep 5% margin.
def text_w(t, pt, bold=False): return FONT.getlength(t) / 1000 * pt_cm(pt) * (1.04 if bold else 1.0) * 0.87

def wrap_count(text, pt, width, bold=False):
    plain = text.replace('**', '')
    words, lines, cur = re.split(r'(\s+)', plain), 1, ''
    for w in words:
        trial = cur + w
        if text_w(trial.rstrip(), pt, bold) <= width or not cur.strip():
            cur = trial
        else:
            lines += 1; cur = w.lstrip()
    return lines

def rpr(pt, kind, bold=False):
    r = etree.SubElement(etree.Element('dummy'), '{%s}rPr' % NS['a'])
    r.set('lang', 'ko-KR'); r.set('altLang', 'en-US'); r.set('sz', str(int(pt * 100))); r.set('dirty', '0')
    if bold or kind == 'title': r.set('b', '1')
    fill = etree.SubElement(r, '{%s}solidFill' % NS['a'])
    if kind == 'title':
        etree.SubElement(fill, '{%s}srgbClr' % NS['a']).set('val', 'C01C75')
    elif kind == 'caption':
        c = etree.SubElement(fill, '{%s}schemeClr' % NS['a']); c.set('val', 'bg1')
        etree.SubElement(c, '{%s}lumMod' % NS['a']).set('val', '65000')
    elif bold:
        etree.SubElement(fill, '{%s}srgbClr' % NS['a']).set('val', '000000')
    else:
        c = etree.SubElement(fill, '{%s}schemeClr' % NS['a']); c.set('val', 'tx1')
        etree.SubElement(c, '{%s}lumMod' % NS['a']).set('val', '75000')
        etree.SubElement(c, '{%s}lumOff' % NS['a']).set('val', '25000')
    for tag in ('latin', 'ea'):
        f = etree.SubElement(r, '{%s}%s' % (NS['a'], tag)); f.set('typeface', FACE); f.set('panose', '02000000000000000000'); f.set('pitchFamily', '2'); f.set('charset', '-127')
    return r

def set_text(sh, paras, pt, align='l'):
    """paras: list of (kind, text). kind in title|bullet|plain|caption. **x** = black bold."""
    body = sh.text_frame._txBody
    for p in body.findall('{%s}p' % NS['a']): body.remove(p)
    bp = body.find('{%s}bodyPr' % NS['a'])
    for k, v in (('wrap', 'square'), ('lIns', '83893'), ('tIns', '41947'), ('rIns', '83893'), ('bIns', '41947')): bp.set(k, v)
    mar = int(pt_cm(pt) * 1.25 * CM)
    for kind, text in paras:
        p = etree.SubElement(body, '{%s}p' % NS['a'])
        ppr = etree.SubElement(p, '{%s}pPr' % NS['a']); ppr.set('algn', align)
        if kind == 'bullet':
            ppr.set('marL', str(mar)); ppr.set('indent', str(-mar))
            bf = etree.SubElement(ppr, '{%s}buFont' % NS['a']); bf.set('typeface', 'Wingdings'); bf.set('pitchFamily', '2'); bf.set('charset', '2')
            etree.SubElement(ppr, '{%s}buChar' % NS['a']).set('char', '§')
        else:
            etree.SubElement(ppr, '{%s}buNone' % NS['a'])
        for i, part in enumerate(re.split(r'\*\*', text)):
            if not part: continue
            r = etree.SubElement(p, '{%s}r' % NS['a'])
            r.append(rpr(pt, kind, bold=(i % 2 == 1)))
            etree.SubElement(r, '{%s}t' % NS['a']).text = part
        end = etree.SubElement(p, '{%s}endParaRPr' % NS['a']); end.set('lang', 'ko-KR'); end.set('sz', str(int(pt * 100)))
    # height estimate
    width = sh.width / CM - 0.466
    lines = 0
    for kind, text in paras:
        w = width - (mar / CM if kind == 'bullet' else 0)
        lines += wrap_count(text, pt, w, bold=(kind == 'title')) if text else 1
    return lines * pt_cm(pt) * LINE + INSET

def place(sh, x, y, w, h=None):
    sh.left, sh.top, sh.width = Emu(int(x * CM)), Emu(int(y * CM)), Emu(int(w * CM))
    if h is not None: sh.height = Emu(int(h * CM))

def fig(path, x, y, w):
    iw, ih = Image.open(path).size
    h = w * ih / iw
    slide.shapes.add_picture(path, Emu(int(x * CM)), Emu(int(y * CM)), Emu(int(w * CM)), Emu(int(h * CM)))
    return h

def move_group(name, y):
    g = shape(name); g.top = Emu(int(y * CM))

def remove(sh): sh._element.getparent().remove(sh._element)

