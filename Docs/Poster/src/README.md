# 포스터 그림 소스

- `chart.js`는 `parse.py`가 `Assets/_Project/Data/Bosses/GeomEulHyeon.bosschart.txt`에서 만든다.
- 글꼴(G마켓 산스 woff)은 저장소에 넣지 않았다. `GmarketSansTTFLight/Medium/Bold.woff`를 이 폴더에 두고 렌더한다.
- 다시 만들기: `python3 parse.py <채보> chart.js` → `node render.js <이 폴더> 3 <출력 폴더> fig1-loop fig2-phases fig3-validator fig4-bots`
- fig4 숫자는 `Docs/Battle11Balance.md`의 battle-11 최종안 값이다.
