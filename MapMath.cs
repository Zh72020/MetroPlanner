using System;
using System.Collections.Generic;
using System.Windows;

namespace MetroPlanner
{
    /// <summary>Web 墨卡托投影与几何工具。</summary>
    public static class MapMath
    {
        public const double TileSize = 256.0;

        public static double LngToWorldX(double lng, int zoom) =>
            (lng + 180.0) / 360.0 * Math.Pow(2, zoom) * TileSize;

        public static double LatToWorldY(double lat, int zoom)
        {
            double s = Math.Sin(lat * Math.PI / 180.0);
            return (0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * Math.Pow(2, zoom) * TileSize;
        }

        public static double WorldXToLng(double x, int zoom) =>
            x / (Math.Pow(2, zoom) * TileSize) * 360.0 - 180.0;

        public static double WorldYToLat(double y, int zoom)
        {
            double n = Math.PI - 2.0 * Math.PI * y / (Math.Pow(2, zoom) * TileSize);
            return 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        }

        public static double Haversine(double lng1, double lat1, double lng2, double lat2)
        {
            const double R = 6371000;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLng = (lng2 - lng1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                       Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * R * Math.Asin(Math.Sqrt(a));
        }

        public static double MetersPerPixel(double lat, int zoom) =>
            156543.03392 * Math.Cos(lat * Math.PI / 180) / Math.Pow(2, zoom);
    }

    /// <summary>路径命令：直线到点，或固定半径圆弧到点。</summary>
    public struct PathCmd
    {
        public bool IsArc;
        public Point Point;
        public double Radius;
        public bool Sweep; // true = 顺时针
    }

    public static class PathBuilder
    {
        public static double Dist(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>带固定转弯半径的圆角折线。</summary>
        public static List<PathCmd> Rounded(List<Point> pts, double radius)
        {
            var cmds = new List<PathCmd>();
            if (pts.Count < 2) return cmds;
            cmds.Add(new PathCmd { IsArc = false, Point = pts[0] });
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var p0 = pts[i - 1]; var p1 = pts[i]; var p2 = pts[i + 1];
                double d1 = Dist(p1, p0), d2 = Dist(p2, p1);
                if (d1 == 0 || d2 == 0) { cmds.Add(new PathCmd { IsArc = false, Point = p1 }); continue; }
                double r = Math.Min(radius, Math.Min(d1 / 2, d2 / 2));
                double v1x = (p1.X - p0.X) / d1, v1y = (p1.Y - p0.Y) / d1;
                double v2x = (p2.X - p1.X) / d2, v2y = (p2.Y - p1.Y) / d2;
                double cross = v1x * v2y - v1y * v2x;
                var t1 = new Point(p1.X - v1x * r, p1.Y - v1y * r);
                var t2 = new Point(p1.X + v2x * r, p1.Y + v2y * r);
                cmds.Add(new PathCmd { IsArc = false, Point = t1 });
                cmds.Add(new PathCmd { IsArc = true, Point = t2, Radius = r, Sweep = cross > 0 });
            }
            cmds.Add(new PathCmd { IsArc = false, Point = pts[pts.Count - 1] });
            return cmds;
        }

        /// <summary>横平竖直 + 45° 切角的示意图路径（返回顶点序列）。</summary>
        public static List<Point> Octilinear(List<Point> pts, double chamfer)
        {
            if (pts.Count < 2) return pts;
            var expanded = new List<Point> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                // 水平 / 垂直：直接连
                if (Math.Abs(dx) < 1e-9 || Math.Abs(dy) < 1e-9) { expanded.Add(b); continue; }
                // 45° 对角线：直接连（避免阶梯式大拐弯）
                if (Math.Abs(Math.Abs(dx) - Math.Abs(dy)) < 1e-9) { expanded.Add(b); continue; }
                // 其余：阶梯，优先走较长方向减少绕行
                if (Math.Abs(dx) >= Math.Abs(dy)) { expanded.Add(new Point(b.X, a.Y)); expanded.Add(b); }
                else { expanded.Add(new Point(a.X, b.Y)); expanded.Add(b); }
            }
            if (chamfer <= 0) return expanded;
            var outp = new List<Point> { expanded[0] };
            for (int i = 1; i < expanded.Count - 1; i++)
            {
                var a = expanded[i - 1]; var p = expanded[i]; var b = expanded[i + 1];
                double d1 = Dist(p, a), d2 = Dist(b, p);
                if (d1 == 0 || d2 == 0) { outp.Add(p); continue; }
                double u1x = (p.X - a.X) / d1, u1y = (p.Y - a.Y) / d1;
                double u2x = (b.X - p.X) / d2, u2y = (b.Y - p.Y) / d2;
                double dot = u1x * u2x + u1y * u2y;
                if (Math.Abs(dot) > 0.001) { outp.Add(p); continue; }
                outp.Add(new Point(p.X - u1x * chamfer, p.Y - u1y * chamfer));
                outp.Add(new Point(p.X + u2x * chamfer, p.Y + u2y * chamfer));
            }
            outp.Add(expanded[expanded.Count - 1]);
            return outp;
        }
    }
}
