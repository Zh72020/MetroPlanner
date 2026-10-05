using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetroPlanner.Models;

namespace MetroPlanner
{
    /// <summary>
    /// PSD 分层导出器：将地铁线路图按「文字 / 站点图标 / 线路网（每条线路一个图层）/ 背景」分组，
    /// 每个站点文字、每个站点图标、每条线路各自独立成图层并命名，用 Photoshop 打开后可按组快速定位。
    /// 手写 PSD 文件格式（8-bit RGB + 图层 + 图层组 section divider）。
    /// </summary>
    public static class PsdExporter
    {
        // ---- 图层/组 数据模型 ----
        class PNode { public string Name; }
        class PLayer : PNode { public int L, T, R, B; public byte[] Rgba; } // Rgba 长度 = w*h*4
        class PGroup : PNode { public List<PNode> Items = new(); }

        public static void Export(
            Dictionary<string, Station> stations, Dictionary<string, Line> lines,
            Dictionary<string, Point> schematicPos, string path,
            int outW, int outH, bool showCn, bool showEn, bool showThird,
            double fontScale, double lineScale,
            bool showText, bool showLines, bool showStations, bool showBackground,
            bool scaleBar, double curveRadiusM, int zoom, TileService tiles)
        {
            if (stations.Count == 0) throw new Exception("无站点数据");
            // 使用真实地理坐标（线路图全貌），与 PNG 导出一致
            double minLng = stations.Values.Min(s => s.Lng), maxLng = stations.Values.Max(s => s.Lng);
            double minLat = stations.Values.Min(s => s.Lat), maxLat = stations.Values.Max(s => s.Lat);
            double padLng = Math.Max((maxLng - minLng) * 0.15, 0.01);
            double padLat = Math.Max((maxLat - minLat) * 0.15, 0.01);
            double west = minLng - padLng, east = maxLng + padLng;
            double south = minLat - padLat, north = maxLat + padLat;

            double x0 = MapMath.LngToWorldX(west, zoom), x1 = MapMath.LngToWorldX(east, zoom);
            double y0 = MapMath.LatToWorldY(north, zoom), y1 = MapMath.LatToWorldY(south, zoom);
            double wpx = Math.Abs(x1 - x0), hpx = Math.Abs(y1 - y0);

            int W, H;
            Func<double, double, Point> proj;
            double sc = 1.0;
            if (outW > 0 || outH > 0)
            {
                W = outW > 0 ? outW : Math.Max(1, (int)Math.Round(wpx * outH / hpx));
                H = outH > 0 ? outH : Math.Max(1, (int)Math.Round(hpx * outW / wpx));
                double sx = W / wpx, sy = H / hpx;
                sc = Math.Min(sx, sy);
                double ox = (W - wpx * sc) / 2, oy = (H - hpx * sc) / 2;
                proj = (lng, lat) => new Point((MapMath.LngToWorldX(lng, zoom) - x0) * sc + ox, (MapMath.LatToWorldY(lat, zoom) - y0) * sc + oy);
            }
            else
            {
                W = Math.Max(200, (int)Math.Ceiling(wpx));
                H = Math.Max(200, (int)Math.Ceiling(hpx));
                proj = (lng, lat) => new Point(MapMath.LngToWorldX(lng, zoom) - x0, MapMath.LatToWorldY(lat, zoom) - y0);
            }
            W = Math.Max(1, W); H = Math.Max(1, H);

            double autoScale = fontScale < 0 ? AutoScale(stations, proj) : 1.0;
            double effFont = fontScale < 0 ? autoScale : fontScale;

            // 预计算站点屏幕位置
            var pos = stations.Values.ToDictionary(s => s.Id, s => proj(s.Lng, s.Lat));

            var roots = new List<PNode>();

            // ---- 组：文字标注 ----
            if (showText)
            {
                var gText = new PGroup { Name = "文字标注 Text" };
                foreach (var s in stations.Values)
                {
                    var labels = new List<string>();
                    if (showCn && !string.IsNullOrEmpty(s.NameCn)) labels.Add(s.NameCn);
                    if (showEn && !string.IsNullOrEmpty(s.NameEn)) labels.Add(s.NameEn);
                    if (showThird && !string.IsNullOrEmpty(s.NameThird)) labels.Add(s.NameThird);
                    if (labels.Count == 0) continue;
                    var layer = RenderTextLayer(s, lines, pos, labels, effFont);
                    if (layer != null) { layer.Name = s.NameCn ?? "站点"; gText.Items.Add(layer); }
                }
                if (gText.Items.Count > 0) roots.Add(gText);
            }

            // ---- 组：站点图标 ----
            if (showStations)
            {
                var gSt = new PGroup { Name = "站点图标 Stations" };
                foreach (var s in stations.Values)
                {
                    var layer = RenderStationIconLayer(s, pos[s.Id], effFont);
                    if (layer != null) { layer.Name = (s.NameCn ?? "站点") + " 图标"; gSt.Items.Add(layer); }
                }
                if (gSt.Items.Count > 0) roots.Add(gSt);
            }

            // ---- 组：线路网 ----
            if (showLines)
            {
                var gLines = new PGroup { Name = "线路网 Lines" };
                foreach (var line in lines.Values)
                {
                    var layer = RenderLineLayer(line, stations, pos, curveRadiusM, zoom, proj, lineScale);
                    if (layer != null) { layer.Name = string.IsNullOrEmpty(line.Name) ? ("线路 " + line.Id) : line.Name; gLines.Items.Add(layer); }
                }
                if (gLines.Items.Count > 0) roots.Add(gLines);
            }

            // ---- 组：背景 ----
            if (showBackground || scaleBar)
            {
                var gBg = new PGroup { Name = "背景 Background" };
                if (showBackground)
                {
                    var bg = RenderBackgroundLayer(tiles, zoom, west, south, east, north, proj, W, H);
                    if (bg != null) { bg.Name = "底图瓦片"; gBg.Items.Add(bg); }
                }
                if (scaleBar)
                {
                    var sb = RenderScaleBarLayer(W, H, proj, (south + north) / 2, zoom, sc);
                    if (sb != null) { sb.Name = "比例尺"; gBg.Items.Add(sb); }
                }
                if (gBg.Items.Count > 0) roots.Add(gBg);
            }

            // 合成一张合并位图（PSD 需要合并图像数据 + 用于底图）
            var composite = RenderComposite(roots, W, H);
            if (composite == null) throw new Exception("无内容可导出");

            WritePsd(path, roots, composite, W, H);
        }

        // ================= 渲染各图层 =================

        static double AutoScale(Dictionary<string, Station> stations, Func<double, double, Point> proj)
        {
            var pts = stations.Values.Select(s => proj(s.Lng, s.Lat)).ToList();
            if (pts.Count < 2) return 1.0;
            double minDist = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    double d = Dist(pts[i], pts[j]);
                    if (d > 1) minDist = Math.Min(minDist, d);
                }
            if (minDist >= double.MaxValue) return 1.0;
            return Math.Max(0.5, Math.Min(1.5, minDist / 50.0));
        }

        static double Dist(Point a, Point b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }

        static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromRgb(0x1f, 0x27, 0x33); }
        }

        static string FontFor(Station s, Line line, int lang)
        {
            string sf = lang switch { 0 => s.CnFont, 1 => s.EnFont, 2 => s.ThirdFont, _ => "" };
            if (!string.IsNullOrEmpty(sf)) return sf;
            if (line == null) return "";
            string f = lang switch { 0 => line.CnFont, 1 => line.EnFont, 2 => line.ThirdFont, _ => "" };
            if (string.IsNullOrEmpty(f)) f = line.LabelFont;
            return f;
        }
        static double FontSizeFor(Station s, Line line) => s.FontSize > 0 ? s.FontSize : (line != null && line.FontSize > 0 ? line.FontSize : 12);
        static bool BoldFor(Station s, Line line) => s.FontBold || (line != null && line.FontBold);
        static Color ColorFor(Station s, Line line) => !string.IsNullOrEmpty(s.FontColor) ? ParseColor(s.FontColor) : Color.FromRgb(0x1f, 0x27, 0x33);

        // 计算线路在站点处的近似切向（用于 PSD 文字方向避让）
        static Point? PsdLineTangent(Station s, Dictionary<string, Line> lines, Dictionary<string, Point> pos)
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

        static PLayer RenderTextLayer(Station s, Dictionary<string, Line> lines, Dictionary<string, Point> pos, List<string> labels, double effFont)
        {
            Line styleLine = null;
            foreach (var lid in s.Lines) if (lines.TryGetValue(lid, out var l)) { styleLine = l; break; }
            double fontSize = FontSizeFor(s, styleLine) * effFont;
            bool bold = BoldFor(s, styleLine);
            Color color = ColorFor(s, styleLine);

            if (!pos.TryGetValue(s.Id, out var p)) return null;

            // 先测量每行尺寸
            var fits = new List<(string text, string fam, FormattedText ft)>();
            double maxW = 0, totalH = 0;
            double lineH = fontSize + 3;
            for (int i = 0; i < labels.Count; i++)
            {
                string fam = FontFor(s, styleLine, i);
                if (string.IsNullOrEmpty(fam)) fam = "Segoe UI";
                FormattedText ft;
                try
                {
                    var tf = new Typeface(new FontFamily(fam), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.SemiBold, FontStretches.Normal);
                    ft = new FormattedText(labels[i], System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, fontSize, new SolidColorBrush(color), 1.0);
                }
                catch
                {
                    var tf = new Typeface("Segoe UI");
                    ft = new FormattedText(labels[i], System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, fontSize, new SolidColorBrush(color), 1.0);
                }
                fits.Add((labels[i], fam, ft));
                maxW = Math.Max(maxW, ft.Width);
                totalH += lineH;
            }
            int pad = 6;
            int bw = (int)Math.Ceiling(maxW) + pad * 2;
            int bh = (int)Math.Ceiling(totalH) + pad * 2;
            if (bw <= 0 || bh <= 0) return null;

            // 方向避让：东西走向线路文字放上方（居中），否则放右侧
            var tan = PsdLineTangent(s, lines, pos);
            bool horizontal = tan.HasValue && Math.Abs(tan.Value.X) > Math.Abs(tan.Value.Y) * 1.4;
            double gap = 10 * effFont;
            double left, top;
            if (horizontal)
            {
                top = p.Y - totalH - gap;
                left = p.X - maxW / 2;
            }
            else
            {
                left = p.X + gap;
                top = p.Y - totalH / 2;
            }

            var rtb = new RenderTargetBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double cy = pad - 0; // 首行基线
                for (int i = 0; i < fits.Count; i++)
                {
                    var (text, fam, ft) = fits[i];
                    var posPt = new Point(pad, pad + i * lineH);
                    var geo = ft.BuildGeometry(posPt);
                    // 白描边
                    var op = new Pen(Brushes.White, 3) { LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(Brushes.White, op, geo);
                    dc.DrawText(ft, posPt);
                }
            }
            rtb.Render(dv);
            return new PLayer { L = (int)Math.Floor(left), T = (int)Math.Floor(top), R = (int)Math.Floor(left) + bw, B = (int)Math.Floor(top) + bh, Rgba = ExtractRgba(rtb) };
        }

        static PLayer RenderStationIconLayer(Station s, Point p, double effFont)
        {
            double r = s.IsTransfer ? 9 : 6;
            int pad = 4;
            int d = (int)Math.Ceiling(r * 2) + pad * 2;
            var rtb = new RenderTargetBitmap(d, d, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var c = new Point(pad + d / 2.0, pad + d / 2.0);
                var stroke = new SolidColorBrush(Color.FromRgb(0x1f, 0x27, 0x33));
                dc.DrawEllipse(Brushes.White, new Pen(stroke, s.IsTransfer ? 2.2 : 1.8), c, r, r);
                if (s.IsTransfer) dc.DrawEllipse(stroke, null, c, 3.4, 3.4);
            }
            rtb.Render(dv);
            return new PLayer { L = (int)Math.Floor(p.X - r) - pad, T = (int)Math.Floor(p.Y - r) - pad, R = (int)Math.Floor(p.X - r) - pad + d, B = (int)Math.Floor(p.Y - r) - pad + d, Rgba = ExtractRgba(rtb) };
        }

        static PLayer RenderLineLayer(Line line, Dictionary<string, Station> stations, Dictionary<string, Point> pos,
            double curveRadiusM, int zoom, Func<double, double, Point> proj, double lineScale)
        {
            if (line.StationOrder.Count < 2) return null;
            var color = ParseColor(string.IsNullOrEmpty(line.LineColor) ? line.Color : line.LineColor);
            double pxPerDeg = Math.Abs(proj(0, 0).X - proj(1, 0).X);
            double radiusPx = curveRadiusM / 111320.0 * pxPerDeg;
            double lw = (line.LineWidth > 0 ? line.LineWidth : 5) * lineScale;

            // 收集所有需要绘制的点，求包围盒
            var allPts = new List<Point>();
            var segList = new List<(List<Point> pts, string type, List<GeoPoint> anchors)>();
            void AddOrder(List<string> order, List<Segment> segs)
            {
                var pts = order.Where(id => pos.ContainsKey(id)).Select(id => pos[id]).ToList();
                if (pts.Count < 2) return;
                if (segs == null || segs.Count == 0) { segList.Add((pts, "", null)); allPts.AddRange(pts); return; }
                for (int i = 0; i < pts.Count - 1 && i < segs.Count; i++)
                {
                    var seg = segs[i];
                    var way = new List<Point> { pts[i] };
                    if (seg.Anchors != null) foreach (var an in seg.Anchors) way.Add(proj(an.Lng, an.Lat));
                    way.Add(pts[i + 1]);
                    segList.Add((way, seg.Type, null));
                    allPts.AddRange(way);
                }
            }
            AddOrder(line.StationOrder, line.Segments);
            foreach (var br in line.Branches)
            {
                var order = new List<string> { br.Junction };
                order.AddRange(br.StationOrder);
                AddOrder(order, br.Segments);
            }
            if (allPts.Count < 2) return null;

            double minX = allPts.Min(p => p.X), maxX = allPts.Max(p => p.X);
            double minY = allPts.Min(p => p.Y), maxY = allPts.Max(p => p.Y);
            int pad = (int)Math.Ceiling(lw) + 8;
            int L = (int)Math.Floor(minX) - pad, T = (int)Math.Floor(minY) - pad;
            int W = (int)Math.Ceiling(maxX - minX) + pad * 2, Hh = (int)Math.Ceiling(maxY - minY) + pad * 2;
            if (W <= 0 || Hh <= 0) return null;

            var rtb = new RenderTargetBitmap(W, Hh, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var brush = new SolidColorBrush(color);
                foreach (var (pts, type, _) in segList)
                {
                    var pen = new Pen(brush, lw) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    if (type == "transition") pen.DashStyle = new DashStyle(new double[] { 1, 6 }, 0);
                    else if (type == "ground") pen.DashStyle = new DashStyle(new double[] { 12, 8 }, 0);
                    var off = pts.Select(p => new Point(p.X - L, p.Y - T)).ToList();
                    DrawPath(dc, off, brush, lw, radiusPx, pen);
                }
            }
            rtb.Render(dv);
            return new PLayer { L = L, T = T, R = L + W, B = T + Hh, Rgba = ExtractRgba(rtb) };
        }

        static void DrawPath(DrawingContext dc, List<Point> pts, SolidColorBrush brush, double width, double radius, Pen overridePen)
        {
            var cmds = PathBuilder.Rounded(pts, radius);
            var fig = new PathFigure { StartPoint = cmds[0].Point };
            foreach (var c in cmds.Skip(1))
            {
                if (c.IsArc) fig.Segments.Add(new ArcSegment(c.Point, new Size(c.Radius, c.Radius), 0, false, c.Sweep ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
                else fig.Segments.Add(new LineSegment(c.Point, true));
            }
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            var pen = overridePen ?? new Pen(brush, width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, geo);
        }

        static PLayer RenderBackgroundLayer(TileService tiles, int zoom, double west, double south, double east, double north, Func<double, double, Point> proj, int W, int H)
        {
            double x0 = MapMath.LngToWorldX(west, zoom), y0 = MapMath.LatToWorldY(north, zoom);
            int tx0 = (int)Math.Floor(x0 / 256), tx1 = (int)Math.Floor(MapMath.LngToWorldX(east, zoom) / 256);
            int ty0 = (int)Math.Floor(y0 / 256), ty1 = (int)Math.Floor(MapMath.LatToWorldY(south, zoom) / 256);
            int maxTiles = (int)Math.Pow(2, zoom);
            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
                for (int ty = ty0; ty <= ty1; ty++)
                    for (int tx = tx0; tx <= tx1; tx++)
                    {
                        if (tx < 0 || ty < 0 || tx >= maxTiles || ty >= maxTiles) continue;
                        if (!tiles.Exists(zoom, tx, ty)) continue;
                        try
                        {
                            var bmp = tiles.Load(zoom, tx, ty);
                            if (bmp == null) continue;
                            var tl = proj(MapMath.WorldXToLng(tx * 256.0, zoom), MapMath.WorldYToLat(ty * 256.0, zoom));
                            var br = proj(MapMath.WorldXToLng((tx + 1) * 256.0, zoom), MapMath.WorldYToLat((ty + 1) * 256.0, zoom));
                            dc.DrawImage(bmp, new Rect(tl, br));
                        }
                        catch { }
                    }
            }
            rtb.Render(dv);
            return new PLayer { L = 0, T = 0, R = W, B = H, Rgba = ExtractRgba(rtb) };
        }

        static PLayer RenderScaleBarLayer(int W, int H, Func<double, double, Point> proj, double midLat, int zoom, double sc)
        {
            double pxPerMeter = 1.0 / MapMath.MetersPerPixel(midLat, zoom) * sc;
            double targetM = 200 / Math.Max(pxPerMeter, 1e-9);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(targetM)));
            double norm = targetM / mag;
            double nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
            double lenM = nice * mag;
            double lenPx = lenM * pxPerMeter;
            double x = 24, y = H - 26;
            string label = lenM >= 1000 ? $"{lenM / 1000:0.#} km" : $"{lenM:0} m";

            var ft = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.Black, 1.0);
            int pad = 8;
            int bw = (int)Math.Ceiling(Math.Max(lenPx, ft.Width)) + pad * 2;
            int bh = 44;
            var rtb = new RenderTargetBitmap(bw, bh, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var pen = new Pen(Brushes.Black, 1.5);
                double ly = bh - pad;
                dc.DrawLine(pen, new Point(pad, ly), new Point(pad + lenPx, ly));
                dc.DrawLine(pen, new Point(pad, ly - 4), new Point(pad, ly + 4));
                dc.DrawLine(pen, new Point(pad + lenPx, ly - 4), new Point(pad + lenPx, ly + 4));
                dc.DrawText(ft, new Point(pad + lenPx / 2 - ft.Width / 2, ly - 22));
            }
            rtb.Render(dv);
            int L = (int)Math.Floor(x) - pad;
            int T = (int)Math.Floor(y) - bh + pad;
            return new PLayer { L = L, T = T, R = L + bw, B = T + bh, Rgba = ExtractRgba(rtb) };
        }

        static byte[] ExtractRgba(BitmapSource bmp)
        {
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            var px = new byte[w * h * 4];
            bmp.CopyPixels(px, w * 4, 0);
            // Pbgra32 -> 转成 RGBA（B 与 R 交换）
            for (int i = 0; i < px.Length; i += 4)
            {
                byte b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = a;
            }
            return px;
        }

        static PLayer RenderComposite(List<PNode> roots, int W, int H)
        {
            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
                DrawNodes(dc, roots);
            }
            rtb.Render(dv);
            return new PLayer { L = 0, T = 0, R = W, B = H, Rgba = ExtractRgba(rtb) };
        }

        static void DrawNodes(DrawingContext dc, List<PNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n is PGroup g) DrawNodes(dc, g.Items);
                else if (n is PLayer l) DrawLayerTo(dc, l);
            }
        }
        static void DrawLayerTo(DrawingContext dc, PLayer l)
        {
            int w = l.R - l.L, h = l.B - l.T;
            if (w <= 0 || h <= 0) return;
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, l.Rgba, w * 4);
            dc.DrawImage(bmp, new Rect(l.L, l.T, w, h));
        }

        // ================= PSD 写入 =================
        // PSD 全部整数为大端序（Big Endian）

        static void W16(BinaryWriter w, ushort v) => w.Write(new byte[] { (byte)(v >> 8), (byte)v });
        static void W16s(BinaryWriter w, short v) => w.Write(new byte[] { (byte)(v >> 8), (byte)v });
        static void W32(BinaryWriter w, int v) => w.Write(new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

        static void WritePsd(string path, List<PNode> roots, PLayer composite, int W, int H)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);

            // --- 展平图层列表（自底向上），并计算分组边界 ---
            var flat = new List<object>(); // 元素：PLayer 或 (string groupName, bool close)
            BuildFlat(roots, flat);

            // --- 文件头 ---
            bw.Write(Encoding.ASCII.GetBytes("8BPS"));       // 签名
            W16(bw, 1);                                       // 版本
            bw.Write(new byte[6]);                            // 保留
            W16(bw, 4);                                       // 通道数（RGBA）
            W32(bw, H);                                       // 高
            W32(bw, W);                                       // 宽
            W16(bw, 8);                                       // 深度 8bit
            W16(bw, 3);                                       // 颜色模式 RGB

            // --- 颜色模式数据段 ---
            W32(bw, 0);

            // --- 图像资源段 ---
            W32(bw, 0);

            // --- 图层和蒙版信息段 ---
            var layerInfo = BuildLayerInfo(flat, W, H);
            W32(bw, layerInfo.Length);
            bw.Write(layerInfo);

            // --- 合并图像数据 ---
            var merged = BuildMergedImage(composite, W, H);
            W16(bw, 0); // 压缩方式：raw
            bw.Write(merged);

            bw.Flush();
        }

        static void BuildFlat(List<PNode> nodes, List<object> flat)
        {
            foreach (var n in nodes)
            {
                if (n is PGroup g)
                {
                    flat.Add((g.Name, false)); // 组开始
                    BuildFlat(g.Items, flat);
                    flat.Add((g.Name, true));  // 组结束
                }
                else if (n is PLayer l)
                {
                    flat.Add(l);
                }
            }
        }

        static byte[] BuildLayerInfo(List<object> flat, int W, int H)
        {
            // 先把 flat 转成「元数据 + 通道数据」两块
            // 元数据：每层 rect + channel info（id/长度） + blend + flags + extra data
            // 通道数据：所有层按顺序的通道像素数据（compression 头 + 数据）
            using var metaMs = new MemoryStream();
            using var metaBw = new BinaryWriter(metaMs);
            using var dataMs = new MemoryStream();
            using var dataBw = new BinaryWriter(dataMs);

            int count = flat.Count;
            W16s(metaBw, (short)-count);

            foreach (var item in flat)
            {
                PLayer layer;
                int sectionType = 0;
                string groupName = null;
                if (item is PLayer l) { layer = l; }
                else if (item is (string gname, bool close))
                {
                    // 组边界图层：开始用 type 3（bounding section divider，名字用 PS 惯例），
                    // 结束用 type 2（closed folder，名字=组名，PS 用它显示组名）
                    string nm = close ? gname : "</Layer group>";
                    layer = new PLayer { L = 0, T = 0, R = 1, B = 1, Rgba = new byte[4] };
                    sectionType = close ? 2 : 3;
                    groupName = nm;
                }
                else continue;

                WriteLayerRecordHeader(metaBw, layer, sectionType, groupName);
                WriteLayerChannelData(dataBw, layer);
            }

            // 组装：layer info 长度 = (2 字节 count) + meta + data
            var metaBytes = metaMs.ToArray();
            var dataBytes = dataMs.ToArray();
            int layerInfoLen = metaBytes.Length + dataBytes.Length;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            W32(bw, layerInfoLen);      // Layer Info 总长度（不含这 4 字节）
            bw.Write(metaBytes);        // layer count + layer records 元数据
            bw.Write(dataBytes);        // channel image data
            W32(bw, 0);                 // 全局图层蒙版信息（无）
            return ms.ToArray();
        }

        // 写图层记录的「元数据」部分（rect + channel info + blend + flags + extra data）
        // 注意：PSD 中图层元数据与通道像素数据是分开的两大块，本方法只写元数据。
        static void WriteLayerRecordHeader(BinaryWriter bw, PLayer l, int sectionType, string groupName)
        {
            int lw = l.R - l.L, lh = l.B - l.T;
            if (lw <= 0) lw = 1; if (lh <= 0) lh = 1;

            // 矩形 top/left/bottom/right
            W32(bw, l.T); W32(bw, l.L); W32(bw, l.B); W32(bw, l.R);

            // 通道数量 + 每通道信息（id + 长度）；长度含 2 字节 compression 头
            int npix = lw * lh;
            var rle = RleAllChannels(l, lw);
            W16(bw, 4); // RGBA 共 4 通道
            for (int ci = 0; ci < 4; ci++)
            {
                W16s(bw, rle[ci].id);
                int chLen = 2 + rle[ci].lengths.Length * 2 + rle[ci].data.Length; // +2 = compression 头
                W32(bw, chLen);
            }

            // 混合模式签名 + 模式 + 不透明度 + 裁剪 + 标志 + 填充
            bw.Write(Encoding.ASCII.GetBytes("8BIM"));
            bw.Write(Encoding.ASCII.GetBytes("norm"));
            bw.Write((byte)255); // 不透明度
            bw.Write((byte)0);   // 裁剪
            bw.Write((byte)8);   // 标志（bit3 = Photoshop 5.0+）
            bw.Write((byte)0);   // 填充字节（filler）

            // 额外数据
            using var extra = new MemoryStream();
            using var ew = new BinaryWriter(extra);
            W32(ew, 0); // layer mask data
            W32(ew, 0); // blending ranges

            // 图层名（Pascal 字符串，padded to 4）
            string name = sectionType != 0 ? (groupName ?? "</Layer group>") : (l.Name ?? "");
            var nameBytes = Encoding.ASCII.GetBytes(name.Length <= 255 ? name : name.Substring(0, 255));
            ew.Write((byte)nameBytes.Length);
            ew.Write(nameBytes);
            int npad = (4 - ((1 + nameBytes.Length) % 4)) % 4;
            ew.Write(new byte[npad]);

            // 8BIM 块：Unicode 名称 (luni)
            WriteLuniBlock(ew, name);

            // 8BIM 块：section divider (lsct) —— 用于分组
            if (sectionType != 0)
                WriteSectionDivider(ew, sectionType);

            var extraBytes = extra.ToArray();
            W32(bw, extraBytes.Length);
            bw.Write(extraBytes);
        }

        // 计算 4 通道（R/G/B/A）的 RLE 编码结果
        static (short id, ushort[] lengths, byte[] data)[] RleAllChannels(PLayer l, int lw)
        {
            int lh = l.B - l.T;
            if (lh <= 0) lh = 1;
            int npix = lw * lh;
            var r = new byte[npix]; var g = new byte[npix]; var b = new byte[npix]; var a = new byte[npix];
            for (int i = 0; i < npix; i++)
            {
                r[i] = l.Rgba[i * 4]; g[i] = l.Rgba[i * 4 + 1]; b[i] = l.Rgba[i * 4 + 2]; a[i] = l.Rgba[i * 4 + 3];
            }
            var encR = PackBitsEncode(r, lw);
            var encG = PackBitsEncode(g, lw);
            var encB = PackBitsEncode(b, lw);
            var encA = PackBitsEncode(a, lw);
            return new (short, ushort[], byte[])[]
            {
                (0, encR.lengths, encR.data),
                (1, encG.lengths, encG.data),
                (2, encB.lengths, encB.data),
                (-1, encA.lengths, encA.data),
            };
        }

        // 写一个图层的通道像素数据（compression 头 + RLE 数据）
        static void WriteLayerChannelData(BinaryWriter bw, PLayer l)
        {
            int lw = l.R - l.L;
            if (lw <= 0) lw = 1;
            var rle = RleAllChannels(l, lw);
            foreach (var (id, lengths, data) in rle)
            {
                W16(bw, 1); // compression = RLE
                foreach (var len in lengths) W16(bw, len);
                bw.Write(data);
            }
        }

        static void WriteLuniBlock(BinaryWriter w, string name)
        {
            // 8BIM 块：signature(4) + key(4) + length(4) + data
            // data = 字符数(4) + UTF-16 BE 字符串（字符数*2 字节）
            var utf16 = Encoding.BigEndianUnicode.GetBytes(name);
            w.Write(Encoding.ASCII.GetBytes("8BIM"));
            w.Write(Encoding.ASCII.GetBytes("luni"));
            W32(w, 4 + utf16.Length);
            W32(w, name.Length); // 字符数（非字节数）
            w.Write(utf16);
        }

        static void WriteSectionDivider(BinaryWriter w, int type)
        {
            // 8BIM 块：signature(4) + key(4) + length(4) + data
            // data = type(4) + '8BIM'(4) + blend mode key(4) = 12 字节（标准格式）
            w.Write(Encoding.ASCII.GetBytes("8BIM"));
            w.Write(Encoding.ASCII.GetBytes("lsct"));
            W32(w, 12);
            W32(w, type);
            w.Write(Encoding.ASCII.GetBytes("8BIM"));
            w.Write(Encoding.ASCII.GetBytes("pass"));
        }

        static (ushort[] lengths, byte[] data) PackBitsEncode(byte[] src, int rowWidth)
        {
            int rows = src.Length / rowWidth;
            var allData = new List<byte>();
            var lengths = new ushort[rows];
            for (int y = 0; y < rows; y++)
            {
                var row = new byte[rowWidth];
                Array.Copy(src, y * rowWidth, row, 0, rowWidth);
                var enc = PackBitsRow(row);
                lengths[y] = (ushort)enc.Length;
                allData.AddRange(enc);
            }
            return (lengths, allData.ToArray());
        }

        static byte[] PackBitsRow(byte[] row)
        {
            var outp = new List<byte>();
            int i = 0;
            while (i < row.Length)
            {
                int runStart = i;
                // 找连续重复
                int runLen = 1;
                while (i + runLen < row.Length && row[i + runLen] == row[i] && runLen < 128) runLen++;
                if (runLen >= 3)
                {
                    outp.Add((byte)(1 - runLen)); // 负值 = 重复
                    outp.Add(row[i]);
                    i += runLen;
                }
                else
                {
                    // 找不重复段
                    int litStart = i;
                    int litLen = 0;
                    while (i < row.Length && litLen < 128)
                    {
                        // 判断当前位置是否开始一个 >=3 的重复
                        int rl = 1;
                        while (i + rl < row.Length && row[i + rl] == row[i] && rl < 3) rl++;
                        if (rl >= 3) break;
                        i++; litLen++;
                    }
                    outp.Add((byte)(litLen - 1));
                    for (int j = litStart; j < litStart + litLen; j++) outp.Add(row[j]);
                }
            }
            return outp.ToArray();
        }

        static byte[] BuildMergedImage(PLayer composite, int W, int H)
        {
            // 每通道一行一行写（RGBA 4 通道，raw 未压缩）；文件头 channels=4，故需 4 个通道平面
            int npix = W * H;
            var r = new byte[npix]; var g = new byte[npix]; var b = new byte[npix]; var a = new byte[npix];
            for (int i = 0; i < npix; i++)
            {
                r[i] = composite.Rgba[i * 4]; g[i] = composite.Rgba[i * 4 + 1];
                b[i] = composite.Rgba[i * 4 + 2]; a[i] = composite.Rgba[i * 4 + 3];
            }
            var outp = new byte[npix * 4];
            for (int i = 0; i < npix; i++)
            {
                outp[i] = r[i];
                outp[npix + i] = g[i];
                outp[npix * 2 + i] = b[i];
                outp[npix * 3 + i] = a[i];
            }
            return outp;
        }
    }
}
