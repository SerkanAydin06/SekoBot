using System.Text.Json;
using SekoBot.Memory;

namespace SekoBot.Bot;

/// <summary>
/// A map's obstacles as a page to look at (opens in the browser, works without internet): every
/// wall, house, rock and tree from above, coloured by kind, the places the bot ran into something
/// and where the character stood. Wheel to zoom, drag to move, hover for a name.
/// </summary>
internal static class MapReport
{
    /// <summary>What kind of obstacle a mesh is, from its name (for the colours and the counts).</summary>
    internal static string Kind(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("wall") || n.Contains("fence") || n.Contains("duvar") || n.Contains("cube") || n.Contains("door") || n.Contains("gate") || n.StartsWith("cit"))
            return "Duvar";
        if (n.Contains("house") || n.Contains("building") || n.Contains("hotel") || n.Contains("shop") || n.Contains("tower") || n.Contains("tent") ||
            n.Contains("cadir") || n.Contains("bridge") || n.Contains("kopru"))
            return "Yapı";
        if (n.Contains("rock") || n.Contains("kaya") || n.Contains("cliff") || n.Contains("stone") && !n.Contains("light"))
            return "Kaya";
        if (n.Contains("tree") || n.Contains("acacia") || n.Contains("baobab") || n.Contains("broadleaf") || n.Contains("pine") ||
            n.Contains("oak") || n.Contains("birch") || n.Contains("palm") || n.Contains("agac") || n.Contains("bush") || n.Contains("trunk"))
            return "Ağaç";
        return "Eşya";
    }

    /// <summary>"412 duvar, 300 kaya, 120 ağaç, …" for the log.</summary>
    internal static string Summary(IEnumerable<MapBlock> blocks) =>
        string.Join(", ", blocks.GroupBy(b => Kind(b.Name)).OrderByDescending(g => g.Count()).Select(g => $"{g.Count():N0} {g.Key.ToLowerInvariant()}"));

    public static string Html(MapTerrain terrain, IReadOnlyList<Vec3> learned, Vec3? me)
    {
        var data = new
        {
            map = terrain.Map,
            read = terrain.Read,
            blocks = terrain.Blocks.Select(b => new { n = b.Name, k = Kind(b.Name), p = b.Points, z = new[] { b.Bottom, b.Top }, r = b.Rough, w = b.Passable }),
            learned = learned.Select(l => new[] { Math.Round(l.X), Math.Round(l.Y) }),
            me = me is Vec3 m ? new[] { Math.Round(m.X), Math.Round(m.Y), Math.Round(m.Z) } : null,
        };
        return Template.Replace("__DATA__", JsonSerializer.Serialize(data).Replace("</", "<\\/"));
    }

    /// <summary>The outlines only (x0, y0, x1, y1, … each), for drawing the walls on other pages.</summary>
    public static string Outlines(MapTerrain? terrain) =>
        terrain == null ? "[]" : JsonSerializer.Serialize(terrain.Blocks.Where(b => !b.Passable).Select(b => b.Points.Select(v => MathF.Round(v))));

    private const string Template = """
<!doctype html>
<html lang="tr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Harita Engelleri</title>
<style>
:root { --bg: #0f1117; --panel: #171a23; --line: #2a2f3f; --text: #e8eaf0; --muted: #8b91a5; --accent: #5b8cff; }
* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; background: var(--bg); color: var(--text); font: 14px/1.45 "Segoe UI", system-ui, sans-serif; overflow: hidden; }
header { padding: 14px 20px 8px; display: flex; flex-wrap: wrap; align-items: baseline; gap: 8px 18px; }
header h1 { margin: 0; font-size: 20px; }
.muted { color: var(--muted); }
.legend { display: flex; flex-wrap: wrap; gap: 8px; padding: 0 20px 10px; }
.chip { display: flex; align-items: center; gap: 7px; background: var(--panel); border: 1px solid var(--line); border-radius: 999px; padding: 4px 12px; cursor: pointer; user-select: none; font-size: 13px; }
.chip.off { opacity: .35; }
.chip i { width: 12px; height: 12px; border-radius: 3px; display: inline-block; }
#wrap { position: absolute; left: 20px; right: 20px; top: 104px; bottom: 16px; border: 1px solid var(--line); border-radius: 12px; overflow: hidden;
  background: radial-gradient(circle at 50% 40%, #182035, #11141c); }
canvas { width: 100%; height: 100%; display: block; cursor: grab; }
canvas.drag { cursor: grabbing; }
#tip { position: fixed; pointer-events: none; background: #0b0d12ee; border: 1px solid var(--line); border-radius: 8px; padding: 6px 9px; font-size: 12px; display: none; }
.help { position: absolute; right: 12px; bottom: 10px; font-size: 12px; color: var(--muted); background: #0b0d12aa; padding: 4px 8px; border-radius: 6px; }
</style>
</head>
<body>
<header><h1 id="title"></h1><span class="muted" id="sub"></span></header>
<div class="legend" id="legend"></div>
<div id="wrap"><canvas id="cv"></canvas><div class="help">Tekerlek: yakınlaş · Sürükle: kaydır · Çift tık: sığdır</div></div>
<div id="tip"></div>
<script>
const D = __DATA__;
const colors = {"Duvar": "#9aa3b8", "Yapı": "#c792ea", "Kaya": "#c8a27a", "Ağaç": "#3ecf8e", "Eşya": "#5b8cff", "Geçilebilir": "#3a4257", "Takıldığı yer": "#ff6b6b"};
const tr = new Intl.NumberFormat('tr-TR'), esc = s => String(s).replace(/[&<>"]/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;'}[c]));
document.getElementById('title').textContent = 'Harita: ' + D.map;
document.getElementById('sub').textContent = `${tr.format(D.blocks.filter(b => !b.w).length)} engel · oyunun belleğinden okundu ${new Date(D.read).toLocaleString('tr-TR')}`;
const counts = {};
for (const b of D.blocks) { const k = b.w ? 'Geçilebilir' : b.k; counts[k] = (counts[k] || 0) + 1; }
if (D.learned.length) counts['Takıldığı yer'] = D.learned.length;
const off = new Set();
const legend = document.getElementById('legend');
legend.innerHTML = Object.keys(colors).filter(k => counts[k]).map(k => `<span class="chip" data-k="${k}"><i style="background:${colors[k]}"></i>${k} <b>${tr.format(counts[k])}</b></span>`).join('')
  + (D.me ? `<span class="chip"><i style="background:#ffd166;border-radius:50%"></i>Karakter</span>` : '');
legend.onclick = e => { const c = e.target.closest('.chip[data-k]'); if (!c) return; const k = c.dataset.k; off.has(k) ? off.delete(k) : off.add(k); c.classList.toggle('off'); draw(); };
const cv = document.getElementById('cv'), ctx = cv.getContext('2d'), tip = document.getElementById('tip');
let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
for (const b of D.blocks) for (let i = 0; i < b.p.length; i += 2) { minX = Math.min(minX, b.p[i]); maxX = Math.max(maxX, b.p[i]); minY = Math.min(minY, b.p[i + 1]); maxY = Math.max(maxY, b.p[i + 1]); }
if (D.me) { minX = Math.min(minX, D.me[0]); maxX = Math.max(maxX, D.me[0]); minY = Math.min(minY, D.me[1]); maxY = Math.max(maxY, D.me[1]); }
if (!isFinite(minX)) { minX = 0; minY = 0; maxX = 1000; maxY = 1000; }
let scale = 1, ox = 0, oy = 0, fitted = false;
function fit() {
  const r = cv.getBoundingClientRect();
  scale = Math.min((r.width - 40) / Math.max(1, maxX - minX), (r.height - 40) / Math.max(1, maxY - minY));
  ox = (r.width - (maxX - minX) * scale) / 2 - minX * scale; oy = (r.height - (maxY - minY) * scale) / 2 - minY * scale;
}
const sx = x => ox + x * scale, sy = y => oy + y * scale;
function draw() {
  const r = cv.getBoundingClientRect(), dpr = devicePixelRatio || 1;
  if (!fitted && r.width > 0) { fitted = true; fit(); }
  cv.width = r.width * dpr; cv.height = r.height * dpr; ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, r.width, r.height);
  // A grid every 10 m.
  const step = 1000 * scale;
  if (step > 14) {
    ctx.strokeStyle = '#ffffff0c'; ctx.lineWidth = 1; ctx.beginPath();
    for (let x = Math.floor(-ox / step) * step + ox % step; x < r.width; x += step) { ctx.moveTo(x, 0); ctx.lineTo(x, r.height); }
    for (let y = Math.floor(-oy / step) * step + oy % step; y < r.height; y += step) { ctx.moveTo(0, y); ctx.lineTo(r.width, y); }
    ctx.stroke();
  }
  for (const b of D.blocks) {
    const k = b.w ? 'Geçilebilir' : b.k;
    if (off.has(k)) continue;
    ctx.beginPath();
    for (let i = 0; i < b.p.length; i += 2) (i ? ctx.lineTo : ctx.moveTo).call(ctx, sx(b.p[i]), sy(b.p[i + 1]));
    ctx.closePath();
    ctx.fillStyle = colors[k] + (b.r ? '55' : 'bb'); ctx.fill();
    if (b.r) { ctx.setLineDash([3, 3]); ctx.strokeStyle = colors[k]; ctx.lineWidth = 1; ctx.stroke(); ctx.setLineDash([]); }
  }
  if (!off.has('Takıldığı yer')) {
    ctx.fillStyle = colors['Takıldığı yer'];
    for (const l of D.learned) { ctx.beginPath(); ctx.arc(sx(l[0]), sy(l[1]), Math.max(2.5, 60 * scale), 0, 7); ctx.fill(); }
  }
  if (D.me) {
    ctx.beginPath(); ctx.arc(sx(D.me[0]), sy(D.me[1]), 6, 0, 7); ctx.fillStyle = '#ffd166'; ctx.fill(); ctx.lineWidth = 2; ctx.strokeStyle = '#fff'; ctx.stroke();
  }
}
function inside(b, x, y) {
  let c = false;
  for (let i = 0, j = b.p.length - 2; i < b.p.length; j = i, i += 2) {
    const xi = b.p[i], yi = b.p[i + 1], xj = b.p[j], yj = b.p[j + 1];
    if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
  }
  return c;
}
let dragging = null;
cv.onmousedown = e => { dragging = [e.clientX, e.clientY]; cv.classList.add('drag'); };
addEventListener('mouseup', () => { dragging = null; cv.classList.remove('drag'); });
cv.onmousemove = e => {
  if (dragging) { ox += e.clientX - dragging[0]; oy += e.clientY - dragging[1]; dragging = [e.clientX, e.clientY]; draw(); tip.style.display = 'none'; return; }
  const r = cv.getBoundingClientRect(), wx = (e.clientX - r.left - ox) / scale, wy = (e.clientY - r.top - oy) / scale;
  const hit = D.blocks.find(b => !off.has(b.w ? 'Geçilebilir' : b.k) && inside(b, wx, wy));
  if (!hit) { tip.style.display = 'none'; return; }
  tip.innerHTML = `<b>${esc(hit.w ? 'Geçilebilir' : hit.k)}</b> · ${esc(hit.n)}<br><span class="muted">yükseklik ${tr.format(Math.round(hit.z[0]))} – ${tr.format(Math.round(hit.z[1]))}${hit.r ? ' · yaklaşık (kutu)' : ''}${hit.w ? ' · karakter içinden geçti' : ''}</span>`;
  tip.style.display = 'block'; tip.style.left = e.clientX + 14 + 'px'; tip.style.top = e.clientY + 10 + 'px';
};
cv.onmouseleave = () => tip.style.display = 'none';
cv.onwheel = e => {
  e.preventDefault();
  const r = cv.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top, f = Math.exp(-e.deltaY * 0.0015);
  ox = mx - (mx - ox) * f; oy = my - (my - oy) * f; scale *= f; draw();
};
cv.ondblclick = () => { fit(); draw(); };
addEventListener('resize', draw);
draw();
</script>
</body>
</html>
""";
}
