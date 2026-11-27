"""Poster v2: fewer elements, less text, larger type. Usage:
python3 build_poster_v2.py <src.pptx> <out.pptx> <figdir> <body pt> <in-game width cm>"""
import os, sys
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'poster_helpers.py'), encoding='utf-8').read())

PITCH, CAPT = BODY + 10, 26
INGAME_W = float(sys.argv[5]) if len(sys.argv) > 5 else 30
LX, RX, W = 7.02, 44.05, 33.03
HEAD = 2.8

def img_h(path, w):
    iw, ih = Image.open(path).size
    return w * ih / iw

def text_block(name, x, w, paras, pt, align='l'):
    sh = shape(name); place(sh, x, 0, w)
    h = set_text(sh, paras, pt, align)
    return sh, h

class Col:
    """Blocks stacked top to bottom; leftover height is spread evenly between blocks (headers stay tight)."""
    def __init__(self, top, bottom): self.top, self.bottom, self.blocks = top, bottom, []
    def add(self, h, put, tight=False): self.blocks.append((h, put, tight))
    def layout(self, gmin=0.5, gmax=1.5):
        loose = sum(1 for i, b in enumerate(self.blocks[:-1]) if not b[2])
        used = sum(b[0] for b in self.blocks) + 0.3 * sum(1 for b in self.blocks[:-1] if b[2])
        gap = max(gmin, min(gmax, (self.bottom - self.top - used) / max(loose, 1)))
        y = self.top
        for i, (h, put, tight) in enumerate(self.blocks):
            put(y); y += h + ((0.3 if tight else gap) if i < len(self.blocks) - 1 else 0)
        return round(y, 2), round(gap, 2)

def header(col, group):
    col.add(HEAD, lambda y: move_group(group, y), tight=True)

def put_text(sh, x):
    return lambda y: place(sh, x, y, sh.width / CM)

# Unused originals
for n in ('TextBox 20', 'TextBox 63', '그림 27'): remove(shape(n))

# ── Left column ──
L = Col(20.13, 110.6)
header(L, '그룹 21')
sh, h = text_block('TextBox 6', LX, W, [('title', '박자에 묶인 체스판 위의 나이트가 되어,'), ('title', '왕을 베고 판을 뚫고 나가는 리듬 액션 게임')], PITCH)
L.add(h, put_text(sh, LX), tight=True)
sh, h = text_block('TextBox 7', LX, W, [('bullet', "이승윤 <검을 현>의 '체스판을 벗어나겠다'는 메시지를 배경음악이 아닌 게임의 규칙으로")], BODY)
L.add(h, put_text(sh, LX))
# Mapping (left) + one prologue still (right)
map_sh, map_h = text_block('TextBox 9', LX, 16.2, [('title', '원곡이 곧 규칙'), ('plain', '**체스판** → 박자에 묶인 판'), ('plain', '**나이트** → 플레이어'),
                                                   ('plain', '**현** → 뜯어 쥔 검'), ('plain', '**킹** → 판의 시스템')], BODY)
pro = shape('그림 8'); PW = 16.4; PH = PW * pro.height / pro.width
cap_sh, cap_h = text_block('TextBox 30', LX + W - PW, PW, [('caption', '▲ 프롤로그: 판에 묶인 나이트')], CAPT, align='ctr')
def put_map(y):
    place(map_sh, LX, y, 16.2); place(pro, LX + W - PW, y + 0.15, PW, PH); place(cap_sh, LX + W - PW, y + 0.15 + PH, PW)
L.add(max(map_h, PH + 0.15 + cap_h), put_map)
header(L, '그룹 37')
loop_sh, loop_h = text_block('TextBox 52', LX, 18.0, [('title', '핵심 루프: 내려가서 줍고, 올라가서 친다'),
                                                     ('bullet', '노트마다 이동·공격·방어 중 하나를 고른다'),
                                                     ('bullet', '아래 5줄에서 잡음을 줍고, 맨 윗줄에서만 공격한다')], BODY)
LOOP = f'{FIGDIR}/c1-loop.png'; LW = W - 18.4; LH = img_h(LOOP, LW)
L.add(max(loop_h, LH), lambda y: (place(loop_sh, LX, y + max(0, (LH - loop_h) / 2), 18.0), fig(LOOP, LX + 18.4, y, LW)))
boss_sh, boss_h = text_block('TextBox 33', LX, W, [('title', '후렴마다 규칙을 잃어가는 킹'),
                                                   ('bullet', '곡에 고정된 공격 80회가 정통 체스 → 페어리 체스 → 체스에 없는 규칙으로 무너진다')], BODY)
PHS = f'{FIGDIR}/v2-phases.png'; PHW = W; PHH = img_h(PHS, PHW)
L.add(boss_h + 0.3 + PHH, lambda y: (place(boss_sh, LX, y, W), fig(PHS, LX, y + boss_h + 0.3, PHW)))
end_sh, end_h = text_block('TextBox 36', LX, W, [('title', '판을 뚫고 나가는 엔딩'),
                                                 ('bullet', '판이 깨지면 격자도 박자도 없는 하얀 세계로')], BODY)
e1, e2 = shape('그림 32'), shape('그림 47')
EH = min(float(sys.argv[6]) if len(sys.argv) > 6 else 8.6, (W - 0.3) / (e1.width / e1.height + e2.width / e2.height))  # pair must fit the column
ecap, ecap_h = text_block('TextBox 54', LX, W, [('caption', '▲ 엔딩 전(왼쪽)과 후(오른쪽)의 메인 화면')], CAPT, align='ctr')
def put_end(y):
    place(end_sh, LX, y, W); yy = y + end_h + 0.2
    w1, w2 = EH * e1.width / e1.height, EH * e2.width / e2.height; x0 = LX + (W - w1 - w2 - 0.3) / 2
    place(e1, x0, yy, w1, EH); place(e2, x0 + w1 + 0.3, yy, w2, EH); place(ecap, LX, yy + EH, W)
L.add(end_h + 0.2 + EH + ecap_h, put_end)

# ── Right column ──
R = Col(20.13, 102.3)
header(R, '그룹 39')
sh, h = text_block('TextBox 4', RX, W, [('bullet', '오디오 DSP 시계로 박자 계산, 판정 ±70 / ±140ms'),
                                        ('bullet', 'RCM으로 직접 찍은 87BPM 채보, 노트 321개'),
                                        ('bullet', '텍스트 보스 채보와 공정성 자동 검증기'),
                                        ('bullet', '화면 정보만 쓰는 봇으로 8만 판 시뮬레이션')], BODY)
R.add(h, put_text(sh, RX))
header(R, '그룹 42')
game = shape('그림 64'); GH = INGAME_W * game.height / game.width
gcap, gcap_h = text_block('TextBox 69', RX, W, [('caption', '▲ 인게임 화면: 노트에 맞춰 나이트가 한 칸씩 움직인다')], CAPT, align='ctr')
R.add(GH + gcap_h, lambda y: (place(game, RX + (W - INGAME_W) / 2, y, INGAME_W, GH), place(gcap, RX, y + GH, W)))
sh, h = text_block('TextBox 51', RX, W, [('title', '계획 대비 구현: 정량 목표 모두 달성'),
                                         ('bullet', '보스 스테이지 1개: 프롤로그부터 엔딩까지'),
                                         ('bullet', '후렴마다 바뀌는 3단계 패턴, 공격 80회'),
                                         ('bullet', '자동 테스트 357개 전부 통과')], BODY)
R.add(h, put_text(sh, RX))
bot_sh, bot_h = text_block('TextBox 65', RX, W, [('title', '봇 시뮬레이션 클리어율'),
                                                 ('bullet', '기준 플레이어: 어려움은 중수, 쉬움은 초보')], BODY)
BOTS = f'{FIGDIR}/v2-bots.png'; BW = W - 1; BH = img_h(BOTS, BW)
R.add(bot_h + 0.3 + BH, lambda y: (place(bot_sh, RX, y, W), fig(BOTS, RX + 0.5, y + bot_h + 0.3, BW)))
sh, h = text_block('TextBox 67', RX, W, [('title', '남은 것'), ('bullet', '판정 표시·타격 VFX, 사람 대상 플레이테스트')], BODY)
R.add(h, put_text(sh, RX))
header(R, '그룹 44')
sh, h = text_block('TextBox 34', RX, W, [('bullet', '다른 곡·아티스트와의 협업 스테이지로 확장'),
                                         ('bullet', '채보·검증기·봇으로 밸런스 비용 절감'),
                                         ('bullet', '사람 플레이테스트 후 전시·공모전 출품')], BODY)
R.add(h, put_text(sh, RX))

print('left  end, gap:', *L.layout(gmax=2.6))
print('right end, gap:', *R.layout(gmax=4.2))
prs.save(OUT)
