// Shared 8x8 board renderer. grid[0] = rank 8 (top row), as in the boss chart text.
const ACCENT = '#C01C75', ACCENT_DARK = '#9B1E60';
const PIECE = { '폰': '♟', '룩': '♜', '비숍': '♝', '나이트': '♞', '킹': '♚', '퀸': '♛' };

function hatchDefs(id) {
  return `<defs><pattern id="${id}" width="14" height="14" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
    <rect width="14" height="14" fill="#F3C9DE"/><rect width="7" height="14" fill="${ACCENT_DARK}"/></pattern></defs>`;
}

// opts: size, grid, labels, rowTint {rows:[r..], fill}, pieces [{r,c,glyph,ghost}], noise [{r,c}], text [{r,c,t,fill,weight}],
//       cellFill (r,c)=>color|null, outline [{r,c,stroke}], id
function board(opts) {
  const n = 8, size = opts.size || 480, s = size / n, pad = opts.labels ? 30 : 0, id = opts.id || ('h' + Math.random().toString(36).slice(2, 7));
  let out = `<svg xmlns="http://www.w3.org/2000/svg" width="${size + pad}" height="${size + pad}" viewBox="${-pad} 0 ${size + pad} ${size + pad}">${hatchDefs(id)}`;
  for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) {
    const x = c * s, y = r * s, v = opts.grid ? opts.grid[r][c] : '0';
    out += `<rect x="${x}" y="${y}" width="${s}" height="${s}" fill="${(r + c) % 2 ? 'var(--sq-dark)' : 'var(--sq-light)'}"/>`;
    if (opts.rowTint) for (const t of opts.rowTint) if (t.rows.includes(r)) out += `<rect x="${x}" y="${y}" width="${s}" height="${s}" fill="${t.fill}"/>`;
    const custom = opts.cellFill && opts.cellFill(r, c);
    if (custom) out += `<rect x="${x}" y="${y}" width="${s}" height="${s}" fill="${custom}"/>`;
    else if (v === '1') out += `<rect x="${x}" y="${y}" width="${s}" height="${s}" fill="${ACCENT}" fill-opacity="0.88"/>`;
    else if (v === '2') out += `<rect x="${x}" y="${y}" width="${s}" height="${s}" fill="url(#${id})"/>`;
  }
  out += `<rect x="1.5" y="1.5" width="${size - 3}" height="${size - 3}" fill="none" stroke="#1a1a1a" stroke-width="3"/>`;
  for (const o of opts.outline || []) out += `<rect x="${o.c * s + 3}" y="${o.r * s + 3}" width="${s - 6}" height="${s - 6}" fill="none" stroke="${o.stroke || '#111'}" stroke-width="${o.width || 5}" rx="4"/>`;
  for (const p of opts.noise || []) out += noiseIcon(p.c * s + s / 2, p.r * s + s / 2, s * 0.62);
  for (const p of opts.pieces || []) {
    const fs = s * (p.scale || 0.78);
    out += `<text x="${p.c * s + s / 2}" y="${p.r * s + s / 2 + fs * 0.36}" text-anchor="middle" font-size="${fs}" class="chess"
      fill="${p.color || '#111'}" fill-opacity="${p.ghost ? 0.9 : 1}" stroke="${p.stroke || '#fff'}" stroke-width="${p.ghost ? 2.5 : 2}" paint-order="stroke">${p.glyph}</text>`;
  }
  for (const t of opts.text || []) out += `<text x="${t.c * s + s / 2}" y="${t.r * s + s / 2 + (t.size || s * 0.42) * 0.36}" text-anchor="middle"
      font-size="${t.size || s * 0.42}" font-weight="${t.weight || 700}" fill="${t.fill || '#111'}" font-family="G">${t.t}</text>`;
  if (opts.labels) {
    for (let i = 0; i < n; i++) {
      out += `<text x="${i * s + s / 2}" y="${size + 24}" text-anchor="middle" font-size="17" fill="#888" font-family="G">${'abcdefgh'[i]}</text>`;
      out += `<text x="${-14}" y="${i * s + s / 2 + 6}" text-anchor="middle" font-size="17" fill="#888" font-family="G">${8 - i}</text>`;
    }
  }
  return out + (opts.extra || '') + '</svg>';
}

function noiseIcon(cx, cy, w) {
  // Jagged "noise" waveform in a dark rounded tile, matching the in-game 잡음 pickup idea.
  const h = w * 0.55, pts = [0, .5, .15, .15, .3, .85, .45, .05, .6, .95, .75, .25, .88, .7, 1, .5];
  let d = '';
  for (let i = 0; i < pts.length; i += 2) d += (i ? 'L' : 'M') + (cx - w / 2 + pts[i] * w).toFixed(1) + ' ' + (cy - h / 2 + pts[i + 1] * h).toFixed(1);
  return `<rect x="${cx - w / 2 - 6}" y="${cy - w / 2 - 2}" width="${w + 12}" height="${w + 4}" rx="10" fill="#111"/>
    <path d="${d}" fill="none" stroke="#fff" stroke-width="${w * 0.07}" stroke-linejoin="round" stroke-linecap="round"/>`;
}

function anchorCell(shape) {
  if (!shape.anchor) return null;
  const [r, c] = shape.anchor.split(',').map(Number);
  return { r: r - 1, c: c - 1 };
}
function piecesOf(shape) {
  return (shape.piece || '').split('+').filter(Boolean).map(p => PIECE[p]).join('');
}
