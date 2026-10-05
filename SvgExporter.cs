using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using MetroPlanner.Models;

namespace MetroPlanner
{
    /// <summary>矢量导出：真实线路图 SVG、示意图 SVG、GeoJSON。全貌导出 + 比例尺 + 防重叠文字 + 分语言字体。</summary>
    public static class SvgExporter
    {
        static readonly Dictionary<string, (string label, string dash)> SEG =
            new Dictionary<string, (string, string)>
            {
                ["underground"] = ("地下段", ""),
                ["transition"] = ("切换段", "2 9"),
                ["ground"] = ("地上段", "16 10")
            };

        static string Esc(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        static string ToSvgPath(List<PathCmd> cmds)
        {
            var sb = new StringBuilder();
            sb.Append($"M {cmds[0].Point.X:F2} {cmds[0].Point.Y:F2}");
            foreach (var c in cmds.Skip(1))
            {
                if (c.IsArc)
                    sb.Append($" A {c.Radius:F2} {c.Radius:F2} 0 0 {(c.Sweep ? 1 : 0)} {c.Point.X:F2} {c.Point.Y:F2}");
                else
                    sb.Append($" L {c.Point.X:F2} {c.Point.Y:F2}");
            }
            return sb.ToString();
        }

        static string PolylineSvg(List<Point> pts)
        {
            var sb = new StringBuilder();
            sb.Append($"M {pts[0].X:F2} {pts[0].Y:F2}");
            foreach (var p in pts.Skip(1)) sb.Append($" L {p.X:F2} {p.Y:F2}");
            return sb.ToString();
        }

        // 获取某条线路的某语言字体（站点级覆盖优先）
        static string FontFor(Station s, Line line, int lang)
        {
            string sf = lang switch { 0 => s.CnFont, 1 => s.EnFont, 2 => s.ThirdFont, _ => "" };
            if (!string.IsNullOrEmpty(sf)) return sf;
            string f = lang switch { 0 => line.CnFont, 1 => line.EnFont, 2 => line.ThirdFont, _ => "" };
            if (string.IsNullOrEmpty(f)) f = line.LabelFont;
            return f;
        }

        // 计算自动字号：根据线路数量与站点密度估算，避免文字重叠（返回缩放倍数，1.0 为基准）
        static double AutoLabelScale(Dictionary<string, Station> stations, Dictionary<string, Point> projXY)
        {
            // 只考虑 projXY 中确实存在的站点，避免 KeyNotFound
            var pts = projXY.Values.ToList();
            if (pts.Count < 2) return 1.0;
            double minDist = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    double d = Math.Sqrt(Math.Pow(pts[i].X - pts[j].X, 2) + Math.Pow(pts[i].Y - pts[j].Y, 2));
                    if (d > 1) minDist = Math.Min(minDist, d);
                }
            if (minDist >= double.MaxValue) return 1.0;
            // 间距越小，字号越小（目标：最小间距约 60px 时字号 1.0）
            double scale = Math.Max(0.55, Math.Min(1.6, minDist / 60.0));
            return scale;
        }

        // 估算文字渲染宽度（中文≈字号，英文≈0.62×字号）
        static double EstimateTextWidth(string s, double fontSize)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double w = 0;
            foreach (var ch in s)
            {
                if (ch > 127) w += fontSize;       // 全角/中文
                else w += fontSize * 0.62;          // 半角/英文
            }
            return w;
        }

        // 计算线路在站点处的近似切向（用于决定标签避让线路的方向）
        static Point? LineTangent(Station s, Dictionary<string, Line> lines, Dictionary<string, Point> pos)
        {
            Point? prev = null, next = null;
            foreach (var lid in s.Lines)
            {
                if (!lines.TryGetValue(lid, out var line)) continue;
                int idx = line.StationOrder.IndexOf(s.Id);
                if (idx >= 0)
                {
                    if (idx > 0 && pos.TryGetValue(line.StationOrder[idx - 1], out var pp)) prev = pp;
                    if (idx + 1 < line.StationOrder.Count && pos.TryGetValue(line.StationOrder[idx + 1], out var pn)) next = pn;
                    if (prev != null || next != null) break;
                }
                foreach (var br in line.Branches)
                {
                    var bo = new List<string> { br.Junction };
                    bo.AddRange(br.StationOrder);
                    int bidx = bo.IndexOf(s.Id);
                    if (bidx < 0) continue;
                    if (bidx > 0 && pos.TryGetValue(bo[bidx - 1], out var bp)) prev = bp;
                    if (bidx + 1 < bo.Count && pos.TryGetValue(bo[bidx + 1], out var bn)) next = bn;
                    if (prev != null || next != null) goto done;
                }
            }
        done:
            if (prev == null && next == null) return null;
            if (!pos.TryGetValue(s.Id, out var p)) return null;
            if (prev != null && next != null) return new Point(next.Value.X - prev.Value.X, next.Value.Y - prev.Value.Y);
            if (prev != null) return new Point(p.X - prev.Value.X, p.Y - prev.Value.Y);
            return new Point(next.Value.X - p.X, next.Value.Y - p.Y);
        }

        // 生成所有站点的标签（方向避让 + 防遮挡），返回 SVG 字符串
        static string BuildLabelsSvg(Dictionary<string, Station> stations, Dictionary<string, Line> lines,
            Dictionary<string, Point> pos, bool showCn, bool showEn, bool showThird,
            double scale, double fontScale, double canvasW)
        {
            var sb = new StringBuilder();
            var placed = new List<(double x, double y, double w, double h)>();
            foreach (var s in stations.Values)
            {
                if (!pos.TryGetValue(s.Id, out var p)) continue;
                Line styleLine = null;
                foreach (var lid in s.Lines) if (lines.TryGetValue(lid, out var l)) { styleLine = l; break; }

                var items = new List<(string text, string font)>();
                if (showCn && !string.IsNullOrEmpty(s.NameCn)) items.Add((s.NameCn, FontFor(s, styleLine, 0)));
                if (showEn && !string.IsNullOrEmpty(s.NameEn)) items.Add((s.NameEn, FontFor(s, styleLine, 1)));
                if (showThird && !string.IsNullOrEmpty(s.NameThird)) items.Add((s.NameThird, FontFor(s, styleLine, 2)));
                if (items.Count == 0) continue;

                double baseSize = s.FontSize > 0 ? s.FontSize : (styleLine != null && styleLine.FontSize > 0 ? styleLine.FontSize : 12);
                double fs = baseSize * scale * fontScale, lineH = (baseSize + 2) * scale * fontScale;
                double gap = 12 * scale;

                // 宽度估算
                double estW = 0;
                foreach (var (text, _) in items) estW = Math.Max(estW, EstimateTextWidth(text, fs));
                double totalH = items.Count * lineH;

                // 方向避让
                var tan = LineTangent(s, lines, pos);
                bool horizontal = tan.HasValue && Math.Abs(tan.Value.X) > Math.Abs(tan.Value.Y) * 1.4;

                // 计算「默认侧」与「另一侧」两种候选位置（固定，不再移动）
                double left, top; bool flip;
                (double x, double y) primary, alt;
                bool primaryFlip;
                if (horizontal)
                {
                    primaryFlip = false;
                    primary = (p.X - estW / 2, p.Y - totalH - gap);
                    alt = (p.X - estW / 2, p.Y + gap);
                }
                else
                {
                    primaryFlip = p.X + gap + estW > canvasW - 6;
                    if (primaryFlip)
                    {
                        primary = (p.X - gap - estW, p.Y - totalH / 2);
                        alt = (p.X + gap, p.Y - totalH / 2);
                    }
                    else
                    {
                        primary = (p.X + gap, p.Y - totalH / 2);
                        alt = (p.X - gap - estW, p.Y - totalH / 2);
                    }
                }

                // 默认侧被遮挡且另一侧空闲 → 翻转到另一侧；否则保持默认侧（固定，不移动）
                var chosen = primary; flip = primaryFlip;
                if (Overlaps(placed, primary.x, primary.y, estW, totalH) && !Overlaps(placed, alt.x, alt.y, estW, totalH))
                { chosen = alt; flip = !primaryFlip; }

                left = Math.Max(4, Math.Min(chosen.x, canvasW - 4 - estW));
                top = Math.Max(2, chosen.y);
                placed.Add((left, top, estW, totalH));

                // 颜色与描边
                string color = !string.IsNullOrEmpty(s.FontColor) ? s.FontColor : "#1f2733";
                bool bold = s.FontBold || (styleLine != null && styleLine.FontBold);
                bool outline = s.FontOutline && (styleLine == null || styleLine.FontOutline);
                string weight = bold ? "700" : "600";
                string anchor = horizontal ? "middle" : (flip ? "end" : "start");
                double ax = horizontal ? p.X : left;

                if (outline)
                    sb.Append($"<text font-size=\"{fs:F1}\" fill=\"{color}\" stroke=\"#ffffff\" stroke-width=\"{3.2 * scale * fontScale:F1}\" paint-order=\"stroke\" text-anchor=\"{anchor}\" style=\"font-weight:{weight}\">");
                else
                    sb.Append($"<text font-size=\"{fs:F1}\" fill=\"{color}\" text-anchor=\"{anchor}\" style=\"font-weight:{weight}\">");
                for (int i = 0; i < items.Count; i++)
                {
                    string fontAttr = string.IsNullOrEmpty(items[i].font) ? "" : $" font-family=\"{Esc(items[i].font)}\"";
                    sb.Append($"<tspan x=\"{ax:F2}\" y=\"{top + i * lineH:F2}\"{fontAttr}>{Esc(items[i].text)}</tspan>");
                }
                sb.Append("</text>");
            }
            return sb.ToString();
        }

        static bool Overlaps(List<(double x, double y, double w, double h)> placed, double x, double y, double w, double h)
        {
            foreach (var r in placed)
                if (x < r.x + r.w && x + w > r.x && y < r.y + r.h && y + h > r.y) return true;
            return false;
        }

        // 比例尺（基于投影的米/像素换算）
        static string ScaleBarSvg(double pxPerMeter, double maxX, double maxY)
        {
            // 选择合适长度（1/2/5 × 10^n 米）
            double targetM = 200 / Math.Max(pxPerMeter, 1e-9);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(targetM)));
            double norm = targetM / mag;
            double nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
            double lenM = nice * mag;
            double lenPx = lenM * pxPerMeter;
            double x = 24, y = maxY - 26;
            string label = lenM >= 1000 ? $"{lenM / 1000:0.#} km" : $"{lenM:0} m";
            var sb = new StringBuilder();
            sb.Append($"<g stroke=\"#1f2733\" stroke-width=\"1.5\">");
            sb.Append($"<line x1=\"{x}\" y1=\"{y}\" x2=\"{x + lenPx}\" y2=\"{y}\"/>");
            sb.Append($"<line x1=\"{x}\" y1=\"{y - 4}\" x2=\"{x}\" y2=\"{y + 4}\"/>");
            sb.Append($"<line x1=\"{x + lenPx}\" y1=\"{y - 4}\" x2=\"{x + lenPx}\" y2=\"{y + 4}\"/>");
            sb.Append($"</g>");
            sb.Append($"<text x=\"{x + lenPx / 2}\" y=\"{y - 8}\" font-size=\"13\" fill=\"#1f2733\" text-anchor=\"middle\" font-weight=\"700\">{Esc(label)}</text>");
            return sb.ToString();
        }

        static (double x, double y) Merc(double lng, double lat)
        {
            double x = (lng + 180) / 360.0;
            double s = Math.Sin(lat * Math.PI / 180);
            double y = 0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI);
            return (x, y);
        }

        public static string BuildMapSvg(Dictionary<string, Station> stations, Dictionary<string, Line> lines,
            bool showCn, bool showEn, bool showThird, double curveRadiusM, double fontScale = 1.0, double lineScale = 1.0)
        {
            if (stations.Count == 0) return null;
            var ids = stations.Keys.ToList();
            var proj = ids.ToDictionary(id => id, id => Merc(stations[id].Lng, stations[id].Lat));
            double minX = proj.Values.Min(p => p.x), maxX = proj.Values.Max(p => p.x);
            double minY = proj.Values.Min(p => p.y), maxY = proj.Values.Max(p => p.y);
            double pad = 0.0003;
            double bw = Math.Max(maxX - minX, 1e-6) + pad * 2, bh = Math.Max(maxY - minY, 1e-6) + pad * 2;
            double W = 1200, labelW = 170;
            double S = (W - labelW) / bw;
            double H = Math.Max(200, Math.Round(bh * S));
            Point ToXY((double x, double y) p) => new Point((p.x - minX + pad) * S, (maxY - p.y + pad) * S);

            double midLat = stations.Values.Average(s => s.Lat);
            double pxPerDeg = S / 360.0;
            double radiusPx = curveRadiusM / (111320 * Math.Cos(midLat * Math.PI / 180)) * pxPerDeg;
            double pxPerMeter = pxPerDeg / (111320 * Math.Cos(midLat * Math.PI / 180));

            // 自动字号缩放（fontScale<0 表示自动；>=0 表示手动指定，不再叠加自动值）
            var xyDict = ids.ToDictionary(id => id, id => ToXY(proj[id]));
            double autoScale = fontScale < 0 ? AutoLabelScale(stations, xyDict) : 1.0;
            double effFont = fontScale < 0 ? 1.0 : fontScale;

            var body = new StringBuilder();
            Func<GeoPoint, Point> anchorProj = g => ToXY(Merc(g.Lng, g.Lat));
            foreach (var line in lines.Values)
            {
                var color = string.IsNullOrEmpty(line.LineColor) ? line.Color : line.LineColor;
                double lw = 6 * lineScale * (line.LineWidth > 0 ? line.LineWidth / 5.0 : 1.0);
                var trunk = line.StationOrder.Where(id => proj.ContainsKey(id)).Select(id => ToXY(proj[id])).ToList();
                AppendLineBodyEx(body, trunk, line.StationOrder, line.Segments, color, radiusPx, lw, anchorProj);
                foreach (var br in line.Branches)
                {
                    var order = new List<string> { br.Junction };
                    order.AddRange(br.StationOrder);
                    var bpts = order.Where(id => proj.ContainsKey(id)).Select(id => ToXY(proj[id])).ToList();
                    AppendLineBodyEx(body, bpts, order, br.Segments, color, radiusPx, lw, anchorProj);
                }
            }

            foreach (var id in ids)
            {
                var s = stations[id]; var p = ToXY(proj[id]); bool tr = s.IsTransfer;
                body.Append(tr
                    ? $"<circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"{13 * effFont:F2}\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"{3 * effFont:F2}\"/><circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"{5 * effFont:F2}\" fill=\"#1f2733\"/>"
                    : $"<circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"{9 * effFont:F2}\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"{2.5 * effFont:F2}\"/>");
            }
            body.Append(BuildLabelsSvg(stations, lines, xyDict, showCn, showEn, showThird, 1.5 * effFont * autoScale, 1.0, W - labelW));

            body.Append(ScaleBarSvg(pxPerMeter, W - labelW, H));

            double ly = 20, lh = 30;
            var legend = new StringBuilder();
            legend.Append($"<rect x=\"20\" y=\"{ly}\" width=\"330\" height=\"{lines.Count * lh + 70}\" fill=\"#fff\" fill-opacity=\"0.95\" stroke=\"#e2e6ec\" rx=\"8\"/>");
            ly += 28;
            legend.Append($"<text x=\"36\" y=\"{ly}\" font-size=\"16\" font-weight=\"700\" fill=\"#1f2733\">图例 Legend</text>"); ly += lh;
            legend.Append($"<circle cx=\"40\" cy=\"{ly - 5}\" r=\"8\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"2\"/><text x=\"60\" y=\"{ly}\" font-size=\"14\">普通站</text>"); ly += lh;
            legend.Append($"<circle cx=\"40\" cy=\"{ly - 5}\" r=\"11\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"2.5\"/><circle cx=\"40\" cy=\"{ly - 5}\" r=\"4\" fill=\"#1f2733\"/><text x=\"60\" y=\"{ly}\" font-size=\"14\">换乘站</text>"); ly += lh;
            foreach (var line in lines.Values)
            {
                legend.Append($"<rect x=\"28\" y=\"{ly - 5}\" width=\"26\" height=\"5\" fill=\"{line.Color}\" rx=\"2\"/><text x=\"60\" y=\"{ly}\" font-size=\"14\">{Esc(line.Name)}</text>"); ly += lh;
            }
            legend.Append($"<text x=\"36\" y=\"{ly}\" font-size=\"12\" fill=\"#6b7684\">实线=地下段 · 虚线=地上段 · 点线=切换段</text>");
            legend.Append($"<g transform=\"translate({W - 64},44)\"><circle cx=\"0\" cy=\"0\" r=\"24\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"1.5\"/><polygon points=\"0,-15 6,6 0,1 -6,6\" fill=\"#e4002b\"/><text x=\"0\" y=\"28\" text-anchor=\"middle\" font-size=\"14\" font-weight=\"700\" fill=\"#1f2733\">北</text></g>");

            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\">" +
                   $"<rect width=\"{W}\" height=\"{H}\" fill=\"#ffffff\"/>{body}{legend}</svg>";
        }

        public static string BuildSchematicSvg(Dictionary<string, Station> stations, Dictionary<string, Line> lines,
            Dictionary<string, Point> pos, bool showCn, bool showEn, bool showThird, double fontScale = 1.0)
        {
            if (stations.Count == 0) return null;
            var pts = pos.Values.ToList();
            if (pts.Count == 0) return null;
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double pad = 40;
            double bw = Math.Max(maxX - minX, 1) + pad * 2, bh = Math.Max(maxY - minY, 1) + pad * 2;
            double W = 1400, labelW = 180;
            double S = (W - labelW) / bw;
            double H = Math.Max(200, Math.Round(bh * S));
            Point ToXY(Point p) => new Point((p.X - minX + pad) * S, (p.Y - minY + pad) * S);

            var xyDict = stations.Values.Where(s => pos.ContainsKey(s.Id)).ToDictionary(s => s.Id, s => ToXY(pos[s.Id]));
            double autoScale = fontScale < 0 ? AutoLabelScale(stations, xyDict) : 1.0;
            double effFont = fontScale < 0 ? 1.0 : fontScale;

            var body = new StringBuilder();
            foreach (var line in lines.Values)
            {
                var color = string.IsNullOrEmpty(line.LineColor) ? line.Color : line.LineColor;
                var list = line.StationOrder.Where(id => pos.ContainsKey(id)).Select(id => ToXY(pos[id])).ToList();
                if (list.Count >= 2)
                    body.Append($"<path d=\"{PolylineSvg(PathBuilder.Octilinear(list, 18))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"7\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
                foreach (var br in line.Branches)
                {
                    var order = new List<string> { br.Junction };
                    order.AddRange(br.StationOrder);
                    var bpts = order.Where(id => pos.ContainsKey(id)).Select(id => ToXY(pos[id])).ToList();
                    if (bpts.Count >= 2)
                        body.Append($"<path d=\"{PolylineSvg(PathBuilder.Octilinear(bpts, 18))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"7\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
                }
            }
            foreach (var s in stations.Values)
            {
                if (!pos.ContainsKey(s.Id)) continue;
                var p = ToXY(pos[s.Id]); bool tr = s.IsTransfer;
                body.Append(tr
                    ? $"<circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"12\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"3\"/><circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"5\" fill=\"#1f2733\"/>"
                    : $"<circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"8\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"2.5\"/>");
            }
            body.Append(BuildLabelsSvg(stations, lines, xyDict, showCn, showEn, showThird, 1.4 * effFont * autoScale, 1.0, W - labelW));

            double ly = 20, lh = 28;
            var legend = new StringBuilder();
            legend.Append($"<rect x=\"20\" y=\"{ly}\" width=\"300\" height=\"{lines.Count * lh + 30}\" fill=\"#fff\" stroke=\"#e2e6ec\" rx=\"8\"/>");
            ly += 26;
            foreach (var line in lines.Values)
            {
                legend.Append($"<rect x=\"36\" y=\"{ly - 4}\" width=\"24\" height=\"5\" fill=\"{line.Color}\" rx=\"2\"/><text x=\"66\" y=\"{ly}\" font-size=\"14\">{Esc(line.Name)}</text>"); ly += lh;
            }
            legend.Append($"<g transform=\"translate({W - 64},44)\"><circle cx=\"0\" cy=\"0\" r=\"24\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"1.5\"/><polygon points=\"0,-15 6,6 0,1 -6,6\" fill=\"#e4002b\"/><text x=\"0\" y=\"28\" text-anchor=\"middle\" font-size=\"14\" font-weight=\"700\" fill=\"#1f2733\">北</text></g>");

            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\">" +
                   $"<rect width=\"{W}\" height=\"{H}\" fill=\"#ffffff\"/>{body}{legend}</svg>";
        }

        public static string BuildGeoJson(Dictionary<string, Station> stations, Dictionary<string, Line> lines)
        {
            var feats = new List<string>();
            foreach (var line in lines.Values)
            {
                AddGeoLine(line.StationOrder, line.Name, line.Color);
                foreach (var br in line.Branches)
                {
                    var order = new List<string> { br.Junction };
                    order.AddRange(br.StationOrder);
                    AddGeoLine(order, line.Name + "支线", line.Color);
                }
            }
            void AddGeoLine(List<string> order, string name, string color)
            {
                var coords = order.Where(id => stations.ContainsKey(id))
                    .Select(id => $"[{stations[id].Lng.ToString("R")},{stations[id].Lat.ToString("R")}]").ToList();
                if (coords.Count >= 2)
                    feats.Add($"{{\"type\":\"Feature\",\"properties\":{{\"name\":\"{Esc(name)}\",\"color\":\"{color}\",\"type\":\"line\"}},\"geometry\":{{\"type\":\"LineString\",\"coordinates\":[{string.Join(",", coords)}]}}}}");
            }
            foreach (var s in stations.Values)
            {
                feats.Add($"{{\"type\":\"Feature\",\"properties\":{{\"name\":\"{Esc(s.NameCn)}\",\"nameEn\":\"{Esc(s.NameEn)}\",\"transfer\":{(s.IsTransfer ? "true" : "false")},\"lines\":[{string.Join(",", s.Lines.Select(Esc).Select(x => $"\"{x}\""))}],\"type\":\"station\"}},\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{s.Lng.ToString("R")},{s.Lat.ToString("R")}]}}}}");
            }
            return $"{{\"type\":\"FeatureCollection\",\"features\":[{string.Join(",", feats)}]}}";
        }

        public static string BuildMapSvgWithTiles(Dictionary<string, Station> stations, Dictionary<string, Line> lines,
            TileService tiles, double west, double south, double east, double north, int zoom,
            bool showCn, bool showEn, bool showThird, double curveRadiusM)
        {
            return BuildMapSvgWithTilesEx(stations, lines, tiles, west, south, east, north, zoom,
                showCn, showEn, showThird, curveRadiusM, 1.0, 1.0);
        }

        public static string BuildMapSvgWithTilesEx(Dictionary<string, Station> stations, Dictionary<string, Line> lines,
            TileService tiles, double west, double south, double east, double north, int zoom,
            bool showCn, bool showEn, bool showThird, double curveRadiusM, double fontScale, double lineScale)
        {
            if (stations.Count == 0) return null;
            double x0 = MapMath.LngToWorldX(west, zoom), y0 = MapMath.LatToWorldY(north, zoom);
            double x1 = MapMath.LngToWorldX(east, zoom), y1 = MapMath.LatToWorldY(south, zoom);
            if (x0 > x1) (x0, x1) = (x1, x0);
            if (y0 > y1) (y0, y1) = (y1, y0);

            int tx0 = (int)Math.Floor(x0 / 256), tx1 = (int)Math.Floor(x1 / 256);
            int ty0 = (int)Math.Floor(y0 / 256), ty1 = (int)Math.Floor(y1 / 256);
            int maxTiles = (int)Math.Pow(2, zoom);

            var tilesSvg = new StringBuilder();
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    if (tx < 0 || ty < 0 || tx >= maxTiles || ty >= maxTiles) continue;
                    var p = tiles.TilePath(zoom, tx, ty);
                    if (!File.Exists(p)) continue;
                    var b64 = Convert.ToBase64String(File.ReadAllBytes(p));
                    tilesSvg.Append($"<image x=\"{tx * 256 - x0}\" y=\"{ty * 256 - y0}\" width=\"256\" height=\"256\" xlink:href=\"data:image/png;base64,{b64}\"/>");
                }

            double W = Math.Max(200, x1 - x0), H = Math.Max(200, y1 - y0);
            Point ToXY(double lng, double lat) => new Point(MapMath.LngToWorldX(lng, zoom) - x0, MapMath.LatToWorldY(lat, zoom) - y0);
            double radiusPx = curveRadiusM / MapMath.MetersPerPixel((south + north) / 2, zoom);
            double midLat = (south + north) / 2;
            double pxPerMeter = 1.0 / MapMath.MetersPerPixel(midLat, zoom);

            var xyDict = stations.Values.ToDictionary(s => s.Id, s => ToXY(s.Lng, s.Lat));
            double autoScale = fontScale < 0 ? AutoLabelScale(stations, xyDict) : 1.0;
            double effFont = fontScale < 0 ? 1.0 : fontScale;

            var body = new StringBuilder();
            Func<GeoPoint, Point> anchorProj = g => ToXY(g.Lng, g.Lat);
            foreach (var line in lines.Values)
            {
                var color = string.IsNullOrEmpty(line.LineColor) ? line.Color : line.LineColor;
                double lw = 6 * lineScale * (line.LineWidth > 0 ? line.LineWidth / 5.0 : 1.0);
                var trunk = line.StationOrder.Where(id => stations.ContainsKey(id)).Select(id => ToXY(stations[id].Lng, stations[id].Lat)).ToList();
                AppendLineBodyEx(body, trunk, line.StationOrder, line.Segments, color, radiusPx, lw, anchorProj);
                foreach (var br in line.Branches)
                {
                    var order = new List<string> { br.Junction };
                    order.AddRange(br.StationOrder);
                    var bpts = order.Where(id => stations.ContainsKey(id)).Select(id => ToXY(stations[id].Lng, stations[id].Lat)).ToList();
                    AppendLineBodyEx(body, bpts, order, br.Segments, color, radiusPx, lw, anchorProj);
                }
            }
            foreach (var s in stations.Values)
            {
                var p = ToXY(s.Lng, s.Lat); bool tr = s.IsTransfer;
                body.Append(tr
                    ? $"<circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"{13 * effFont:F2}\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"{3 * effFont:F2}\"/><circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"{5 * effFont:F2}\" fill=\"#1f2733\"/>"
                    : $"<circle cx=\"{p.X:F2}\" cy=\"{p.Y:F2}\" r=\"{9 * effFont:F2}\" fill=\"#fff\" stroke=\"#1f2733\" stroke-width=\"{2.5 * effFont:F2}\"/>");
            }
            body.Append(BuildLabelsSvg(stations, lines, xyDict, showCn, showEn, showThird, 1.5 * effFont * autoScale, 1.0, W));

            body.Append(ScaleBarSvg(pxPerMeter, W, H));

            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\">" +
                   $"<rect width=\"{W}\" height=\"{H}\" fill=\"#e8e6df\"/>{tilesSvg}{body}</svg>";
        }

        static void AppendLineBodyEx(StringBuilder body, List<Point> pts, List<string> order, List<Segment> segs, string color, double radiusPx, double lw, Func<GeoPoint, Point> anchorProj = null)
        {
            if (pts.Count < 2) return;
            if (segs == null || segs.Count == 0)
            {
                body.Append($"<path d=\"{ToSvgPath(PathBuilder.Rounded(pts, radiusPx))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{lw:F1}\" stroke-linejoin=\"round\"/>");
                return;
            }
            for (int i = 0; i < pts.Count - 1 && i < segs.Count; i++)
            {
                var seg = segs[i];
                var way = new List<Point> { pts[i] };
                if (seg.Anchors != null && anchorProj != null)
                    foreach (var an in seg.Anchors) way.Add(anchorProj(an));
                way.Add(pts[i + 1]);
                var type = seg.Type;
                var (_, dash) = SEG.TryGetValue(type, out var kv) ? kv : ("", "");
                string dashAttr = string.IsNullOrEmpty(dash) ? "" : $" stroke-dasharray=\"{dash}\"";
                body.Append($"<path d=\"{ToSvgPath(PathBuilder.Rounded(way, radiusPx))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{lw:F1}\" stroke-linecap=\"round\"{dashAttr}/>");
            }
        }

        static void AppendLineBody(StringBuilder body, List<Point> pts, List<string> order, List<Segment> segs, string color, double radiusPx, Func<GeoPoint, Point> anchorProj = null)
        {
            AppendLineBodyEx(body, pts, order, segs, color, radiusPx, 6, anchorProj);
        }
    }
}
