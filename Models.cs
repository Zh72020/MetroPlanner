using System;
using System.Collections.Generic;
using System.Windows;

namespace MetroPlanner.Models
{
    public class Station
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string NameCn { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string NameThird { get; set; } = "";
        public string ThirdLabel { get; set; } = "";
        public bool Transfer { get; set; }
        public double Lng { get; set; }
        public double Lat { get; set; }
        public HashSet<string> Lines { get; set; } = new HashSet<string>();
        public Point SchematicPos { get; set; }
        /// <summary>创建时间（Unix 毫秒），用于按创建时间排序。</summary>
        public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ---- 每站独立样式（覆盖线路样式；空/0 表示沿用线路样式）----
        public string CnFont { get; set; } = "";      // 中文名字体
        public string EnFont { get; set; } = "";      // 英文名字体
        public string ThirdFont { get; set; } = "";   // 第三语言字体
        public string FontColor { get; set; } = "";   // 文字颜色（#RRGGBB，空=默认深色）
        public double FontSize { get; set; } = 0;      // 字号（0=沿用线路）
        public bool FontBold { get; set; } = false;    // 加粗（独立于线路）
        public bool FontOutline { get; set; } = true;  // 描边

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsTransfer => Transfer || Lines.Count >= 2;
    }

    public class Segment
    {
        public string A { get; set; }
        public string B { get; set; }
        public string Type { get; set; } = "underground";
        /// <summary>两站之间线路绕行经过的锚点（经纬度）。</summary>
        public List<GeoPoint> Anchors { get; set; } = new List<GeoPoint>();
        /// <summary>钢笔式曲线锚点（带手柄），用于在两站之间绘制贝塞尔曲线。</summary>
        public List<CurveAnchor> CurveAnchors { get; set; } = new List<CurveAnchor>();
    }

    /// <summary>
    /// 钢笔式锚点：位于线段上的一个点，带两个控制点（手柄），用于绘制贝塞尔曲线。
    /// 曲线为 A→锚点→B，其中锚点两侧各有一个手柄（InCtrl / OutCtrl），
    /// 形成 锚点两侧的贝塞尔曲线段，实现 PS 钢笔般的平滑/转折曲线。
    /// </summary>
    public class CurveAnchor
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        /// <summary>锚点位置（经纬度，位于线段上）。</summary>
        public double Lng { get; set; }
        public double Lat { get; set; }
        /// <summary>入向手柄相对锚点的偏移（经纬度差），控制进入锚点的曲线方向。</summary>
        public double InDlng { get; set; }
        public double InDlat { get; set; }
        /// <summary>出向手柄相对锚点的偏移（经纬度差），控制离开锚点的曲线方向。</summary>
        public double OutDlng { get; set; }
        public double OutDlat { get; set; }
        /// <summary>手柄类型：smooth=平滑（两侧共线反向），corner=尖角（独立控制）。</summary>
        public string Mode { get; set; } = "smooth";

        public GeoPoint InCtrl => new GeoPoint(Lng + InDlng, Lat + InDlat);
        public GeoPoint OutCtrl => new GeoPoint(Lng + OutDlng, Lat + OutDlat);
    }

    public class GeoPoint
    {
        public double Lng { get; set; }
        public double Lat { get; set; }
        public GeoPoint() { }
        public GeoPoint(double lng, double lat) { Lng = lng; Lat = lat; }
    }

    public class Branch
    {
        public string Junction { get; set; } = "";
        public List<string> StationOrder { get; set; } = new();
        public List<Segment> Segments { get; set; } = new();
    }

    public class Line
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string Color { get; set; } = "#e4002b";
        public List<string> StationOrder { get; set; } = new List<string>();
        public List<Segment> Segments { get; set; } = new List<Segment>();
        public List<Branch> Branches { get; set; } = new List<Branch>();

        // ---- 样式 / 个性化 ----
        public string LabelFont { get; set; } = "";        // 站名字体（空=默认，向后兼容）
        public string CnFont { get; set; } = "";           // 中文名专用字体（空=用 LabelFont/默认）
        public string EnFont { get; set; } = "";           // 英文名专用字体（空=用 LabelFont/默认）
        public string ThirdFont { get; set; } = "";        // 第三语言专用字体（空=用 LabelFont/默认）
        public double FontSize { get; set; } = 12;          // 站名字号
        public bool FontBold { get; set; } = false;          // 站名加粗
        public bool FontOutline { get; set; } = true;        // 字体描边（白底）
        public string LineColor { get; set; } = "";          // 精确线路颜色（覆盖 Color，空=用 Color）
        public double LineWidth { get; set; } = 5;           // 线路粗细
        public string LogoText { get; set; } = "";           // 线路 logo 文字
        public string LogoColor { get; set; } = "#ffffff";   // logo 文字颜色
        public string LogoPath { get; set; } = "";           // 线路 logo 图片路径
        /// <summary>共线时是否与其他线路并置显示（默认 true）。</summary>
        public bool ColinearOffset { get; set; } = true;
        /// <summary>创建时间（Unix 毫秒），用于按创建时间排序。</summary>
        public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public class ProjectData
    {
        public Dictionary<string, Station> Stations { get; set; } = new();
        public Dictionary<string, Line> Lines { get; set; } = new();
        public Dictionary<string, Point> SchematicPos { get; set; } = new();

        // 项目元信息
        public string ProjectName { get; set; } = "未命名项目";
        public double AnchorLng { get; set; }
        public double AnchorLat { get; set; }
        public string AnchorName { get; set; } = "";
        public bool Constrained { get; set; } = true;
        public double MaxDistKm { get; set; } = 60;
        public double MapSideKm { get; set; } = 40;
    }
}
