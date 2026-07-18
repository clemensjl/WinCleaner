using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinCleaner.Core;

/// <summary>Eingangsdaten für den HTML-Report (reine Daten, keine IO).</summary>
public sealed class HtmlReportData
{
    /// <summary>Analysierter Wurzelpfad (wird escaped angezeigt).</summary>
    public required string RootPath { get; init; }

    /// <summary>Zeitpunkt der Analyse.</summary>
    public required DateTime GeneratedAt { get; init; }

    /// <summary>Verzeichnisbaum mit rekursiven Größen.</summary>
    public required DiskTreeNode Tree { get; init; }

    /// <summary>Aufschlüsselung nach Dateiendung (Top-N inkl. Gesamtsumme).</summary>
    public required ExtensionAnalysis Extensions { get; init; }
}

/// <summary>
/// Erzeugt eine einzelne selbst-enthaltene HTML-Datei mit interaktiver Treemap
/// (Squarified-Layout in Vanilla-JS), Top-Verzeichnis-Tabelle und Endungs-
/// Aufschlüsselung. Keine externen Requests: CSS, JS und Daten (JSON) liegen
/// inline. Reine Funktion Daten -&gt; String, dadurch direkt testbar.
/// </summary>
public static class HtmlReportWriter
{
    /// <summary>Der Report ist immer deutsch formatiert – unabhängig von der Systemsprache
    /// (das eingebettete JS formatiert ebenfalls mit "de-DE").</summary>
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Baut den kompletten HTML-Report als String.</summary>
    public static string Build(HtmlReportData data)
    {
        long total = data.Tree.Bytes;

        var sections = new Dictionary<string, string>
        {
            ["HEADER"]    = BuildHeader(data, total),
            ["TABLE"]     = BuildTable(data.Tree, total),
            ["EXT"]       = BuildExtensions(data.Extensions),
            ["GENERATED"] = Esc(data.GeneratedAt.ToString("dd.MM.yyyy HH:mm", De)),
            ["JSON"]      = BuildJson(data)
        };

        // Ein einziger Durchlauf über das Template: eingesetzte Nutzdaten werden
        // nie erneut gescannt (ein Ordner namens "%%JSON%%" bleibt einfach Text).
        return Regex.Replace(Template, "%%(HEADER|TABLE|EXT|GENERATED|JSON)%%",
                             m => sections[m.Groups[1].Value]);
    }

    // ---- Abschnitte ----

    private static string BuildHeader(HtmlReportData data, long total)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<p class=\"kicker\">WinCleaner – Speicheranalyse</p>");
        sb.AppendLine($"<p class=\"hero\">{Esc(DiskAnalyzer.FormatSize(total, De))}</p>");
        sb.AppendLine("<dl class=\"meta\">");
        sb.AppendLine($"<div><dt>Pfad</dt><dd><code>{Esc(data.RootPath)}</code></dd></div>");
        sb.AppendLine($"<div><dt>Erstellt</dt><dd>{Esc(data.GeneratedAt.ToString("dd.MM.yyyy HH:mm", De))}</dd></div>");
        sb.AppendLine($"<div><dt>Dateien</dt><dd>{data.Tree.Files.ToString("N0", De)}</dd></div>");
        sb.AppendLine($"<div><dt>Gesamtgröße</dt><dd>{Esc(DiskAnalyzer.FormatSize(total, De))}</dd></div>");
        sb.AppendLine("</dl>");
        return sb.ToString();
    }

    private static string BuildTable(DiskTreeNode tree, long total)
    {
        if (tree.Children.Count == 0)
            return "<p class=\"hint\">Keine Einträge gefunden.</p>";

        var sb = new StringBuilder();
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th class=\"num\">#</th><th>Eintrag</th>" +
                      "<th class=\"num\">Größe</th><th class=\"num\">Dateien</th>" +
                      "<th class=\"num\">Anteil</th></tr></thead><tbody>");
        int rank = 0;
        foreach (var c in tree.Children.Take(15))
        {
            rank++;
            sb.AppendLine($"<tr><td class=\"num\">{rank}</td>" +
                          $"<td>{Esc(c.Name)}</td>" +
                          $"<td class=\"num\">{Esc(DiskAnalyzer.FormatSize(c.Bytes, De))}</td>" +
                          $"<td class=\"num\">{c.Files.ToString("N0", De)}</td>" +
                          $"<td class=\"num\">{Share(c.Bytes, total)}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
        return sb.ToString();
    }

    private static string BuildExtensions(ExtensionAnalysis ext)
    {
        if (ext.Entries.Count == 0)
            return "<p class=\"hint\">Keine Dateien gefunden.</p>";

        long max = ext.Entries.Max(e => e.Bytes);
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"bars\">");
        foreach (var e in ext.Entries)
        {
            // Balkenbreite relativ zur größten Endung; Anteil relativ zur Gesamtsumme.
            var width = (max > 0 ? e.Bytes * 100.0 / max : 0)
                .ToString("0.##", CultureInfo.InvariantCulture);
            sb.AppendLine($"<div class=\"bar-label\">{Esc(e.Extension)}</div>");
            sb.AppendLine($"<div class=\"bar-track\"><span class=\"bar-fill\" style=\"width:{width}%\"></span></div>");
            var files = e.Files == 1 ? "1 Datei" : $"{e.Files.ToString("N0", De)} Dateien";
            sb.AppendLine($"<div class=\"bar-value\">{Esc(DiskAnalyzer.FormatSize(e.Bytes, De))} · " +
                          $"{Share(e.Bytes, ext.TotalBytes)} · {files}</div>");
        }
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string BuildJson(HtmlReportData data)
    {
        var payload = new
        {
            rootPath = data.RootPath,
            generatedAt = data.GeneratedAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            totalBytes = data.Tree.Bytes,
            tree = ToDto(data.Tree),
            extensions = new
            {
                totalBytes = data.Extensions.TotalBytes,
                entries = data.Extensions.Entries
                    .Select(e => new { ext = e.Extension, bytes = e.Bytes, files = e.Files })
                    .ToArray()
            }
        };
        // Standard-Encoder escapet '<', '>', '&' als \uXXXX – damit kann kein
        // Pfad wie "</script>" aus dem Daten-Script-Tag ausbrechen.
        return JsonSerializer.Serialize(payload);
    }

    // Kein "path" pro Knoten: Pfade sind aus Wurzelpfad + Namen ableitbar (macht
    // das JS beim Vorbereiten) – spart bei großen Bäumen einen Großteil des JSON.
    private static object ToDto(DiskTreeNode n) => new
    {
        name = n.Name,
        dir = n.IsDir,
        bytes = n.Bytes,
        files = n.Files,
        children = n.Children.Select(ToDto).ToArray()
    };

    private static string Share(long bytes, long total)
        => total > 0 ? string.Format(De, "{0:N1} %", bytes * 100.0 / total) : "–";

    /// <summary>Maskiert HTML-Sonderzeichen (Pfade/Namen sind Fremddaten).</summary>
    internal static string Esc(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;");

    // ---- Template (selbst-enthalten: kein CDN, keine externen Requests) ----

    private const string Template = """
<!DOCTYPE html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>WinCleaner – Speicheranalyse</title>
<link rel="icon" href="data:,">
<style>
:root{
  color-scheme:light;
  --page:#f9f9f7; --surface:#fcfcfb;
  --ink:#0b0b0b; --ink-2:#52514e; --muted:#898781;
  --grid:#e1e0d9; --baseline:#c3c2b7;
  --border:rgba(11,11,11,.10);
  --accent:#2a78d6;
}
@media (prefers-color-scheme: dark){
  :root{
    color-scheme:dark;
    --page:#0d0d0d; --surface:#1a1a19;
    --ink:#ffffff; --ink-2:#c3c2b7; --muted:#898781;
    --grid:#2c2c2a; --baseline:#383835;
    --border:rgba(255,255,255,.10);
    --accent:#3987e5;
  }
}
*{box-sizing:border-box}
body{margin:0;background:var(--page);color:var(--ink);
  font:15px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
.wrap{max-width:1080px;margin:0 auto;padding:24px 20px 40px}
.card{background:var(--surface);border:1px solid var(--border);border-radius:10px;
  padding:20px 24px;margin-bottom:20px}
.kicker{margin:0;color:var(--ink-2);font-size:13px;letter-spacing:.4px;text-transform:uppercase}
.hero{margin:2px 0 12px;font-size:52px;font-weight:600;line-height:1.1}
.meta{display:flex;gap:12px 36px;flex-wrap:wrap;margin:0}
.meta dt{font-size:12px;color:var(--muted)}
.meta dd{margin:0;font-size:14px;color:var(--ink-2)}
.meta code{font-family:inherit;word-break:break-all}
h2{font-size:17px;margin:0 0 4px}
.hint{color:var(--muted);font-size:13px;margin:0 0 12px}
#breadcrumb{display:flex;flex-wrap:wrap;gap:2px;align-items:center;margin:0 0 10px;
  font-size:13px;min-height:24px}
#breadcrumb button{border:0;background:none;color:var(--accent);cursor:pointer;
  font:inherit;padding:2px 4px;border-radius:4px}
#breadcrumb button:hover{text-decoration:underline}
#breadcrumb button[aria-current]{color:var(--ink-2);font-weight:600;cursor:default;
  text-decoration:none}
#breadcrumb .sep{color:var(--muted);padding:0 2px}
#treemap{position:relative;height:460px}
.tile{position:absolute;border-radius:3px;overflow:hidden;font:inherit;text-align:left}
.tile[role="button"]{cursor:pointer}
.tile:hover,.tile:focus-visible{filter:brightness(1.07)}
.tile:focus-visible{outline:2px solid var(--ink);outline-offset:1px}
.tile .tl{display:block;padding:3px 6px 0;font-size:12px;font-weight:600;white-space:nowrap}
.tile .ts{display:block;padding:0 6px;font-size:11px;opacity:.85;white-space:nowrap}
#legend{display:flex;flex-wrap:wrap;gap:8px 18px;margin-top:12px;font-size:13px;color:var(--ink-2)}
#legend .sw{display:inline-block;width:12px;height:12px;border-radius:3px;margin-right:6px;
  vertical-align:-1px}
table{border-collapse:collapse;width:100%;font-size:14px}
th{color:var(--muted);font-weight:600;font-size:12px;text-align:left;
  border-bottom:1px solid var(--baseline);padding:6px 10px}
td{border-bottom:1px solid var(--grid);padding:7px 10px}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
.bars{display:grid;grid-template-columns:minmax(90px,auto) 1fr auto;gap:8px 14px;
  align-items:center;font-size:13px}
.bar-label{color:var(--ink-2);font-weight:600;font-variant-numeric:tabular-nums}
.bar-track{height:14px}
.bar-fill{display:block;height:14px;background:var(--accent);border-radius:0 4px 4px 0;
  min-width:2px}
.bar-value{color:var(--ink-2);font-variant-numeric:tabular-nums;white-space:nowrap}
#tooltip{position:fixed;z-index:10;background:var(--surface);border:1px solid var(--border);
  border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,.18);padding:8px 12px;max-width:360px;
  font-size:13px;pointer-events:none}
#tooltip .tt-val{font-size:16px;font-weight:600;color:var(--ink)}
#tooltip .tt-name{color:var(--ink-2)}
#tooltip .tt-path{color:var(--muted);font-size:11px;word-break:break-all}
footer{color:var(--muted);font-size:12px;text-align:center}
</style>
</head>
<body>
<div class="wrap">
<header class="card">
%%HEADER%%
</header>
<section class="card">
<h2>Treemap der Verzeichnisse</h2>
<p class="hint">Fläche = Speicherplatz. Klick auf einen Ordner zoomt hinein;
zurück über die Pfadleiste oder mit Escape. Details per Maus oder Tastatur (Tab).</p>
<nav id="breadcrumb" aria-label="Zoom-Pfad"></nav>
<div id="treemap" role="group" aria-label="Treemap der Verzeichnisse"></div>
<div id="legend" aria-label="Legende"></div>
<noscript><p class="hint">Für die interaktive Treemap wird JavaScript benötigt.
Die Tabellen unten enthalten dieselben Daten.</p></noscript>
</section>
<section class="card">
<h2>Top-Verzeichnisse</h2>
<p class="hint">Größte Einträge direkt unterhalb des analysierten Pfads.</p>
%%TABLE%%
</section>
<section class="card">
<h2>Aufschlüsselung nach Dateiendung</h2>
<p class="hint">Größenanteil je Endung über alle Dateien des analysierten Pfads.</p>
%%EXT%%
</section>
<footer>Erstellt am %%GENERATED%% mit WinCleaner (analyze-disk --html).</footer>
</div>
<div id="tooltip" hidden></div>
<script type="application/json" id="wc-data">%%JSON%%</script>
<script>
(function(){
"use strict";
var data = JSON.parse(document.getElementById("wc-data").textContent);

/* Farbrollen: kategorische Slots in fester Reihenfolge fuer die Top-Level-
   Eintraege (validierte Palette, hell + dunkel), Pseudo-/Restknoten neutral. */
var PAL = {
  light:{surface:"#fcfcfb", ink:"#0b0b0b",
    series:["#2a78d6","#008300","#e87ba4","#eda100","#1baf7a","#eb6834","#4a3aa7","#e34948"],
    other:"#898781"},
  dark:{surface:"#1a1a19", ink:"#0b0b0b",
    series:["#3987e5","#008300","#d55181","#c98500","#199e70","#d95926","#9085e9","#e66767"],
    other:"#898781"}
};
var mq = window.matchMedia("(prefers-color-scheme: dark)");
function pal(){ return mq.matches ? PAL.dark : PAL.light; }

/* Vorbereitung: Pfade aus Wurzelpfad + Namen ableiten (spart JSON-Gewicht) und
   Farb-Slots fest den Top-Level-Eintraegen zuordnen - der Slot folgt dem Eintrag,
   nie dem Rang in der aktuellen Ansicht. Pseudo-Knoten ("(Dateien)"/"(Weitere)")
   sind an dir=false erkennbar und bleiben neutral. */
(function prepare(){
  var slot = 0;
  data.tree._slot = -1; data.tree._depth = 0;
  data.tree.path = data.rootPath;
  data.tree.children.forEach(function(c){
    var s = (c.dir && slot < 8) ? slot++ : -1;
    walk(c, data.tree, s, 1);
  });
  function joinPath(parent, name){
    var sep = parent.charAt(parent.length - 1) === "\\" ? "" : "\\";
    return parent + sep + name;
  }
  function walk(n, parent, s, d){
    n._slot = s; n._depth = d;
    n.path = n.dir ? joinPath(parent.path, n.name) : parent.path;
    (n.children || []).forEach(function(k){ walk(k, n, s, d + 1); });
  }
})();

function hexToRgb(h){
  return [parseInt(h.slice(1,3),16), parseInt(h.slice(3,5),16), parseInt(h.slice(5,7),16)];
}
function mix(a, b, t){
  var A = hexToRgb(a), B = hexToRgb(b);
  var r = [0,1,2].map(function(i){ return Math.round(A[i]*t + B[i]*(1-t)); });
  return "rgb(" + r.join(",") + ")";
}
function relLum(rgbStr){
  var m = rgbStr.match(/\d+/g).map(Number).map(function(v){
    v /= 255; return v <= 0.03928 ? v/12.92 : Math.pow((v+0.055)/1.055, 2.4);
  });
  return 0.2126*m[0] + 0.7152*m[1] + 0.0722*m[2];
}
function tileColors(n, viewDepth){
  var p = pal();
  var base = n._slot >= 0 ? p.series[n._slot] : p.other;
  var rel = Math.max(1, n._depth - viewDepth);
  var t = Math.max(0.35, 0.82 - 0.18*(rel-1));
  var bg = mix(base, p.surface, t);
  var L = relLum(bg);
  var cWhite = 1.05/(L+0.05), cInk = (L+0.05)/0.055;
  return { bg: bg, fg: cWhite >= cInk ? "#ffffff" : p.ink };
}

function fmtSize(b){
  var u = ["B","KB","MB","GB","TB"], s = b, i = 0;
  while (s >= 1024 && i < u.length-1){ s /= 1024; i++; }
  return s.toLocaleString("de-DE",{minimumFractionDigits:1,maximumFractionDigits:1}) + " " + u[i];
}
function fmtPct(b, total){
  if (!(total > 0)) return "0,0 %";
  return (b*100/total).toLocaleString("de-DE",
    {minimumFractionDigits:1,maximumFractionDigits:1}) + " %";
}
function fmtInt(n){ return n.toLocaleString("de-DE"); }
function fmtFiles(n){ return n === 1 ? "1 Datei" : fmtInt(n) + " Dateien"; }

/* Squarified-Treemap (Bruls/Huizing/van Wijk): Reihen entlang der kurzen Seite,
   Kandidat kommt zur Reihe, solange das schlechteste Seitenverhaeltnis sinkt. */
function squarify(nodes, x, y, w, h){
  var out = [];
  if (w <= 0 || h <= 0) return out;
  var total = 0;
  nodes.forEach(function(n){ total += n.bytes; });
  if (!(total > 0)) return out;
  var items = nodes.map(function(n){ return { node:n, area:n.bytes/total*w*h }; });
  var rx = x, ry = y, rw = w, rh = h, i = 0;

  function worst(row, len){
    var s = 0, mx = 0, mn = Infinity;
    row.forEach(function(r){ s += r.area; mx = Math.max(mx, r.area); mn = Math.min(mn, r.area); });
    var s2 = s*s, l2 = len*len;
    return Math.max(l2*mx/s2, s2/(l2*mn));
  }
  while (i < items.length){
    var len = Math.min(rw, rh);
    var row = [items[i]];
    var cur = worst(row, len);
    var j = i + 1;
    while (j < items.length){
      var cand = row.concat(items[j]);
      var cw = worst(cand, len);
      if (cw <= cur){ row = cand; cur = cw; j++; } else break;
    }
    var s = 0;
    row.forEach(function(r){ s += r.area; });
    if (rw >= rh){
      var stripW = s / rh, yy = ry;
      row.forEach(function(r){
        var hh = r.area / stripW;
        out.push({ node:r.node, x:rx, y:yy, w:stripW, h:hh }); yy += hh;
      });
      rx += stripW; rw -= stripW;
    } else {
      var stripH = s / rw, xx = rx;
      row.forEach(function(r){
        var ww = r.area / stripH;
        out.push({ node:r.node, x:xx, y:ry, w:ww, h:stripH }); xx += ww;
      });
      ry += stripH; rh -= stripH;
    }
    i = j;
  }
  return out;
}

var stack = [data.tree];
var box = document.getElementById("treemap");
var crumbEl = document.getElementById("breadcrumb");
var legendEl = document.getElementById("legend");
var tip = document.getElementById("tooltip");
function current(){ return stack[stack.length-1]; }

function render(){ hideTip(); renderCrumb(); renderMap(); renderLegend(); }

function renderCrumb(){
  crumbEl.textContent = "";
  stack.forEach(function(n, i){
    if (i > 0){
      var sep = document.createElement("span");
      sep.className = "sep"; sep.textContent = "›";
      crumbEl.appendChild(sep);
    }
    var b = document.createElement("button");
    b.type = "button";
    b.textContent = n.name;
    if (i === stack.length-1) b.setAttribute("aria-current", "page");
    else b.addEventListener("click", function(){ stack.length = i + 1; render(); });
    crumbEl.appendChild(b);
  });
}

function canZoom(n){ return n.dir && n.children && n.children.length > 0; }
function zoomTo(path){
  path.forEach(function(n){ stack.push(n); });
  render();
}

function makeTile(r, viewDepth, zoomPath){
  var n = r.node;
  var el = document.createElement("div");
  el.className = "tile";
  var w = Math.max(0, r.w - 2), h = Math.max(0, r.h - 2);
  el.style.left = (r.x + 1) + "px";
  el.style.top = (r.y + 1) + "px";
  el.style.width = w + "px";
  el.style.height = h + "px";
  var c = tileColors(n, viewDepth);
  el.style.background = c.bg;
  el.style.color = c.fg;
  el.setAttribute("tabindex", "0");
  el.setAttribute("aria-label",
    n.name + ", " + fmtSize(n.bytes) + ", " + fmtPct(n.bytes, data.tree.bytes) +
    " der Gesamtgröße, " + fmtFiles(n.files));
  /* Klick zoomt zum tiefsten zoombaren Knoten des Pfads - ein Klick auf ein
     Blatt innerhalb eines Ordners zoomt also in den Ordner. */
  var effPath = zoomPath.slice();
  while (effPath.length && !canZoom(effPath[effPath.length-1])) effPath.pop();
  if (effPath.length){
    el.setAttribute("role", "button");
    el.addEventListener("click", function(ev){ ev.stopPropagation(); zoomTo(effPath); });
    el.addEventListener("keydown", function(ev){
      if (ev.key === "Enter" || ev.key === " "){ ev.preventDefault(); zoomTo(effPath); }
    });
  }
  /* Beschriftung nur, wenn sie bequem passt - nie abschneiden; sonst traegt
     sie der Tooltip (und die Tabelle unten haelt alles ohne Hover erreichbar). */
  if (h >= 24 && n.name.length * 7 + 12 <= w){
    var l = document.createElement("span");
    l.className = "tl"; l.textContent = n.name;
    el.appendChild(l);
    var sizeText = fmtSize(n.bytes);
    if (h >= 42 && sizeText.length * 6 + 12 <= w){
      var s = document.createElement("span");
      s.className = "ts"; s.textContent = sizeText;
      el.appendChild(s);
    }
  }
  el.addEventListener("pointermove", function(ev){
    ev.stopPropagation(); showTip(n, ev.clientX, ev.clientY);
  });
  el.addEventListener("pointerleave", hideTip);
  el.addEventListener("focus", function(){
    var b = el.getBoundingClientRect();
    showTip(n, b.left + 8, b.bottom + 2);
  });
  el.addEventListener("blur", hideTip);
  return el;
}

function renderMap(){
  box.textContent = "";
  var node = current();
  var W = box.clientWidth, H = box.clientHeight;
  var kids = (node.children || []).filter(function(c){ return c.bytes > 0; });
  if (!kids.length){
    var p = document.createElement("p");
    p.className = "hint";
    p.textContent = "Keine weiteren Unterordner mit Daten in dieser Ebene.";
    box.appendChild(p);
    return;
  }
  squarify(kids, 0, 0, W, H).forEach(function(r){
    if (r.w < 2 || r.h < 2) return;
    var tile = makeTile(r, node._depth, [r.node]);
    box.appendChild(tile);
    /* Eine Ebene tiefer andeuten, wenn genug Flaeche da ist. */
    var headH = 22;
    if (canZoom(r.node) && r.w >= 90 && r.h >= 64){
      var inner = (r.node.children || []).filter(function(c){ return c.bytes > 0; });
      squarify(inner, r.x + 4, r.y + headH, r.w - 8, r.h - headH - 4)
        .forEach(function(ir){
          if (ir.w < 8 || ir.h < 8) return;
          box.appendChild(makeTile(ir, node._depth, [r.node, ir.node]));
        });
    }
  });
}

function renderLegend(){
  legendEl.textContent = "";
  var p = pal();
  data.tree.children.forEach(function(c){
    var item = document.createElement("span");
    var sw = document.createElement("span");
    sw.className = "sw";
    var base = c._slot >= 0 ? p.series[c._slot] : p.other;
    sw.style.background = mix(base, p.surface, 0.82);
    item.appendChild(sw);
    item.appendChild(document.createTextNode(c.name + " · " + fmtSize(c.bytes)));
    legendEl.appendChild(item);
  });
}

/* Tooltip: Wert zuerst, Name/Pfad sekundaer; ausschliesslich textContent
   (Namen und Pfade sind Fremddaten). */
function showTip(n, x, y){
  tip.textContent = "";
  var v = document.createElement("div");
  v.className = "tt-val";
  v.textContent = fmtSize(n.bytes) + " · " + fmtPct(n.bytes, data.tree.bytes);
  var nm = document.createElement("div");
  nm.className = "tt-name";
  nm.textContent = n.name + " – " + fmtFiles(n.files);
  var pt = document.createElement("div");
  pt.className = "tt-path";
  pt.textContent = n.path;
  tip.appendChild(v); tip.appendChild(nm); tip.appendChild(pt);
  /* Vor dem Messen an den Ursprung setzen, sonst staucht die alte Position am
     rechten Rand die gemessene Breite. */
  tip.style.left = "0px"; tip.style.top = "0px";
  tip.hidden = false;
  var r = tip.getBoundingClientRect();
  tip.style.left = Math.max(4, Math.min(x + 14, window.innerWidth - r.width - 8)) + "px";
  tip.style.top = Math.max(4, Math.min(y + 14, window.innerHeight - r.height - 8)) + "px";
}
function hideTip(){ tip.hidden = true; }

document.addEventListener("keydown", function(ev){
  if (ev.key === "Escape" && stack.length > 1){ stack.pop(); render(); }
});
if (mq.addEventListener) mq.addEventListener("change", render);
else if (mq.addListener) mq.addListener(render);
var resizeTimer = null;
window.addEventListener("resize", function(){
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(render, 120);
});

render();
})();
</script>
</body>
</html>
""";
}
