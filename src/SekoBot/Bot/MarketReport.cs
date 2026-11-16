namespace SekoBot.Bot;

/// <summary>
/// The market scan as a page to look at (opens in the browser, works without internet): the
/// market map with every shop, a search over all items, each item's cheapest seller, average and
/// middle price, every listing sorted by price, and the wanted items first. The scan's data is
/// put into the page as it is.
/// </summary>
internal static class MarketReport
{
    /// <param name="walls">The map's obstacles to draw under the shops (see <see cref="MapReport.Outlines"/>).</param>
    public static string Html(string json, string walls = "[]") =>
        Template.Replace("__WALLS__", walls).Replace("__DATA__", json.Replace("</", "<\\/"));

    private const string Template = """
<!doctype html>
<html lang="tr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Pazar Raporu</title>
<style>
:root {
  --bg: #0f1117; --panel: #171a23; --panel2: #1e2230; --line: #2a2f3f; --text: #e8eaf0; --muted: #8b91a5;
  --accent: #5b8cff; --accent2: #7aa2ff; --good: #3ecf8e; --warn: #f5b84b; --bad: #ff6b6b; --gold: #ffd166;
}
* { box-sizing: border-box; }
html, body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.45 "Segoe UI", system-ui, sans-serif; }
a { color: var(--accent2); cursor: pointer; text-decoration: none; }
a:hover { text-decoration: underline; }
header { padding: 18px 24px 8px; display: flex; flex-wrap: wrap; align-items: baseline; gap: 12px 20px; }
header h1 { margin: 0; font-size: 22px; letter-spacing: .3px; }
header .sub { color: var(--muted); }
.kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; padding: 8px 24px; }
.kpi { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 12px 14px; }
.kpi .v { font-size: 22px; font-weight: 600; }
.kpi .l { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: .6px; }
section.wanted { padding: 8px 24px; }
section.wanted h2, .panel h2 { font-size: 13px; text-transform: uppercase; letter-spacing: .8px; color: var(--muted); margin: 6px 0 10px; }
.cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }
.card { background: linear-gradient(180deg, #1c2233, #171a23); border: 1px solid var(--line); border-radius: 12px; padding: 12px 14px; cursor: pointer; }
.card:hover { border-color: var(--accent); }
.card .n { font-weight: 600; font-size: 15px; }
.card .p { font-size: 20px; color: var(--good); font-weight: 600; margin: 4px 0; }
.card .m { color: var(--muted); font-size: 12px; }
.card.none .p { color: var(--muted); font-size: 14px; }
.badge { display: inline-block; padding: 1px 8px; border-radius: 999px; font-size: 11px; font-weight: 600; }
.badge.ok { background: rgba(62,207,142,.15); color: var(--good); }
.badge.no { background: rgba(255,107,107,.12); color: var(--bad); }
main { display: grid; grid-template-columns: minmax(380px, 1.05fr) minmax(380px, 1fr); gap: 16px; padding: 12px 24px 24px; }
@media (max-width: 1000px) { main { grid-template-columns: 1fr; } }
.panel { background: var(--panel); border: 1px solid var(--line); border-radius: 14px; padding: 14px; min-width: 0; }
.tools { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 10px; }
input[type=search] { flex: 1; min-width: 200px; background: var(--panel2); border: 1px solid var(--line); color: var(--text); border-radius: 10px; padding: 9px 12px; font-size: 14px; outline: none; }
input[type=search]:focus { border-color: var(--accent); }
label.opt { color: var(--muted); font-size: 13px; display: flex; gap: 6px; align-items: center; user-select: none; }
.tablewrap { max-height: 68vh; overflow: auto; border-radius: 10px; border: 1px solid var(--line); }
table { width: 100%; border-collapse: collapse; }
th, td { padding: 6px 8px; text-align: left; white-space: nowrap; font-size: 13px; }
th { position: sticky; top: 0; background: var(--panel2); color: var(--muted); font-weight: 600; font-size: 12px; cursor: pointer; user-select: none; z-index: 1; }
th.num, td.num { text-align: right; }
th:hover { color: var(--text); }
th.sorted::after { content: " ▾"; } th.sorted.asc::after { content: " ▴"; }
tbody tr { border-top: 1px solid var(--line); }
tbody tr:hover { background: #20263a; }
tbody tr.sel { background: #24305a; }
td.name { white-space: normal; max-width: 280px; }
td .bon { color: var(--warn); font-size: 12px; white-space: normal; }
.cheap { color: var(--good); font-weight: 600; }
.muted { color: var(--muted); }
canvas { width: 100%; height: 360px; background: radial-gradient(circle at 50% 40%, #182035, #11141c); border-radius: 10px; border: 1px solid var(--line); display: block; }
.legend { display: flex; gap: 14px; color: var(--muted); font-size: 12px; margin: 8px 2px 0; flex-wrap: wrap; }
.dot { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin-right: 5px; vertical-align: -1px; }
#detail { margin-top: 14px; }
#detail h3 { margin: 0 0 6px; font-size: 18px; }
.chips { display: flex; flex-wrap: wrap; gap: 8px; margin: 8px 0 10px; }
.chip { background: var(--panel2); border: 1px solid var(--line); border-radius: 10px; padding: 6px 10px; }
.chip b { display: block; font-size: 15px; }
.chip span { color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: .5px; }
#tip { position: fixed; pointer-events: none; background: #0b0d12ee; border: 1px solid var(--line); border-radius: 8px; padding: 6px 9px; font-size: 12px; display: none; z-index: 10; }
.overlay { position: fixed; inset: 0; background: #000a; display: none; align-items: center; justify-content: center; z-index: 20; }
.modal { background: var(--panel); border: 1px solid var(--line); border-radius: 14px; width: min(860px, 94vw); max-height: 86vh; overflow: auto; padding: 16px; }
.modal .x { float: right; font-size: 20px; cursor: pointer; color: var(--muted); }
.empty { color: var(--muted); padding: 30px; text-align: center; }
footer { color: var(--muted); font-size: 12px; padding: 0 24px 24px; }
</style>
</head>
<body>
<header>
  <h1>🛒 Pazar Raporu</h1>
  <span class="sub" id="sub"></span>
</header>
<div class="kpis" id="kpis"></div>
<section class="wanted" id="wanted" style="display:none"><h2>Aradıkların</h2><div class="cards" id="wcards"></div></section>
<main>
  <div class="panel">
    <h2>Bütün eşyalar</h2>
    <div class="tools">
      <input type="search" id="q" placeholder="Eşya ara… (ör. kavrama, +9, kitap)" autofocus>
      <label class="opt"><input type="checkbox" id="inBon"> efsunlarda da ara</label>
      <label class="opt"><input type="checkbox" id="multi"> en az 2 dükkanda olanlar</label>
    </div>
    <div class="tablewrap"><table id="items">
      <thead><tr>
        <th data-k="name">Eşya</th><th class="num" data-k="shops">Dükkan</th><th class="num" data-k="pieces">Adet</th>
        <th class="num" data-k="min">En ucuz</th><th class="num" data-k="average">Ortalama</th><th class="num" data-k="max">En pahalı</th>
      </tr></thead><tbody></tbody></table></div>
    <div class="muted" id="count" style="margin-top:8px"></div>
  </div>
  <div class="panel">
    <h2>Pazar haritası</h2>
    <canvas id="map"></canvas>
    <div class="legend">
      <span><i class="dot" style="background:#3a4257"></i>okunan dükkan</span>
      <span><i class="dot" style="background:#262b38"></i>açılmayan</span>
      <span><i class="dot" style="background:var(--good)"></i>ucuz</span>
      <span><i class="dot" style="background:var(--warn)"></i>orta</span>
      <span><i class="dot" style="background:var(--bad)"></i>pahalı</span>
      <span><i class="dot" style="background:var(--gold);box-shadow:0 0 0 2px #fff"></i>en ucuz</span>
    </div>
    <div id="detail"><div class="empty">Bir eşyaya tıkla: kimde var, kaça, nerede — haritada da işaretlenir.</div></div>
  </div>
</main>
<footer>Bir ürünün dükkanına gitmek için: SekoBot'ta Pazar sekmesinde ara, seç ve "🚶 Dükkana götür"e bas. Fiyatlar bir tanesi için: yığınlar adede bölündü. "Ortalama", en ucuz ve en pahalı onda birler (uç / şaka fiyatlar) çıkarılarak hesaplandı. SekoBot hiçbir şey satın almaz; sadece bakar.</footer>
<div id="tip"></div>
<div class="overlay" id="ov"><div class="modal" id="modal"></div></div>
<script>
const D = __DATA__;
const tr = new Intl.NumberFormat('tr-TR');
const yang = p => p >= 1e9 ? (p / 1e9).toLocaleString('tr-TR', {maximumFractionDigits: 2}) + ' milyar'
  : p >= 1e6 ? (p / 1e6).toLocaleString('tr-TR', {maximumFractionDigits: 2}) + ' milyon' : tr.format(Math.round(p));
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;'}[c]));
const low = s => String(s ?? '').toLocaleLowerCase('tr-TR');
const unit = i => i.count > 1 ? i.price / i.count : i.price;
const seen = t => { const d = new Date(t), m = Math.round((Date.now() - d) / 60000);
  return m < 1 ? 'şimdi' : m < 60 ? m + ' dk önce' : m < 1440 ? Math.round(m / 60) + ' sa önce' : d.toLocaleDateString('tr-TR'); };
const shopsById = new Map(D.shops.map(s => [s.id, s]));
const listings = D.shops.flatMap(s => s.items.map(i => ({...i, shop: s, unit: unit(i)})));
const byName = new Map();
for (const l of listings) { if (!byName.has(l.name)) byName.set(l.name, []); byName.get(l.name).push(l); }
const stats = D.stats;
const bonusText = new Map([...byName].map(([n, ls]) => [n, low(ls.map(l => l.bonuses).join(' '))]));

// ── header ──
document.getElementById('sub').textContent = `${D.map} · son güncelleme ${new Date(D.saved).toLocaleString('tr-TR')} · kapanan dükkanlar listeden çıkarılır`;
const total = listings.reduce((a, l) => a + l.price, 0);
const kpi = (v, l) => `<div class="kpi"><div class="v">${v}</div><div class="l">${l}</div></div>`;
document.getElementById('kpis').innerHTML =
  kpi(tr.format(D.shops.length) + (D.all.length ? ` <span class="muted" style="font-size:14px">/ ${tr.format(D.all.length)}</span>` : ''), 'Okunan dükkan') +
  kpi(tr.format(listings.length), 'Ürün') + kpi(tr.format(stats.length), 'Farklı eşya') + kpi(yang(total), 'Toplam değer (yang)');

// ── wanted ──
function matches(name, want) { return low(name).includes(low(want.name.trim())); }
if (D.wanted && D.wanted.length) {
  document.getElementById('wanted').style.display = '';
  document.getElementById('wcards').innerHTML = D.wanted.map((w, k) => {
    const found = stats.filter(s => matches(s.name, w));
    if (!found.length) return `<div class="card none"><div class="n">${esc(w.name)}</div><div class="p">Bulunamadı</div>
      <div class="m">${w.maxPrice > 0 ? 'en fazla ' + yang(w.maxPrice) + ' · ' : ''}okunan dükkanlarda yok</div></div>`;
    const best = found.reduce((a, b) => a.min <= b.min ? a : b);
    const ok = w.maxPrice > 0 ? (best.min <= w.maxPrice ? '<span class="badge ok">fiyatı uygun</span>' : '<span class="badge no">pahalı</span>') : '';
    const pieces = found.reduce((a, s) => a + s.pieces, 0), shops = new Set(found.flatMap(s => byName.get(s.name).map(l => l.shop.id))).size;
    return `<div class="card" data-name="${esc(best.name)}"><div class="n">${esc(w.name)} ${ok}</div>
      <div class="p">${yang(best.min)} <span class="muted" style="font-size:13px">/ tanesi</span></div>
      <div class="m">en ucuz: <b>${esc(best.minSeller)}</b> · ${esc(best.name)}<br>ortalama ${yang(best.average)} · ${shops} dükkan, ${tr.format(pieces)} adet${w.maxPrice > 0 ? ' · hedef ≤ ' + yang(w.maxPrice) : ''}</div></div>`;
  }).join('');
  document.querySelectorAll('.card[data-name]').forEach(c => c.onclick = () => select(c.dataset.name, true));
}

// ── table ──
let sortKey = 'name', asc = true, selected = null;
const tbody = document.querySelector('#items tbody');
function render() {
  const q = low(document.getElementById('q').value.trim()), inBon = document.getElementById('inBon').checked, multi = document.getElementById('multi').checked;
  let rows = stats.filter(s => (!q || low(s.name).includes(q) || inBon && bonusText.get(s.name).includes(q)) && (!multi || s.shops >= 2));
  rows.sort((a, b) => { const x = a[sortKey], y = b[sortKey]; const c = typeof x === 'string' ? x.localeCompare(y, 'tr') : x - y; return asc ? c : -c; });
  document.getElementById('count').textContent = `${tr.format(rows.length)} eşya gösteriliyor`;
  tbody.innerHTML = rows.slice(0, 1500).map(s => `<tr data-name="${esc(s.name)}" class="${s.name === selected ? 'sel' : ''}">
    <td class="name">${esc(s.name)}</td><td class="num">${s.shops}</td><td class="num">${tr.format(s.pieces)}</td>
    <td class="num cheap">${yang(s.min)}</td><td class="num">${yang(s.average)}</td><td class="num muted">${yang(s.max)}</td></tr>`).join('');
  document.querySelectorAll('th').forEach(th => { th.classList.toggle('sorted', th.dataset.k === sortKey); th.classList.toggle('asc', th.dataset.k === sortKey && asc); });
}
tbody.onclick = e => { const tr_ = e.target.closest('tr'); if (tr_) select(tr_.dataset.name); };
document.querySelectorAll('th').forEach(th => th.onclick = () => { const k = th.dataset.k; asc = sortKey === k ? !asc : k === 'name'; sortKey = k; render(); });
['q', 'inBon', 'multi'].forEach(id => document.getElementById(id).oninput = render);

// ── detail ──
function select(name, scroll) {
  selected = name; render(); draw();
  const ls = (byName.get(name) || []).slice().sort((a, b) => a.unit - b.unit), s = stats.find(x => x.name === name);
  if (!s) return;
  document.getElementById('detail').innerHTML = `<h3>${esc(name)}</h3>
    <div class="chips">
      <div class="chip"><b class="cheap">${yang(s.min)}</b><span>en ucuz</span></div>
      <div class="chip"><b>${yang(s.average)}</b><span>ortalama</span></div>
      <div class="chip"><b>${yang(s.median)}</b><span>orta fiyat</span></div>
      <div class="chip"><b>${yang(s.max)}</b><span>en pahalı</span></div>
      <div class="chip"><b>${s.shops}</b><span>dükkan</span></div>
      <div class="chip"><b>${tr.format(s.pieces)}</b><span>adet</span></div>
    </div>
    <div class="tablewrap" style="max-height:40vh"><table><thead><tr><th>Satıcı</th><th class="num">Adet</th><th class="num">Fiyat</th><th class="num">Tanesi</th><th>Yer</th><th>Okundu</th></tr></thead><tbody>
    ${ls.map((l, k) => `<tr><td><a data-shop="${l.shop.id}">${esc(l.shop.owner)}</a> <span class="muted">Sv ${l.shop.level}</span>${l.bonuses ? `<div class="bon">${esc(l.bonuses)}</div>` : ''}</td>
      <td class="num">${tr.format(l.count)}</td><td class="num">${yang(l.price)}</td><td class="num ${k === 0 ? 'cheap' : ''}">${yang(l.unit)}</td>
      <td class="muted">${l.shop.x || l.shop.y ? Math.round(l.shop.x / 100) + ', ' + Math.round(l.shop.y / 100) : '—'}</td>
      <td class="muted">${seen(l.shop.seen)}</td></tr>`).join('')}
    </tbody></table></div>`;
  document.querySelectorAll('#detail a[data-shop]').forEach(a => a.onclick = () => showShop(+a.dataset.shop));
  if (scroll) document.getElementById('map').scrollIntoView({behavior: 'smooth', block: 'start'});
}

// ── one shop ──
function showShop(id) {
  const s = shopsById.get(id); if (!s) return;
  const items = s.items.slice().sort((a, b) => b.price - a.price);
  document.getElementById('modal').innerHTML = `<span class="x" id="cx">✕</span><h3 style="margin:0">${esc(s.owner)} <span class="muted">Sv ${s.level}</span></h3>
    <div class="muted">${esc(s.title || '')} · dükkan ${s.id} · ${s.items.length} ürün · toplam ${yang(s.items.reduce((a, i) => a + i.price, 0))}</div>
    <div class="tablewrap" style="margin-top:10px;max-height:66vh"><table><thead><tr><th>Eşya</th><th class="num">Adet</th><th class="num">Fiyat</th><th class="num">Tanesi</th></tr></thead><tbody>
    ${items.map(i => `<tr><td class="name"><a data-name="${esc(i.name)}">${esc(i.name)}</a>${i.bonuses ? `<div class="bon">${esc(i.bonuses)}</div>` : ''}</td>
      <td class="num">${tr.format(i.count)}</td><td class="num">${yang(i.price)}</td><td class="num">${yang(unit(i))}</td></tr>`).join('')}</tbody></table></div>`;
  const ov = document.getElementById('ov'); ov.style.display = 'flex';
  document.getElementById('cx').onclick = () => ov.style.display = 'none';
  document.querySelectorAll('#modal a[data-name]').forEach(a => a.onclick = () => { ov.style.display = 'none'; select(a.dataset.name, true); });
}
document.getElementById('ov').onclick = e => { if (e.target.id === 'ov') e.target.style.display = 'none'; };
document.addEventListener('keydown', e => { if (e.key === 'Escape') document.getElementById('ov').style.display = 'none'; });

// ── map ──
const cv = document.getElementById('map'), ctx = cv.getContext('2d'), tip = document.getElementById('tip');
const pts = D.all.length ? D.all.map(a => ({id: a[0], x: a[1], y: a[2]})) : D.shops.map(s => ({id: s.id, x: s.x, y: s.y}));
const xs = pts.map(p => p.x).filter(Boolean), ys = pts.map(p => p.y).filter(Boolean);
const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
let marks = [];
function place() {
  const r = cv.getBoundingClientRect(), dpr = devicePixelRatio || 1;
  cv.width = r.width * dpr; cv.height = r.height * dpr; ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  const pad = 16, sx = (r.width - 2 * pad) / Math.max(1, maxX - minX), sy = (r.height - 2 * pad) / Math.max(1, maxY - minY), s = Math.min(sx, sy);
  const ox = (r.width - (maxX - minX) * s) / 2, oy = (r.height - (maxY - minY) * s) / 2;
  return p => [ox + (p.x - minX) * s, oy + (p.y - minY) * s];
}
const W = __WALLS__;
function draw() {
  const at = place(); ctx.clearRect(0, 0, cv.width, cv.height);
  // The map's walls, houses and rocks (from the game) under the shops.
  ctx.fillStyle = '#4a5268aa';
  for (const w of W) {
    let near = false;
    for (let i = 0; i < w.length; i += 2) if (w[i] >= minX - 3000 && w[i] <= maxX + 3000 && w[i + 1] >= minY - 3000 && w[i + 1] <= maxY + 3000) { near = true; break; }
    if (!near) continue;
    ctx.beginPath();
    for (let i = 0; i < w.length; i += 2) { const [x, y] = at({x: w[i], y: w[i + 1]}); i ? ctx.lineTo(x, y) : ctx.moveTo(x, y); }
    ctx.closePath(); ctx.fill();
  }
  for (const p of pts) { if (!p.x) continue; const [x, y] = at(p); ctx.fillStyle = shopsById.has(p.id) ? '#3a4257' : '#262b38'; ctx.fillRect(x - 1.5, y - 1.5, 3, 3); }
  marks = [];
  const ls = selected ? (byName.get(selected) || []) : [];
  if (ls.length) {
    const sorted = ls.map(l => l.unit).sort((a, b) => a - b), q = v => sorted.findIndex(u => u >= v) / Math.max(1, sorted.length - 1);
    const best = ls.reduce((a, b) => a.unit <= b.unit ? a : b);
    for (const l of ls.slice().sort((a, b) => b.unit - a.unit)) {
      if (!l.shop.x) continue; const [x, y] = at(l.shop), t = q(l.unit);
      ctx.beginPath(); ctx.arc(x, y, l === best ? 7 : 4.5, 0, 7);
      ctx.fillStyle = l === best ? '#ffd166' : t < .34 ? '#3ecf8e' : t < .67 ? '#f5b84b' : '#ff6b6b'; ctx.fill();
      if (l === best) { ctx.lineWidth = 2; ctx.strokeStyle = '#fff'; ctx.stroke(); }
      marks.push({x, y, l});
    }
  }
}
cv.onmousemove = e => {
  const r = cv.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top;
  let hit = null, d = 81;
  for (const m of marks) { const k = (m.x - mx) ** 2 + (m.y - my) ** 2; if (k < d) { d = k; hit = m; } }
  if (!hit) { tip.style.display = 'none'; cv.style.cursor = 'default'; return; }
  tip.innerHTML = `<b>${esc(hit.l.shop.owner)}</b> · ${yang(hit.l.unit)} / tanesi<br><span class="muted">${tr.format(hit.l.count)} adet · ${yang(hit.l.price)}</span>`;
  tip.style.display = 'block'; tip.style.left = e.clientX + 14 + 'px'; tip.style.top = e.clientY + 10 + 'px'; cv.style.cursor = 'pointer';
  cv.onclick = () => showShop(hit.l.shop.id);
};
cv.onmouseleave = () => tip.style.display = 'none';
addEventListener('resize', draw);
render(); draw();
const firstWanted = document.querySelector('.card[data-name]');
if (firstWanted) select(firstWanted.dataset.name);
</script>
</body>
</html>
""";
}
