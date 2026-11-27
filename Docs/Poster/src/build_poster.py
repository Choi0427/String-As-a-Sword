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
def text_w(t, pt, bold=False): return FONT.getlength(t) / 1000 * pt_cm(pt) * (1.04 if bold else 1.0)

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

LX, RX, W = 7.02, 44.05, 33.03
GAP, HEAD = 0.35, 2.8
report = []

# ── Left column ──
y = 20.13 + HEAD + 0.25
sh = shape('TextBox 6'); place(sh, LX, y, W)
h = set_text(sh, [('title', '노래의 메시지를 듣는 데서 플레이하는 경험으로'),
                  ('bullet', '이승윤의 <검을 현>은 체스판(정해진 규칙) 위에 박혀 있지 않겠다는 의지를 담은 곡'),
                  ('bullet', '곡을 배경음악으로 쓰는 데 그치지 않고, 곡의 메시지를 게임의 규칙 자체로 옮기는 것이 목표')], BODY)
y += h + GAP
sh = shape('TextBox 7'); place(sh, LX, y, W)
h = set_text(sh, [('title', '디자인 명제: 제약은 곧 서사'),
                  ('bullet', '박자에 묶인 이동'), ('bullet', '맨 윗줄에서만 닿는 검'), ('bullet', '잡음이 실리지 않으면 공격 불가'),
                  ('plain', '→ 플레이 내내 쌓인 답답함이 곡의 끝에서 판을 뚫고 나가는 쾌감으로')], BODY)
y += h + GAP
sh = shape('TextBox 9'); place(sh, LX, y, 16.4)
h = set_text(sh, [('title', '원곡이 곧 규칙'), ('plain', '**체스판** → 박자에 묶인 판'), ('plain', '**나이트** → 플레이어'), ('plain', '**현** → 뜯어 쥔 검')], BODY)
sh2 = shape('TextBox 20'); place(sh2, LX + 16.6, y, 16.4)
set_text(sh2, [('plain', ''), ('plain', '**잡음** → 규칙에 대항하는 소리'), ('plain', '**킹** → 판의 시스템 그 자체')], BODY)
y += h + 0.15
IMH = float(sys.argv[7]) if len(sys.argv) > 7 else 5.4
for name, side in (('그림 8', 0), ('그림 27', 1)):
    p = shape(name); ar = p.width / p.height; w = IMH * ar
    x0 = LX + W / 2 - (2 * w + 0.3) / 2
    place(p, x0 + side * (w + 0.3), y, w, IMH)
y += IMH
cap = shape('TextBox 30'); place(cap, LX, y, W)
h = set_text(cap, [('caption', '▲ 프롤로그: 판에 묶인 나이트, 현을 뜯어 쥔 검')], CAP, align='ctr')
y += h + 0.4
move_group('그룹 37', y); y += HEAD + 0.25
# Core loop: text left, figure right
sh = shape('TextBox 52'); place(sh, LX, y, 18.2)
h1 = set_text(sh, [('title', '핵심 루프: 내려가서 줍고, 올라가서 친다'),
                   ('bullet', '노트 321개 = 선택 321번. 노트마다 이동, 공격, 방어 중 하나'),
                   ('bullet', '① 잡음은 아래 5줄에만 생긴다. 내려가서 줍는다'),
                   ('bullet', '② 잡음으로 현을 충전하고 올라간다'),
                   ('bullet', '③ 맨 윗줄에서만 공격: 8노트 공명 (잡음이 없으면 공격 불가)')], BODY)
h2 = fig(f'{FIGDIR}/c1-loop.png', LX + 18.5, y + 0.2, W - 18.5)
y += max(h1, h2 + 0.2) + GAP
sh = shape('TextBox 33'); place(sh, LX, y, W)
h = set_text(sh, [('title', '곡이 곧 목숨인 보스: 규칙을 잃어가는 킹'),
                  ('bullet', '후렴마다 정통 체스 → 페어리 체스 → 존재하지 않는 체스 규칙으로 변하는 공격 80회를 곡에 고정'),
                  ('bullet', '왕관 4칸을 다 꺼뜨린 킹은 곡 끝 3연타에서 격파 → 곡의 끝과 맞물림')], BODY)
y += h + 0.2
y += fig(f'{FIGDIR}/c2-phases.png', LX, y, W) + GAP
sh = shape('TextBox 36'); place(sh, LX, y, W)
h = set_text(sh, [('title', '직접 판을 뚫고 나가는 경험'),
                  ('bullet', '판이 깨지면 격자도 박자도 없는 하얀 세계를 직접 돌아다님'),
                  ('bullet', '엔딩 이후에는 메인 화면도 하얀 세계로 바뀜')], BODY)
y += h + 0.15
IMH2 = float(sys.argv[5]) if len(sys.argv) > 5 else 6.0
for name, side in (('그림 32', 0), ('그림 47', 1)):
    p = shape(name); ar = p.width / p.height; w = IMH2 * ar
    x0 = LX + W / 2 - (2 * w + 0.3) / 2
    place(p, x0 + side * (w + 0.3), y, w, IMH2)
y += IMH2
cap = shape('TextBox 54'); place(cap, LX, y, W)
h = set_text(cap, [('caption', '▲ 엔딩 전(왼쪽)과 후(오른쪽)의 메인 화면')], CAP, align='ctr')
report.append(('left bottom', round(y + h, 2)))

# ── Right column ──
y = 20.13 + HEAD + 0.25
sh = shape('TextBox 4'); place(sh, RX, y, W)
h = set_text(sh, [('bullet', 'Unity 6, C#, Windows PC'),
                  ('bullet', '화면 프레임이 아닌 오디오 DSP 시계로 박자 계산'),
                  ('bullet', '±70ms Perfect, ±140ms Good 판정'),
                  ('bullet', 'RCM으로 직접 찍은 87BPM 채보, 321 노트'),
                  ('bullet', '8×8 모양을 텍스트로 쓰는 보스 채보와 공정성 검증기: 모든 출발 칸에서 피할 길이 있는지 자동 검사'),
                  ('bullet', '화면에 보이는 정보만 쓰는 봇으로 8만 판 이상 시뮬레이션 (실력 4단계, 전략 5종)')], BODY)
y += h + 0.2
y += fig(f'{FIGDIR}/c3-validator.png', RX, y, W) + 0.45
move_group('그룹 42', y); y += HEAD + 0.25
p = shape('그림 64'); IMH3 = float(sys.argv[6]) if len(sys.argv) > 6 else 10.5
w = IMH3 * p.width / p.height; place(p, RX + (W - w) / 2, y, w, IMH3); y += IMH3
cap = shape('TextBox 69'); place(cap, RX, y, W)
y += set_text(cap, [('caption', '▲ 인게임 플레이 화면')], CAP, align='ctr') + 0.15
sh = shape('TextBox 51'); place(sh, RX, y, W)
h = set_text(sh, [('title', '계획 대비 구현: 정량 목표 모두 달성'),
                  ('bullet', '보스 스테이지 1개: 프롤로그부터 엔딩까지 완성'),
                  ('bullet', '이동, 공격, 방어: 노트마다 하나를 선택하는 규칙'),
                  ('bullet', '체스 기반 보스 패턴: 후렴마다 바뀌는 3단계, 공격 80회'),
                  ('bullet', '계획 밖 추가: 잡음 시스템, 프롤로그, 튜토리얼')], BODY)
y += h + GAP
remove(shape('TextBox 63'))
sh = shape('TextBox 65'); place(sh, RX, y, W)
h = set_text(sh, [('title', '완성도'),
                  ('bullet', '자동 테스트 357개 전부 통과, 컴파일 오류 0'),
                  ('bullet', '봇 클리어율: 어려움(중수 81%), 쉬움(초보 77%)')], BODY)
y += h + 0.15
y += fig(f'{FIGDIR}/c4-bots.png', RX, y, W) + GAP
sh = shape('TextBox 67'); place(sh, RX, y, W)
h = set_text(sh, [('title', '남은 것'), ('bullet', '퀄리티 업(판정 표시, 타격 VFX 등), 사람 대상 플레이테스트')], BODY)
y += h + 0.4
move_group('그룹 44', y); y += HEAD + 0.25
sh = shape('TextBox 34'); place(sh, RX, y, W)
h = set_text(sh, [('bullet', '곡의 메시지를 규칙으로 옮기는 설계를 다른 곡, 아티스트와의 협업 콘텐츠로 확장'),
                  ('bullet', '퍼펙트 모드, 하드코어 모드 등 난이도와 스테이지 확장'),
                  ('bullet', '텍스트 채보, 공정성 검증기, 봇 시뮬레이션으로 새 스테이지의 밸런스 비용 절감'),
                  ('bullet', '사람 대상 플레이테스트로 봇의 실력 가정을 보정한 뒤 전시·공모전 출품, 인디 게임으로 발전')], BODY)
report.append(('right bottom (must be < 103.2)', round(y + h, 2)))

prs.save(OUT)
for r in report: print(*r)
