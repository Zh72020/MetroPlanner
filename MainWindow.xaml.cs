using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using MetroPlanner.Models;
using Line = MetroPlanner.Models.Line;
using IOPath = System.IO.Path;

namespace MetroPlanner
{
    public partial class MainWindow : Window
    {
        // ---------- 数据 ----------
        Dictionary<string, Station> _stations = new();
        Dictionary<string, Line> _lines = new();
        Dictionary<string, Point> _schematicPos = new();

        string _tool = "select";
        string _activeLineId = null;
        string _selectedStationId = null;
        string _selectedLineId = null;
        string _segTypeDraw = "underground";
        string _mode = "map";
        string _currentFile = null;

        // 设站工具：放置类型 + 测距子模式
        bool _placeTransfer = false;          // 设站工具当前放置普通站(false)/换乘站(true)
        bool _measureActive = false;          // 设站工具内测距子模式是否激活

        // 排序设置（站点列表 / 线路管理共用）
        string _stationSort = "pinyin";       // pinyin | time | name
        bool _stationSortAsc = true;
        string _lineSort = "name";            // name | time
        bool _lineSortAsc = true;

        // 线路绘制工具：子模式（主线 / 支线 / 锚点）
        string _drawSubMode = "main";          // main | branch | anchor

        // 钢笔锚点拖拽：null=无；tuple(curveAnchorId, segKey, isInCtrl)
        string _dragCurveSegKey = null;
        int _dragCurveAnchorIdx = -1;
        int _dragCurveCtrl = 0;                 // 1=入向手柄, 2=出向手柄, 0=移动锚点本体

        // 右键拖动判定
        Point _rightDownPos;
        bool _rightMoved = false;

        // 导出面板状态
        string _exportFormat = "map";      // map | maptiles | schematic | png | geojson
        bool _exportScaleBar = true;
        bool _exportAutoText = true;
        double _exportFontScale = 1.0;     // 手动文字缩放
        double _exportLineScale = 1.0;     // 线路粗细缩放
        Image _exportPreviewImage;         // 导出面板预览图

        // PNG 导出高级选项
        int _exportPngW = 0;               // 输出宽度（0=自动按内容）
        int _exportPngH = 0;               // 输出高度（0=自动按内容）
        int _exportMapZoom = -1;           // 底图加载比例尺（-1=自动跟随当前 zoom）
        double _exportSchZoom = 1.0;       // 示意图 PNG 缩放等级（1=适配内容，越大站点间距/图片越大、字体相对越小）
        double _exportRes = 4.0;           // 渲染分辨率系数（默认 4 倍，提高清晰度；投影与元素尺寸同步放大，视觉不变）
        bool _exportShowText = true;       // 是否导出文字
        bool _exportShowLines = true;      // 是否导出线路
        bool _exportShowStations = true;   // 是否导出站点图标
        bool _exportShowBackground = true; // 是否导出背景/底图

        // 文字防遮挡：本次渲染已放置标签的屏幕包围盒（Render 开始时清空）
        readonly Dictionary<string, Rect> _placedLabels = new();
        // 本次渲染各站点的屏幕位置（用于计算线路方向，决定标签避让线路）
        readonly Dictionary<string, Point> _renderStationPos = new();

        // 项目
        string _projectName = "未命名项目";
        double _anchorLng = 0, _anchorLat = 0;
        string _anchorName = "";
        bool _constrained = true;
        double _maxDistKm = 60;
        double _mapSideKm = 40;
        bool _projectLoaded = false;
        List<string> _recent = new();

        bool _showCn = true, _showEn = true, _showThird = true;
        double _curveRadiusM = 30;

        // 示意图网格对齐阈值
        double _schGridSize = 40;       // 网格间距
        double _schSnapThreshold = 15;  // 若两站 x/y 差小于该值，视为同一网格线（自动对齐）
        bool _schGrid = true;           // 示意图自动对齐网格（默认开启）

        // ---------- 相机 ----------
        int _zoom = 12;
        double _centerLng = 116.40, _centerLat = 39.908;

        // ---------- 示意图视图 ----------
        double _schScale = 1, _schOffX = 0, _schOffY = 0;

        // ---------- 测距 / 交互 ----------
        List<(double lng, double lat)> _measure = new();
        bool _panning = false;
        bool _rightPanning = false;
        Point _lastMouse;
        string _dragStationId = null;
        bool _boxSelect = false;
        (double lng, double lat)? _boxStart, _boxCurrent;

        // 支线绘制
        string _branchJunction = null;
        List<string> _branchStations = new();

        // 延长模式：null=正常，head=从首端延长，tail=从末端延长
        string _extendMode = null;

        // 锚点拖拽
        string _dragAnchorSegKey = null;
        int _dragAnchorIdx = -1;

        // 撤销 / 重做
        List<ProjectData> _undoStack = new();
        List<ProjectData> _redoStack = new();

        // 示意图网格（沿用上面字段）
        bool _schShowMapBg = false;
        double _schMapBgOpacity = 0.35;

        // ---------- 瓦片 ----------
        TileService _tiles;
        Dictionary<string, BitmapImage> _tileCache = new();
        HashSet<string> _inflight = new();
        bool _offlineMode = false;

        // 工具图标工具栏缓存（ToggleButton -> tag）
        readonly Dictionary<string, ToggleButton> _toolButtons = new();

        static readonly (string Name, string Hex)[] Palette =
        {
            ("红","#e4002b"), ("蓝","#0f7dc2"), ("绿","#00a651"), ("橙","#f08300"),
            ("紫","#8a2be2"), ("青","#00b0b9"), ("棕","#c58c3e"), ("黄","#e6c700"),
            ("粉","#e14b8a"), ("灰","#5b6770"), ("深绿","#2c7a4d"), ("深红","#d1495b")
        };

        // 离线城市库（名称,经度,纬度），覆盖全国主要城市，搜索无需联网
        const string CitiesData = """
北京,116.407,39.904
上海,121.474,31.230
天津,117.200,39.125
重庆,106.551,29.563
石家庄,114.515,38.042
唐山,118.180,39.630
秦皇岛,119.600,39.935
邯郸,114.539,36.626
邢台,114.505,37.070
保定,115.465,38.874
张家口,114.886,40.768
承德,117.963,40.952
沧州,116.838,38.304
廊坊,116.704,39.523
衡水,115.669,37.739
太原,112.549,37.870
大同,113.300,40.076
阳泉,113.580,37.857
长治,113.116,36.195
晋城,112.851,35.491
朔州,112.433,39.331
晋中,112.752,37.687
运城,111.007,35.026
忻州,112.734,38.416
临汾,111.519,36.088
吕梁,111.144,37.519
呼和浩特,111.749,40.842
包头,109.840,40.657
乌海,106.794,39.655
赤峰,118.888,42.258
通辽,122.243,43.653
鄂尔多斯,109.781,39.608
呼伦贝尔,119.766,49.212
巴彦淖尔,107.387,40.743
乌兰察布,113.133,40.994
沈阳,123.432,41.806
大连,121.615,38.914
鞍山,122.996,41.108
抚顺,123.957,41.881
本溪,123.766,41.294
丹东,124.354,40.000
锦州,121.127,41.095
营口,122.235,40.667
阜新,121.670,42.022
辽阳,123.237,41.268
盘锦,122.071,41.120
铁岭,123.842,42.223
朝阳,120.451,41.576
葫芦岛,120.837,40.711
长春,125.324,43.817
吉林,126.549,43.838
四平,124.350,43.166
辽源,125.145,42.888
通化,125.940,41.728
白山,126.435,41.941
松原,124.825,45.141
白城,122.839,45.620
延吉,129.509,42.891
哈尔滨,126.535,45.803
齐齐哈尔,123.918,47.354
鸡西,130.969,45.295
鹤岗,130.298,47.350
双鸭山,131.159,46.647
大庆,125.103,46.589
伊春,128.841,47.728
佳木斯,130.318,46.800
七台河,131.003,45.771
牡丹江,129.633,44.552
黑河,127.528,50.245
绥化,126.969,46.653
南京,118.797,32.060
无锡,120.312,31.491
徐州,117.284,34.205
常州,119.974,31.811
苏州,120.585,31.299
南通,120.894,31.980
连云港,119.222,34.597
淮安,119.021,33.610
盐城,120.163,33.347
扬州,119.412,32.394
镇江,119.455,32.205
泰州,119.923,32.455
宿迁,118.275,33.963
杭州,120.155,30.274
宁波,121.550,29.875
温州,120.699,28.000
嘉兴,120.756,30.746
湖州,120.087,30.894
绍兴,120.580,30.030
金华,119.647,29.079
衢州,118.859,28.936
舟山,122.207,29.985
台州,121.421,28.656
丽水,119.923,28.468
合肥,117.227,31.820
芜湖,118.433,31.352
蚌埠,117.389,32.916
淮南,117.018,32.585
马鞍山,118.506,31.670
淮北,116.798,33.955
铜陵,117.812,30.945
安庆,117.063,30.543
黄山,118.337,29.715
滁州,118.317,32.302
阜阳,115.814,32.890
宿州,116.964,33.647
六安,116.523,31.735
亳州,115.779,33.845
池州,117.491,30.665
宣城,118.758,30.941
福州,119.296,26.074
厦门,118.089,24.480
莆田,119.008,25.454
三明,117.639,26.264
泉州,118.676,24.874
漳州,117.647,24.513
南平,118.178,26.642
龙岩,117.017,25.075
宁德,119.548,26.666
南昌,115.858,28.682
景德镇,117.178,29.269
萍乡,113.854,27.623
九江,115.989,29.705
新余,114.917,27.818
鹰潭,117.069,28.260
赣州,114.935,25.831
吉安,114.966,27.114
宜春,114.416,27.816
抚州,116.358,27.949
上饶,117.943,28.455
济南,117.120,36.651
青岛,120.383,36.067
淄博,118.055,36.813
枣庄,117.323,34.810
东营,118.675,37.434
烟台,121.447,37.464
潍坊,119.162,36.707
济宁,116.587,35.415
泰安,117.088,36.200
威海,122.120,37.513
日照,119.527,35.416
临沂,118.356,35.105
德州,116.357,37.436
聊城,115.985,36.457
滨州,117.971,37.382
菏泽,115.481,35.234
郑州,113.625,34.746
开封,114.307,34.797
洛阳,112.454,34.620
平顶山,113.193,33.766
安阳,114.392,36.098
鹤壁,114.297,35.748
新乡,113.927,35.303
焦作,113.242,35.216
濮阳,115.029,35.762
许昌,113.852,34.036
漯河,114.016,33.581
三门峡,111.200,34.772
南阳,112.528,32.991
商丘,115.656,34.414
信阳,114.091,32.147
周口,114.697,33.626
驻马店,114.022,33.011
武汉,114.305,30.593
黄石,115.039,30.200
十堰,110.798,32.629
宜昌,111.286,30.692
襄阳,112.122,32.009
鄂州,114.895,30.391
荆门,112.199,31.035
孝感,113.916,30.925
荆州,112.240,30.335
黄冈,114.872,30.454
咸宁,114.322,29.841
随州,113.382,31.690
恩施,109.488,30.272
长沙,112.939,28.228
株洲,113.134,27.828
湘潭,112.944,27.830
衡阳,112.572,26.893
邵阳,111.468,27.239
岳阳,113.129,29.357
常德,111.699,29.032
张家界,110.479,29.117
益阳,112.355,28.554
郴州,113.015,25.771
永州,111.613,26.420
怀化,110.002,27.569
娄底,112.005,27.728
广州,113.264,23.129
韶关,113.597,24.810
深圳,114.058,22.543
珠海,113.577,22.271
汕头,116.682,23.354
佛山,113.122,23.022
江门,113.082,22.579
湛江,110.359,21.271
茂名,110.925,21.663
肇庆,112.465,23.047
惠州,114.416,23.112
梅州,116.122,24.288
汕尾,115.375,22.787
河源,114.700,23.744
阳江,111.983,21.858
清远,113.056,23.682
东莞,113.752,23.021
中山,113.392,22.517
潮州,116.623,23.657
揭阳,116.373,23.550
云浮,112.044,22.915
南宁,108.366,22.817
柳州,109.416,24.326
桂林,110.290,25.274
梧州,111.279,23.477
北海,109.120,21.481
防城港,108.353,21.687
钦州,108.654,21.981
贵港,109.599,23.112
玉林,110.181,22.654
百色,106.618,23.902
贺州,111.567,24.404
河池,108.085,24.693
来宾,109.221,23.750
崇左,107.365,22.377
海口,110.199,20.044
三亚,109.512,18.253
儋州,109.581,19.521
成都,104.066,30.573
自贡,104.778,29.339
攀枝花,101.718,26.582
泸州,105.442,28.871
德阳,104.398,31.127
绵阳,104.679,31.468
广元,105.843,32.435
遂宁,105.593,30.533
内江,105.058,29.580
乐山,103.766,29.552
南充,106.111,30.837
眉山,103.848,30.075
宜宾,104.643,28.752
广安,106.633,30.456
达州,107.468,31.209
雅安,103.042,30.010
巴中,106.747,31.868
资阳,104.627,30.129
贵阳,106.630,26.647
六盘水,104.830,26.594
遵义,106.937,27.726
安顺,105.928,26.246
毕节,105.284,27.302
铜仁,109.189,27.731
昆明,102.833,24.880
曲靖,103.796,25.490
玉溪,102.547,24.352
保山,99.161,25.112
昭通,103.717,27.338
丽江,100.227,26.855
普洱,100.966,22.825
临沧,100.088,23.884
拉萨,91.114,29.645
日喀则,88.885,29.267
昌都,97.172,31.141
林芝,94.362,29.649
山南,91.773,29.237
西安,108.940,34.341
铜川,108.945,34.897
宝鸡,107.144,34.361
咸阳,108.709,34.330
渭南,109.510,34.499
延安,109.494,36.585
汉中,107.024,33.067
榆林,109.735,38.285
安康,109.029,32.685
商洛,109.918,33.873
兰州,103.834,36.061
嘉峪关,98.289,39.772
金昌,102.188,38.520
白银,104.138,36.545
天水,105.725,34.581
武威,102.638,37.928
张掖,100.450,38.926
平凉,106.665,35.543
酒泉,98.494,39.733
庆阳,107.643,35.709
定西,104.626,35.581
陇南,104.921,33.401
西宁,101.778,36.617
海东,102.104,36.502
银川,106.232,38.487
石嘴山,106.384,39.020
吴忠,106.199,37.998
固原,106.242,36.016
中卫,105.189,37.515
乌鲁木齐,87.617,43.793
克拉玛依,84.869,45.599
吐鲁番,89.184,42.947
哈密,93.515,42.819
昌吉,87.267,44.014
库尔勒,86.145,41.766
阿克苏,80.263,41.168
喀什,75.989,39.470
和田,79.922,37.114
伊宁,81.277,43.908
""";

        List<(string Name, double Lng, double Lat)> _cityList = new();

        public MainWindow()
        {
            InitializeComponent();
            _tiles = new TileService("osm", "https://tile.openstreetmap.org/{z}/{x}/{y}.png");
            InitBasemap();
            InitCityPreset();
            InitToolBar();
            LoadRecent();
            PreviewKeyDown += (s, e) =>
            {
                bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
                if (ctrl && e.Key == Key.S) { e.Handled = true; SaveProject(); return; }
                if (ctrl && e.Key == Key.Z) { e.Handled = true; Undo(); return; }
                if (ctrl && e.Key == Key.Y) { e.Handled = true; Redo(); return; }
                if (ctrl && e.Key == Key.O) { e.Handled = true; OpenProject(); return; }
                if (ctrl && e.Key == Key.N) { e.Handled = true; NewProject(); return; }
                if (ctrl && e.Key == Key.E) { e.Handled = true; ExportBtn_Click(s, e); return; }
                if (e.Key == Key.Escape)
                {
                    _measure.Clear();
                    if (_boxSelect) { _boxSelect = false; _boxStart = null; _boxCurrent = null; MapCanvas.Cursor = Cursors.Arrow; }
                    Render();
                }
            };
            SetTool("select");
            UpdateOfflineCount();
            bool hasSession = LoadAutoSave();
            UpdateProjectUI();
            UpdateTitle();
            UpdateWelcomeVisibility();
            WelcomeContinueBtn.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
            Render();
            Loaded += (s, e) => Render();
            MapCanvas.SizeChanged += (s, e) => Render();
        }

        // ================= 初始化 =================
        void InitBasemap()
        {
            BasemapCombo.Items.Clear();
            BasemapCombo.Items.Add(new ComboBoxItem { Content = "OpenStreetMap（栅格）", Tag = "osm" });
            BasemapCombo.Items.Add(new ComboBoxItem { Content = "卫星影像（Esri）", Tag = "sat" });
            BasemapCombo.SelectedIndex = 0;
        }
        void InitCityPreset()
        {
            _cityList.Clear();
            foreach (var line in CitiesData.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                var parts = t.Split(',');
                if (parts.Length >= 3 && double.TryParse(parts[1], out var lng) && double.TryParse(parts[2], out var lat))
                    _cityList.Add((parts[0], lng, lat));
            }
            CityPresetCombo.Items.Clear();
            CityPresetCombo.Items.Add(new ComboBoxItem { Content = "— 常用城市（离线可用）—", Tag = "" });
            foreach (var c in _cityList)
                CityPresetCombo.Items.Add(new ComboBoxItem { Content = c.Name, Tag = $"{c.Lng},{c.Lat},11" });
            CityPresetCombo.SelectedIndex = 0;
        }

        // ================= 图标工具条 =================
        // 工具定义：key / 图标 / 名称
        static readonly (string Key, string Icon, string Name)[] ToolDefs =
        {
            ("select",      "🖱️", "选择"),
            ("station",     "Ⓢ",  "设站"),
            ("drawLine",    "✏️", "线路"),
            ("lines",       "🚇", "线路管理"),
            ("design",      "🎨", "设计"),
            ("export",      "⬇️", "导出"),
            ("project",     "⚙️", "设置"),
            ("offline",     "🗺️", "离线地图"),
        };

        void InitToolBar()
        {
            ToolBarStack.Children.Clear();
            _toolButtons.Clear();
            foreach (var td in ToolDefs)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                var btn = new ToggleButton
                {
                    Tag = td.Key,
                    Width = 44, Height = 44,
                    ToolTip = td.Name,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                var inner = new StackPanel { IsHitTestVisible = false };
                inner.Children.Add(new TextBlock { Text = td.Icon, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 24 });
                inner.Children.Add(new TextBlock { Text = td.Name, FontSize = 9, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
                btn.Content = inner;
                btn.Click += Tool_Click;
                sp.Children.Add(btn);
                ToolBarStack.Children.Add(sp);
                _toolButtons[td.Key] = btn;
            }
        }

        void UpdateToolBarSelection()
        {
            foreach (var kv in _toolButtons)
                kv.Value.IsChecked = kv.Key == _tool;
        }

        // ================= 工具函数 =================
        Color ParseColor(string hex)
        {
            hex = (hex ?? "").TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromRgb(r, g, b);
            }
            return Colors.Red;
        }
        string FormatDist(double m) => m >= 1000 ? (m / 1000).ToString("F2") + " km" : Math.Round(m).ToString() + " m";

        Point GeoToScreen(double lng, double lat)
        {
            double wx = MapMath.LngToWorldX(lng, _zoom), wy = MapMath.LatToWorldY(lat, _zoom);
            double cx = MapMath.LngToWorldX(_centerLng, _zoom), cy = MapMath.LatToWorldY(_centerLat, _zoom);
            return new Point((wx - cx) + MapCanvas.ActualWidth / 2, (wy - cy) + MapCanvas.ActualHeight / 2);
        }
        (double lng, double lat) ScreenToGeo(Point p)
        {
            double cx = MapMath.LngToWorldX(_centerLng, _zoom), cy = MapMath.LatToWorldY(_centerLat, _zoom);
            double wx = (p.X - MapCanvas.ActualWidth / 2) + cx;
            double wy = (p.Y - MapCanvas.ActualHeight / 2) + cy;
            return (MapMath.WorldXToLng(wx, _zoom), MapMath.WorldYToLat(wy, _zoom));
        }
        Point SchToScreen(Point w) => new Point(w.X * _schScale + _schOffX, w.Y * _schScale + _schOffY);
        Point ScreenToSch(Point s) => new Point((s.X - _schOffX) / _schScale, (s.Y - _schOffY) / _schScale);
        double RadiusPx() => _curveRadiusM / MapMath.MetersPerPixel(_centerLat, _zoom);

        void SetStatus(string msg) => StatusText.Text = msg;

        // ================= 工具切换 + 专属面板 =================
        void SetTool(string tool)
        {
            _tool = tool;
            UpdateToolBarSelection();

            // 清除非本工具的状态
            if (tool != "drawLine") _extendMode = null;
            if (tool != "drawLine") { _branchJunction = null; _branchStations = new(); _drawSubMode = "main"; }
            if (tool != "station") _measureActive = false;

            ShowToolPanel(tool);

            var hints = new Dictionary<string, string>
            {
                ["select"] = "当前工具：选择 — 点击/拖动站点，右键拖动平移地图",
                ["station"] = "当前工具：设站 — 点击地图放置站点；面板可测距、管理站点",
                ["drawLine"] = "当前工具：线路 — 依次点击站点连成线路；面板内含支线/锚点",
                ["lines"] = "线路管理 — 新建、命名、样式、分段与支线",
                ["export"] = "导出 — 右侧实时预览导出效果",
                ["project"] = "项目设置 — 名称、城市范围与地图加载区域",
                ["offline"] = "离线地图 — 下载指定区域瓦片供断网使用"
            };
            SetStatus(hints.TryGetValue(tool, out var h) ? h : tool);
        }

        // 构建当前工具对应的专属面板
        void ShowToolPanel(string tool)
        {
            ToolPanelHost.Children.Clear();
            switch (tool)
            {
                case "station": BuildStationToolPanel(); break;
                case "drawLine": BuildLineToolPanel(); break;
                case "lines": BuildLinesPanel(); break;
                case "design": BuildDesignPanel(); break;
                case "export": BuildExportPanel(); break;
                case "project": BuildProjectPanel(); break;
                case "offline": BuildOfflinePanel(); break;
                case "select":
                default: BuildSelectPanel(); break;
            }
        }

        // ---- 通用小部件 ----
        void AddPanelTitle(string text) =>
            ToolPanelHost.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), Margin = new Thickness(0, 0, 0, 6) });
        void AddHint(string text) =>
            ToolPanelHost.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        void AddLabel(string text) =>
            ToolPanelHost.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 2) });
        Button AddButton(string text, Action onClick, bool fullWidth = true)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 3, 0, 0) };
            if (fullWidth) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Click += (s, e) => onClick();
            ToolPanelHost.Children.Add(b);
            return b;
        }
        void AddSeparator() => ToolPanelHost.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });

        // ---- 选择面板 ----
        void BuildSelectPanel()
        {
            ToolPanelHost.Children.Clear();
            AddPanelTitle("选择 / 浏览");
            AddHint("点击站点选中查看详情；拖动站点调整位置；右键拖动平移地图；右键空白处快速建站。");
            // 站点信息
            AddLabel("所选站点");
            var infoBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)), CornerRadius = new CornerRadius(6), Padding = new Thickness(8) };
            var info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = GetStationInfoText() };
            infoBorder.Child = info;
            ToolPanelHost.Children.Add(infoBorder);
            _selectInfoText = info;
            // 视图
            AddSeparator();
            AddPanelTitle("视图");
            AddButton("适应窗口", () => { if (_mode == "schematic") FitSchematic(); else FitToStations(); Render(); });
            AddButton("自动布局示意图", AutoArrange);
            AddButton("一键拉直优化（消除曲折）", OptimizeSchematicStraighten);
            AddButton("重置示意图为真实走向", () => { SeedSchematicFromGeo(true); FitSchematic(); Render(); SaveState(); });
            // 显示设置
            AddSeparator();
            AddPanelTitle("显示设置");
            AddDisplaySettings();
        }
        TextBlock _selectInfoText;

        string GetStationInfoText()
        {
            if (_selectedStationId == null || !_stations.TryGetValue(_selectedStationId, out var s))
                return "点击地图上的站点查看详情。";
            var lineNames = s.Lines.Count == 0 ? "（无）" : string.Join("、", s.Lines.Where(l => _lines.ContainsKey(l)).Select(l => _lines[l].Name));
            string info = $"{s.NameCn}{(s.IsTransfer ? "  [换乘站]" : "")}\n英文：{(string.IsNullOrEmpty(s.NameEn) ? "—" : s.NameEn)}";
            if (!string.IsNullOrEmpty(s.NameThird)) info += $"\n{(string.IsNullOrEmpty(s.ThirdLabel) ? "第三语" : s.ThirdLabel)}：{s.NameThird}";
            info += $"\n坐标：{s.Lng:F5}, {s.Lat:F5}\n所属线路：{lineNames}";
            return info;
        }

        // ---- 设站工具面板（整合 普通站/换乘站/测距/站点列表）----
        void BuildStationToolPanel()
        {
            ToolPanelHost.Children.Clear();
            AddPanelTitle("设站");
            AddHint("点击地图放置站点，随后弹出属性对话框；下方列表可双击编辑或管理全部站点。");

            // 放置类型
            AddLabel("放置类型");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var normal = new ToggleButton { Content = "普通站", IsChecked = !_placeTransfer, Width = 80, Margin = new Thickness(0, 0, 4, 0) };
            var tr = new ToggleButton { Content = "换乘站", IsChecked = _placeTransfer, Width = 80 };
            normal.Click += (s, e) => { _placeTransfer = false; tr.IsChecked = false; SetStatus("设站：点击地图放置普通站"); };
            tr.Click += (s, e) => { _placeTransfer = true; normal.IsChecked = false; SetStatus("设站：点击地图放置换乘站"); };
            row.Children.Add(normal); row.Children.Add(tr);
            ToolPanelHost.Children.Add(row);

            // 测距（作为子功能）
            AddSeparator();
            var measureToggle = new ToggleButton { Content = _measureActive ? "📏 测距中…（点击完成/清除）" : "📏 测距", IsChecked = _measureActive, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 3, 0, 0) };
            measureToggle.Click += (s, e) =>
            {
                _measureActive = !_measureActive;
                SetStatus(_measureActive ? "测距模式：点击地图累计测量距离（Esc 或再次点击按钮退出）" : "已退出测距");
                BuildStationToolPanel(); Render();
            };
            ToolPanelHost.Children.Add(measureToggle);
            if (_measure.Count >= 2)
            {
                double total = 0;
                for (int i = 0; i < _measure.Count - 1; i++)
                    total += MapMath.Haversine(_measure[i].lng, _measure[i].lat, _measure[i + 1].lng, _measure[i + 1].lat);
                ToolPanelHost.Children.Add(new TextBlock { Text = "总距离", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 2) });
                ToolPanelHost.Children.Add(new TextBlock { Text = FormatDist(total), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xe4, 0, 0x2b)) });
                ToolPanelHost.Children.Add(new TextBlock { Text = $"{_measure.Count} 个点", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) });
            }
            if (_measure.Count > 0)
                AddButton("清除测距", () => { _measure.Clear(); _measureActive = false; BuildStationToolPanel(); Render(); });

            // 站点列表（可排序：拼音/创建时间/名称，正序/倒序）
            AddSeparator();
            AddPanelTitle($"站点列表（{_stations.Count}）");
            // 排序工具栏
            var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var sortCombo = new ComboBox { Width = 110 };
            sortCombo.Items.Add(new ComboBoxItem { Content = "按拼音", Tag = "pinyin" });
            sortCombo.Items.Add(new ComboBoxItem { Content = "按创建时间", Tag = "time" });
            sortCombo.Items.Add(new ComboBoxItem { Content = "按名称", Tag = "name" });
            sortCombo.SelectedIndex = Math.Max(0, sortCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == _stationSort));
            sortCombo.SelectionChanged += (s, e) => { if (sortCombo.SelectedItem is ComboBoxItem it && it.Tag is string t) { _stationSort = t; BuildStationToolPanel(); } };
            sortRow.Children.Add(sortCombo);
            var ascBtn = new ToggleButton { Content = _stationSortAsc ? "↑ 正序" : "↓ 倒序", IsChecked = _stationSortAsc, Width = 72, Margin = new Thickness(4, 0, 0, 0) };
            ascBtn.Click += (s, e) => { _stationSortAsc = !_stationSortAsc; BuildStationToolPanel(); };
            sortRow.Children.Add(ascBtn);
            ToolPanelHost.Children.Add(sortRow);

            var list = new ListBox { MaxHeight = 200, Margin = new Thickness(0, 4, 0, 0) };
            var sorted = SortStations(_stations.Values);
            foreach (var s in sorted)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                sp.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x1f, 0x27, 0x33)), StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center });
                if (s.IsTransfer) sp.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(0x1f, 0x27, 0x33)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-10, 0, 0, 0) });
                sp.Children.Add(new TextBlock { Text = s.NameCn, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = s.Id == _selectedStationId ? FontWeights.Bold : FontWeights.Normal });
                if (!string.IsNullOrEmpty(s.NameEn))
                    sp.Children.Add(new TextBlock { Text = "  " + s.NameEn, Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                var item = new ListBoxItem { Content = sp, Tag = s.Id };
                if (s.Id == _selectedStationId) item.IsSelected = true;
                list.Items.Add(item);
            }
            list.SelectionChanged += (s, e) => { if (list.SelectedItem is ListBoxItem it && it.Tag is string id) { SelectStation(id); } };
            list.MouseDoubleClick += (s, e) => { if (list.SelectedItem is ListBoxItem it && it.Tag is string id) EditStation(id); };
            ToolPanelHost.Children.Add(list);

            // 命名辅助提示
            AddSeparator();
            AddPanelTitle("命名辅助");
            AddHint("在站点属性对话框中，可自动生成英文名（拼音）与第三语言（注音），并手动标注声调。");
            AddLabel("显示语言");
            AddDisplayCheckboxes();
        }

        // 按当前排序设置返回站点列表
        List<Station> SortStations(IEnumerable<Station> src)
        {
            var list = src.ToList();
            IEnumerable<Station> q = _stationSort switch
            {
                "time" => list.OrderBy(s => s.CreatedAt),
                "name" => list.OrderBy(s => s.NameCn, StringComparer.Ordinal),
                _ => list.OrderBy(s => PinyinHelper.ToPinyinKey(s.NameCn), StringComparer.Ordinal).ThenBy(s => s.NameCn, StringComparer.Ordinal)
            };
            if (!_stationSortAsc) q = q.Reverse();
            return q.ToList();
        }

        // 按当前排序设置返回线路列表
        List<Line> SortLines(IEnumerable<Line> src)
        {
            var list = src.ToList();
            IEnumerable<Line> q = _lineSort switch
            {
                "time" => list.OrderBy(l => l.CreatedAt),
                _ => list.OrderBy(l => l.Name, StringComparer.Ordinal)
            };
            if (!_lineSortAsc) q = q.Reverse();
            return q.ToList();
        }

        // ---- 线路绘制面板（整合 主线/支线/锚点）----
        void BuildLineToolPanel()
        {
            ToolPanelHost.Children.Clear();
            AddPanelTitle("线路绘制");
            // 子模式切换（主线 / 支线 / 锚点）
            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var mainBtn = new ToggleButton { Content = "主线", IsChecked = _drawSubMode == "main", Width = 60, Margin = new Thickness(0, 0, 4, 0) };
            var branchBtn = new ToggleButton { Content = "支线", IsChecked = _drawSubMode == "branch", Width = 60, Margin = new Thickness(0, 0, 4, 0) };
            var anchorBtn = new ToggleButton { Content = "锚点", IsChecked = _drawSubMode == "anchor", Width = 60 };
            mainBtn.Click += (s, e) => { _drawSubMode = "main"; BuildLineToolPanel(); SetStatus("主线模式：依次点击站点连成线路"); };
            branchBtn.Click += (s, e) =>
            {
                if (_activeLineId == null) { SetStatus("请先选中/新建一条线路"); return; }
                _drawSubMode = "branch"; _branchJunction = null; _branchStations = new();
                BuildLineToolPanel(); SetStatus("支线模式：先点分叉站，再点支线站点");
            };
            anchorBtn.Click += (s, e) => { _drawSubMode = "anchor"; BuildLineToolPanel(); SetStatus("锚点模式：在线段上点击添加钢笔式锚点"); };
            modeRow.Children.Add(mainBtn); modeRow.Children.Add(branchBtn); modeRow.Children.Add(anchorBtn);
            ToolPanelHost.Children.Add(modeRow);

            if (_drawSubMode == "branch")
            {
                AddHint("先点击本线路上某站作为分叉点，再依次点击支线站点，最后点「完成支线」。");
                var lineName = _activeLineId != null && _lines.TryGetValue(_activeLineId, out var bl) ? bl.Name : "（未选中线路）";
                ToolPanelHost.Children.Add(new TextBlock { Text = "分叉自：" + lineName, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
                if (_branchJunction != null)
                    ToolPanelHost.Children.Add(new TextBlock { Text = "分叉站：" + (_stations.TryGetValue(_branchJunction, out var jn) ? jn.NameCn : "?") + "\n支线站点：" + string.Join("→", _branchStations.Select(sid => _stations.TryGetValue(sid, out var st) ? st.NameCn : "?")), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x0f, 0x7d, 0xc2)), Margin = new Thickness(0, 0, 0, 6) });
                AddButton("完成支线", () => { FinishBranch(); _drawSubMode = "main"; BuildLineToolPanel(); });
                return;
            }
            if (_drawSubMode == "anchor")
            {
                AddHint("钢笔式锚点：在线段上点击可添加一个带手柄的锚点，通过拖动手柄绘制贝塞尔曲线；拖动锚点本体可移动位置。");
                AddButton("返回主线绘制", () => { _drawSubMode = "main"; BuildLineToolPanel(); });
                AddSeparator();
                AddPanelTitle("锚点操作");
                AddHint("· 在线段上点击 → 添加锚点\n· 拖动锚点本体 → 移动\n· 拖动两侧手柄 → 调整曲线\n· 右键线段 → 移除最近锚点");
                return;
            }

            // 主线模式
            AddHint("依次点击已有站点连成线路（至少两站才能成为有效地铁线）；两站之间可添加锚点实现绕行。");
            AddLabel("新路段类型");
            var segCombo = new ComboBox();
            segCombo.Items.Add(new ComboBoxItem { Content = "地下段 · 实线", Tag = "underground" });
            segCombo.Items.Add(new ComboBoxItem { Content = "切换段 · 点线", Tag = "transition" });
            segCombo.Items.Add(new ComboBoxItem { Content = "地上段 · 虚线", Tag = "ground" });
            segCombo.SelectedIndex = segCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == _segTypeDraw);
            if (segCombo.SelectedIndex < 0) segCombo.SelectedIndex = 0;
            segCombo.SelectionChanged += (s, e) => { if (segCombo.SelectedItem is ComboBoxItem it && it.Tag is string t) _segTypeDraw = t; };
            ToolPanelHost.Children.Add(segCombo);

            AddSeparator();
            AddPanelTitle("当前线路");
            var lineName2 = _activeLineId != null && _lines.TryGetValue(_activeLineId, out var l) ? l.Name : "（未创建）";
            var lineInfoRow = new StackPanel { Orientation = Orientation.Horizontal };
            lineInfoRow.Children.Add(new TextBlock { Text = lineName2, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            if (_activeLineId != null && _lines.TryGetValue(_activeLineId, out var lc))
            {
                lineInfoRow.Children.Add(new TextBlock { Text = $"  · {lc.StationOrder.Count} 站", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                lineInfoRow.Children.Add(new TextBlock { Text = lc.StationOrder.Count >= 2 ? "  ✓ 有效线路" : "  ⚠ 不足两站", Foreground = lc.StationOrder.Count >= 2 ? new SolidColorBrush(Color.FromRgb(0x00, 0xa6, 0x51)) : Brushes.Orange, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            }
            ToolPanelHost.Children.Add(lineInfoRow);

            var extRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var extHead = new Button { Content = "◀ 延长首端", FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            extHead.Click += (s, e) => StartExtend("head");
            var extTail = new Button { Content = "延长末端 ▶", FontSize = 11 };
            extTail.Click += (s, e) => StartExtend("tail");
            extRow.Children.Add(extHead); extRow.Children.Add(extTail);
            ToolPanelHost.Children.Add(extRow);

            AddButton("撤销上一站", UndoDraw);
            AddButton("完成绘制", () => { _extendMode = null; _drawSubMode = "main"; SetTool("select"); });
        }

        // ---- 锚点工具面板（已并入线路绘制，保留兼容）----
        void BuildAnchorToolPanel()
        {
            AddPanelTitle("锚点工具");
            AddHint("在线段上右键，选择「在此处添加锚点」，即可让线路绕行经过该点；拖动线段上的白色手柄可微调锚点位置。");
            AddButton("返回线路绘制", () => SetTool("drawLine"));
            AddSeparator();
            AddPanelTitle("说明");
            AddHint("锚点会作为线路走向的中间节点。删除锚点：右键线段 →「移除最近的锚点」。");
        }

        // ---- 测距面板（已并入设站，保留兼容）----
        void BuildMeasurePanel()
        {
            AddPanelTitle("测距");
            AddHint("点击地图多个位置累计测量距离；按 Esc 清除。");
            if (_measure.Count >= 2)
            {
                double total = 0;
                for (int i = 0; i < _measure.Count - 1; i++)
                    total += MapMath.Haversine(_measure[i].lng, _measure[i].lat, _measure[i + 1].lng, _measure[i + 1].lat);
                ToolPanelHost.Children.Add(new TextBlock { Text = "总距离", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 2) });
                ToolPanelHost.Children.Add(new TextBlock { Text = FormatDist(total), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xe4, 0, 0x2b)) });
                ToolPanelHost.Children.Add(new TextBlock { Text = $"{_measure.Count} 个点", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) });
            }
            AddButton("清除测距", () => { _measure.Clear(); ShowToolPanel("measure"); Render(); });
        }

        // ---- 导出工具面板（实时预览 + 参数真正生效）----
        void BuildExportPanel()
        {
            ToolPanelHost.Children.Clear();
            AddPanelTitle("导出");
            if (_stations.Count == 0)
            {
                AddHint("暂无站点数据，请先添加站点。");
                return;
            }

            // 格式选择
            AddLabel("格式");
            var fmtCombo = new ComboBox();
            fmtCombo.Items.Add(new ComboBoxItem { Content = "线路图 SVG（不含底图）", Tag = "map" });
            fmtCombo.Items.Add(new ComboBoxItem { Content = "线路图 SVG（嵌入底图）", Tag = "maptiles" });
            fmtCombo.Items.Add(new ComboBoxItem { Content = "示意图 SVG", Tag = "schematic" });
            fmtCombo.Items.Add(new ComboBoxItem { Content = "示意图 PNG", Tag = "schematicpng" });
            fmtCombo.Items.Add(new ComboBoxItem { Content = "PNG 图片", Tag = "png" });
            fmtCombo.Items.Add(new ComboBoxItem { Content = "PSD 分层文件（分组导入）", Tag = "psd" });
            fmtCombo.Items.Add(new ComboBoxItem { Content = "GeoJSON", Tag = "geojson" });
            fmtCombo.SelectedIndex = fmtCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == _exportFormat);
            if (fmtCombo.SelectedIndex < 0) fmtCombo.SelectedIndex = 0;
            fmtCombo.SelectionChanged += (s, e) =>
            {
                if (fmtCombo.SelectedItem is ComboBoxItem it && it.Tag is string t) { _exportFormat = t; BuildExportPanel(); }
            };
            ToolPanelHost.Children.Add(fmtCombo);

            // ---- 尺寸设置（PNG / PSD / 示意图PNG）----
            bool isPng = _exportFormat == "png";
            bool isPsd = _exportFormat == "psd";
            bool isSchPng = _exportFormat == "schematicpng";
            if (isPng || isPsd || isSchPng)
            {
                AddLabel("图片尺寸（像素）");
                var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
                var wTxt = new TextBox { Width = 70, Text = _exportPngW > 0 ? _exportPngW.ToString() : "" };
                var hTxt = new TextBox { Width = 70, Text = _exportPngH > 0 ? _exportPngH.ToString() : "", Margin = new Thickness(6, 0, 0, 0) };
                wTxt.TextChanged += (s, e) => { int.TryParse(wTxt.Text, out _exportPngW); };
                hTxt.TextChanged += (s, e) => { int.TryParse(hTxt.Text, out _exportPngH); };
                sizeRow.Children.Add(new TextBlock { Text = "宽", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11 });
                sizeRow.Children.Add(wTxt);
                sizeRow.Children.Add(new TextBlock { Text = "高", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(6, 0, 0, 0) });
                sizeRow.Children.Add(hTxt);
                ToolPanelHost.Children.Add(sizeRow);
                AddHint("留空 = 自动按内容最大尺寸；填一个 = 按比例缩放。");
            }

            // ---- PNG / 示意图 PNG 缩放倍率 ----
            if (isPng || isSchPng)
            {
                AddLabel("缩放倍率");
                var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
                var zTxt = new TextBox { Width = 70, Text = _exportSchZoom.ToString("0.##") };
                var applyBtn = new Button { Content = "应用", Width = 56, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1) };
                applyBtn.Click += (s, e) =>
                {
                    if (double.TryParse(zTxt.Text, out double v) && v > 0)
                    {
                        _exportSchZoom = Math.Round(Math.Clamp(v, 0.25, 32), 4);
                        zTxt.Text = _exportSchZoom.ToString("0.##");
                        RefreshExportPreview();
                    }
                    else
                    {
                        zTxt.Text = _exportSchZoom.ToString("0.##"); // 非法输入回退
                    }
                };
                zTxt.KeyDown += (s, e) => { if (e.Key == Key.Enter) { applyBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } };
                zoomRow.Children.Add(zTxt);
                zoomRow.Children.Add(applyBtn);
                zoomRow.Children.Add(new TextBlock { Text = " ×（大于 1 图片更大、元素相对更小）", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11 });
                ToolPanelHost.Children.Add(zoomRow);
                AddHint("缩放倍率放大图片尺寸与站点间距，线路、站点图标、字体保持像素大小 → 相对更小；全部线路仍完整居中输出。");
            }

            // ---- 底图比例尺（PNG / maptiles）----
            if (isPng || _exportFormat == "maptiles")
            {
                AddLabel("底图加载比例尺（zoom）");
                var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
                var zoomCombo = new ComboBox { Width = 90 };
                zoomCombo.Items.Add(new ComboBoxItem { Content = "自动", Tag = "-1" });
                for (int zz = 3; zz <= 19; zz++) zoomCombo.Items.Add(new ComboBoxItem { Content = zz.ToString(), Tag = zz.ToString() });
                zoomCombo.SelectedIndex = zoomCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == _exportMapZoom.ToString());
                if (zoomCombo.SelectedIndex < 0) zoomCombo.SelectedIndex = 0;
                zoomCombo.SelectionChanged += (s, e) =>
                {
                    if (zoomCombo.SelectedItem is ComboBoxItem it && int.TryParse((string)it.Tag, out int zv)) { _exportMapZoom = zv; RefreshExportPreview(); }
                };
                zoomRow.Children.Add(zoomCombo);
                zoomRow.Children.Add(new TextBlock { Text = " 值越大越精细", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11 });
                ToolPanelHost.Children.Add(zoomRow);
            }

            // ---- 内容开关（PNG / PSD / 示意图PNG）----
            if (isPng || isPsd || isSchPng)
            {
                AddLabel("图片内容");
                var c1 = new CheckBox { Content = "文字（站名标注）", IsChecked = _exportShowText };
                var c2 = new CheckBox { Content = "线路", IsChecked = _exportShowLines };
                var c3 = new CheckBox { Content = "站点图标", IsChecked = _exportShowStations };
                var c4 = new CheckBox { Content = "背景 / 底图", IsChecked = _exportShowBackground };
                c1.Checked += (s, e) => { _exportShowText = true; RefreshExportPreview(); };
                c1.Unchecked += (s, e) => { _exportShowText = false; RefreshExportPreview(); };
                c2.Checked += (s, e) => { _exportShowLines = true; RefreshExportPreview(); };
                c2.Unchecked += (s, e) => { _exportShowLines = false; RefreshExportPreview(); };
                c3.Checked += (s, e) => { _exportShowStations = true; RefreshExportPreview(); };
                c3.Unchecked += (s, e) => { _exportShowStations = false; RefreshExportPreview(); };
                c4.Checked += (s, e) => { _exportShowBackground = true; RefreshExportPreview(); };
                c4.Unchecked += (s, e) => { _exportShowBackground = false; RefreshExportPreview(); };
                ToolPanelHost.Children.Add(c1);
                ToolPanelHost.Children.Add(c2);
                ToolPanelHost.Children.Add(c3);
                ToolPanelHost.Children.Add(c4);
            }

            AddLabel("比例尺");
            var scaleBarChk = new CheckBox { Content = "在图上绘制比例尺", IsChecked = _exportScaleBar };
            scaleBarChk.Checked += (s, e) => { _exportScaleBar = true; RefreshExportPreview(); };
            scaleBarChk.Unchecked += (s, e) => { _exportScaleBar = false; RefreshExportPreview(); };
            ToolPanelHost.Children.Add(scaleBarChk);

            AddLabel("文字大小");
            var autoTextChk = new CheckBox { Content = "自动调整（防重叠）", IsChecked = _exportAutoText };
            autoTextChk.Checked += (s, e) => { _exportAutoText = true; RefreshExportPreview(); };
            autoTextChk.Unchecked += (s, e) => { _exportAutoText = false; RefreshExportPreview(); };
            ToolPanelHost.Children.Add(autoTextChk);
            var fontSlider = new Slider { Minimum = 0.5, Maximum = 2.5, Value = _exportFontScale, IsEnabled = !_exportAutoText };
            fontSlider.ValueChanged += (s, e) => { _exportFontScale = Math.Round(fontSlider.Value, 2); RefreshExportPreview(); };
            ToolPanelHost.Children.Add(fontSlider);
            var fontVal = new TextBlock { Text = _exportAutoText ? "自动" : $"{_exportFontScale:F1}×", Foreground = new SolidColorBrush(Color.FromRgb(0x0f, 0x7d, 0xc2)), FontSize = 11 };
            ToolPanelHost.Children.Add(fontVal);
            autoTextChk.Checked += (s, e) => fontVal.Text = "自动";
            autoTextChk.Unchecked += (s, e) => fontVal.Text = $"{_exportFontScale:F1}×";

            AddLabel("线路粗细");
            var lineSlider = new Slider { Minimum = 0.5, Maximum = 3, Value = _exportLineScale };
            lineSlider.ValueChanged += (s, e) => { _exportLineScale = Math.Round(lineSlider.Value, 2); RefreshExportPreview(); };
            ToolPanelHost.Children.Add(lineSlider);
            ToolPanelHost.Children.Add(new TextBlock { Text = $"{_exportLineScale:F1}×", Foreground = new SolidColorBrush(Color.FromRgb(0x0f, 0x7d, 0xc2)), FontSize = 11 });

            // 预览
            AddSeparator();
            AddPanelTitle("预览");
            var previewBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xe2, 0xe6, 0xec)), BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(0xf7, 0xf9, 0xfb)),
                Height = 220, ClipToBounds = true
            };
            var previewImage = new Image { Stretch = Stretch.Uniform };
            _exportPreviewImage = previewImage;
            previewBorder.Child = previewImage;
            ToolPanelHost.Children.Add(previewBorder);

            AddSeparator();
            var exportBtn = AddButton("导出到文件…", DoExport);
            exportBtn.Content = "导出到文件…";

            // 初始刷新预览
            RefreshExportPreview();
        }

        void RefreshExportPreview()
        {
            if (_exportPreviewImage == null) return;
            if (_exportFormat == "geojson" || _exportFormat == "psd")
            {
                if (_exportFormat == "psd")
                {
                    // PSD 预览：用 PNG 渲染路径展示合成效果
                    try { _exportPreviewImage.Source = RenderExportPreview(); }
                    catch { _exportPreviewImage.Source = null; }
                }
                else _exportPreviewImage.Source = null;
                return;
            }
            try
            {
                var bmp = RenderExportPreview();
                _exportPreviewImage.Source = bmp;
            }
            catch { _exportPreviewImage.Source = null; }
        }

        BitmapSource RenderExportPreview()
        {
            // 示意图与真实图用不同渲染路径，但都应用文字/线宽缩放
            if (_exportFormat == "schematic" || _exportFormat == "schematicpng")
                return RenderSchematicBitmap(_exportAutoText ? -1 : _exportFontScale, _exportLineScale);
            bool withTiles = _exportFormat == "png" || _exportFormat == "maptiles" || _exportFormat == "psd";
            return RenderFullExtentBitmap(withTiles, _exportAutoText ? -1 : _exportFontScale, _exportLineScale, _exportFormat == "maptiles");
        }

        void DoExport()
        {
            if (_exportFormat == "geojson")
            {
                var content = SvgExporter.BuildGeoJson(_stations, _lines);
                if (string.IsNullOrEmpty(content)) { SetStatus("无内容可导出"); return; }
                var dlg = new SaveFileDialog { FileName = "地铁线路.geojson", Filter = "GeoJSON (*.geojson)|*.geojson|所有文件 (*.*)|*.*" };
                if (dlg.ShowDialog() != true) return;
                File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
                SetStatus("已导出 GeoJSON");
                return;
            }
            if (_exportFormat == "schematicpng")
            {
                var bmp = RenderSchematicBitmap(_exportAutoText ? -1 : _exportFontScale, _exportLineScale);
                if (bmp == null) { SetStatus("无法渲染示意图"); return; }
                var dlg = new SaveFileDialog { FileName = "地铁示意图.png", Filter = "PNG 图片 (*.png)|*.png" };
                if (dlg.ShowDialog() != true) return;
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                using var pngFs = File.Create(dlg.FileName); enc.Save(pngFs);
                SetStatus("已导出示意图 PNG");
                return;
            }
            if (_exportFormat == "png")
            {
                var bmp = RenderFullExtentBitmap(true, _exportAutoText ? -1 : _exportFontScale, _exportLineScale, false);
                if (bmp == null) { SetStatus("无法渲染"); return; }
                var dlg = new SaveFileDialog { FileName = "地铁线路图.png", Filter = "PNG 图片 (*.png)|*.png" };
                if (dlg.ShowDialog() != true) return;
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                using var pngFs = File.Create(dlg.FileName); enc.Save(pngFs);
                SetStatus("已导出 PNG");
                return;
            }
            if (_exportFormat == "psd")
            {
                var dlg = new SaveFileDialog { FileName = "地铁线路图.psd", Filter = "PSD 文件 (*.psd)|*.psd" };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    PsdExporter.Export(_stations, _lines, _schematicPos, dlg.FileName,
                        _exportPngW, _exportPngH,
                        _showCn, _showEn, _showThird,
                        _exportAutoText ? -1 : _exportFontScale, _exportLineScale,
                        _exportShowText, _exportShowLines, _exportShowStations, _exportShowBackground,
                        _exportScaleBar, _curveRadiusM, _exportMapZoom > 0 ? _exportMapZoom : _zoom, _tiles);
                    SetStatus("已导出 PSD（分组分层）");
                }
                catch (Exception ex) { MessageBox.Show("PSD 导出失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
                return;
            }
            // SVG 格式（fontScale<0 表示自动防重叠）
            double fs = _exportAutoText ? -1.0 : _exportFontScale;
            string svg = _exportFormat switch
            {
                "map" => SvgExporter.BuildMapSvg(_stations, _lines, _showCn, _showEn, _showThird, _curveRadiusM, fs, _exportLineScale),
                "maptiles" => SvgExporter.BuildMapSvgWithTilesEx(_stations, _lines, _tiles, GetWest(), GetSouth(), GetEast(), GetNorth(), _exportMapZoom > 0 ? _exportMapZoom : _zoom,
                    _showCn, _showEn, _showThird, _curveRadiusM, fs, _exportLineScale),
                "schematic" => SvgExporter.BuildSchematicSvg(_stations, _lines, _schematicPos, _showCn, _showEn, _showThird, fs),
                _ => null
            };
            if (string.IsNullOrEmpty(svg)) { SetStatus("无内容可导出"); return; }
            var fn = _exportFormat == "schematic" ? "地铁示意图.svg" : "地铁线路图.svg";
            var dlg2 = new SaveFileDialog { FileName = fn, Filter = "SVG (*.svg)|*.svg" };
            if (dlg2.ShowDialog() != true) return;
            File.WriteAllText(dlg2.FileName, svg, Encoding.UTF8);
            SetStatus("已导出 SVG");
        }

        double GetWest() => _stations.Values.Min(s => s.Lng) - Math.Max((_stations.Values.Max(s => s.Lng) - _stations.Values.Min(s => s.Lng)) * 0.15, 0.01);
        double GetEast() => _stations.Values.Max(s => s.Lng) + Math.Max((_stations.Values.Max(s => s.Lng) - _stations.Values.Min(s => s.Lng)) * 0.15, 0.01);
        double GetSouth() => _stations.Values.Min(s => s.Lat) - Math.Max((_stations.Values.Max(s => s.Lat) - _stations.Values.Min(s => s.Lat)) * 0.15, 0.01);
        double GetNorth() => _stations.Values.Max(s => s.Lat) + Math.Max((_stations.Values.Max(s => s.Lat) - _stations.Values.Min(s => s.Lat)) * 0.15, 0.01);

        // ---- 线路管理面板（PS 组式：可展开/折叠，选中一条收回上一条）----
        void BuildLinesPanel()
        {
            ToolPanelHost.Children.Clear();
            AddPanelTitle("线路管理");
            AddButton("＋ 新建线路", () => { _activeLineId = CreateLine(); _selectedLineId = _activeLineId; BuildLinesPanel(); SaveState(); });

            // 排序工具栏
            var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
            var sortCombo = new ComboBox { Width = 110 };
            sortCombo.Items.Add(new ComboBoxItem { Content = "按名称", Tag = "name" });
            sortCombo.Items.Add(new ComboBoxItem { Content = "按创建时间", Tag = "time" });
            sortCombo.SelectedIndex = Math.Max(0, sortCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == _lineSort));
            sortCombo.SelectionChanged += (s, e) => { if (sortCombo.SelectedItem is ComboBoxItem it && it.Tag is string t) { _lineSort = t; BuildLinesPanel(); } };
            sortRow.Children.Add(sortCombo);
            var ascBtn = new ToggleButton { Content = _lineSortAsc ? "↑ 正序" : "↓ 倒序", IsChecked = _lineSortAsc, Width = 72, Margin = new Thickness(4, 0, 0, 0) };
            ascBtn.Click += (s, e) => { _lineSortAsc = !_lineSortAsc; BuildLinesPanel(); };
            sortRow.Children.Add(ascBtn);
            ToolPanelHost.Children.Add(sortRow);

            // 每条线路一个折叠组
            foreach (var line in SortLines(_lines.Values))
            {
                bool expanded = line.Id == _selectedLineId;
                var header = BuildLineHeader(line, expanded);
                ToolPanelHost.Children.Add(header);
                if (expanded && _lines.TryGetValue(line.Id, out _))
                {
                    var detailHost = new StackPanel { Margin = new Thickness(18, 0, 0, 6) };
                    BuildLineDetailInline(line, detailHost);
                    ToolPanelHost.Children.Add(detailHost);
                }
            }
        }

        // 线路组头（可点击展开/折叠，带颜色条、站数、有效状态）
        Border BuildLineHeader(Line line, bool expanded)
        {
            var border = new Border
            {
                Background = expanded ? new SolidColorBrush(Color.FromRgb(0xe8, 0xf0, 0xfb)) : new SolidColorBrush(Color.FromRgb(0xf5, 0xf7, 0xfa)),
                BorderBrush = expanded ? new SolidColorBrush(Color.FromRgb(0x0f, 0x7d, 0xc2)) : new SolidColorBrush(Color.FromRgb(0xe2, 0xe6, 0xec)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(6, 5, 6, 5),
                Cursor = Cursors.Hand, Tag = line.Id
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            // 折叠箭头
            var arrow = new TextBlock { Text = expanded ? "▼" : "▶", Foreground = new SolidColorBrush(Color.FromRgb(0x0f, 0x7d, 0xc2)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Width = 16 };
            row.Children.Add(arrow);
            // 颜色条
            row.Children.Add(new Rectangle { Width = 4, Height = 22, Fill = new SolidColorBrush(ParseColor(EffectiveLineColor(line))), RadiusX = 2, RadiusY = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            // 名称
            var nameTb = new TextBlock { Text = line.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(nameTb);
            // 有效状态标记
            bool valid = line.StationOrder.Count >= 2;
            row.Children.Add(new TextBlock { Text = valid ? "  ●" : "  ⚠", Foreground = valid ? new SolidColorBrush(ParseColor(EffectiveLineColor(line))) : Brushes.Orange, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, ToolTip = valid ? "有效线路" : "不足两站，尚不是有效地铁线" });
            // 站数
            row.Children.Add(new TextBlock { Text = $"  {line.StationOrder.Count}站", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = $" · {FormatDist(LineLength(line.Id))}", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            border.Child = row;

            border.MouseLeftButtonDown += (s, e) =>
            {
                // 点击同一条线：切换折叠；点击另一条线：展开它并收回上一条
                if (_selectedLineId == line.Id) _selectedLineId = null;
                else _selectedLineId = line.Id;
                _activeLineId = _selectedLineId;
                BuildLinesPanel(); Render();
            };
            return border;
        }

        void BuildLineDetailInline(Line line, StackPanel host)
        {
            void L(string text) => host.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 2) });
            void Add(UIElement el) => host.Children.Add(el);

            // 名称
            L("线路名称");
            var nameBox = new TextBox { Text = line.Name };
            nameBox.LostFocus += (s, e) => { line.Name = nameBox.Text; BuildLinesPanel(); SaveState(); };
            Add(nameBox);

            L("英文名（可选）");
            var enBox = new TextBox { Text = line.NameEn };
            enBox.LostFocus += (s, e) => { line.NameEn = enBox.Text; SaveState(); };
            Add(enBox);

            L("线路颜色");
            var colorCombo = new ComboBox();
            foreach (var c in Palette) colorCombo.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Hex });
            int ci = Array.FindIndex(Palette, p => p.Hex == line.Color);
            colorCombo.SelectedIndex = ci >= 0 ? ci : 0;
            colorCombo.SelectionChanged += (s, e) => { if (colorCombo.SelectedItem is ComboBoxItem it && it.Tag is string hex) { line.Color = hex; Render(); BuildLinesPanel(); SaveState(); } };
            Add(colorCombo);

            // 样式设置
            L("线路样式");
            BuildLineStyleControls(line, host);
            Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            var drawBtn = new Button { Content = "绘制这条线路", FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            drawBtn.Click += (s, e) => { _activeLineId = line.Id; SetTool("drawLine"); };
            btnRow.Children.Add(drawBtn);
            var branchBtn = new Button { Content = "＋ 支线", FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            branchBtn.Click += (s, e) => { _activeLineId = line.Id; _drawSubMode = "branch"; SetTool("drawLine"); };
            btnRow.Children.Add(branchBtn);
            var copyBtn = new Button { Content = "复制编号", FontSize = 11 };
            copyBtn.Click += (s, e) =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < line.StationOrder.Count; i++)
                    sb.AppendLine($"{StationNumber(line, -1, i)}\t{(_stations.TryGetValue(line.StationOrder[i], out var st) ? st.NameCn : "?")}");
                for (int b = 0; b < line.Branches.Count; b++)
                    for (int j = 0; j < line.Branches[b].StationOrder.Count; j++)
                        sb.AppendLine($"{StationNumber(line, b, j)}\t{(_stations.TryGetValue(line.Branches[b].StationOrder[j], out var st2) ? st2.NameCn : "?")}");
                Clipboard.SetText(sb.ToString());
                SetStatus("编号列表已复制到剪贴板");
            };
            btnRow.Children.Add(copyBtn);
            Add(btnRow);

            // 站点顺序
            L($"站点顺序（{line.StationOrder.Count} 站）");
            for (int i = 0; i < line.StationOrder.Count; i++)
            {
                var sid = line.StationOrder[i];
                var name = _stations.TryGetValue(sid, out var st) ? st.NameCn : "?";
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
                var num = StationNumber(line, -1, i);
                row.Children.Add(new TextBlock { Text = $"{num}  {name}", Width = 118, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = $"{num}  {name}" });
                var up = new Button { Content = "↑", Width = 26, Margin = new Thickness(2, 0, 0, 0) };
                var down = new Button { Content = "↓", Width = 26, Margin = new Thickness(2, 0, 0, 0) };
                var rm = new Button { Content = "✕", Width = 26, Margin = new Thickness(2, 0, 0, 0) };
                up.Click += (s, e) => MoveStationInLine(line.Id, sid, -1);
                down.Click += (s, e) => MoveStationInLine(line.Id, sid, 1);
                rm.Click += (s, e) => RemoveStationFromLine(line.Id, sid);
                row.Children.Add(up); row.Children.Add(down); row.Children.Add(rm);
                Add(row);
            }

            // 分段
            if (line.Segments.Count > 0)
            {
                L("分段（✂断开 / ＋插入 / 类型）");
                for (int si = 0; si < line.Segments.Count; si++)
                {
                    var seg = line.Segments[si];
                    int segIdx = si;
                    var an = _stations.TryGetValue(seg.A, out var sa) ? sa.NameCn : "?";
                    var bn = _stations.TryGetValue(seg.B, out var sb) ? sb.NameCn : "?";
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
                    row.Children.Add(new TextBlock { Text = $"{an}→{bn}", Width = 88, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11 });
                    var combo = new ComboBox { Width = 64 };
                    combo.Items.Add(new ComboBoxItem { Content = "地下", Tag = "underground" });
                    combo.Items.Add(new ComboBoxItem { Content = "切换", Tag = "transition" });
                    combo.Items.Add(new ComboBoxItem { Content = "地上", Tag = "ground" });
                    combo.SelectedIndex = seg.Type == "transition" ? 1 : seg.Type == "ground" ? 2 : 0;
                    combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is ComboBoxItem it && it.Tag is string t) { seg.Type = t; Render(); BuildLinesPanel(); SaveState(); } };
                    row.Children.Add(combo);
                    var cutBtn = new Button { Content = "✂", Width = 26, Margin = new Thickness(2, 0, 0, 0), ToolTip = "在此断开为两条线路" };
                    cutBtn.Click += (s, e) => SplitLineAtSegment(line.Id, segIdx);
                    row.Children.Add(cutBtn);
                    var insBtn = new Button { Content = "＋", Width = 26, Margin = new Thickness(2, 0, 0, 0), ToolTip = "在两站间插入站点" };
                    insBtn.Click += (s, e) => ShowInsertMenu(insBtn, line.Id, segIdx);
                    row.Children.Add(insBtn);
                    Add(row);
                }
            }

            // 支线
            if (line.Branches.Count > 0)
            {
                L("支线（Y形分叉）");
                for (int bi = 0; bi < line.Branches.Count; bi++)
                {
                    var br = line.Branches[bi];
                    int brIdx = bi;
                    var jn = _stations.TryGetValue(br.Junction, out var js) ? js.NameCn : "?";
                    var sts = string.Join("→", br.StationOrder.Select((id, j) => $"{StationNumber(line, brIdx, j)} {(_stations.TryGetValue(id, out var s) ? s.NameCn : "?")}"));
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
                    row.Children.Add(new TextBlock { Text = $"∟{jn} · {sts}", Width = 150, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, ToolTip = $"∟{jn} · {sts}" });
                    var rm = new Button { Content = "✕", Width = 26, Margin = new Thickness(2, 0, 0, 0), ToolTip = "删除支线" };
                    rm.Click += (s, e) => { line.Branches.RemoveAt(brIdx); Render(); BuildLinesPanel(); SaveState(); };
                    row.Children.Add(rm);
                    Add(row);
                }
            }

            var mergeBtn = new Button { Content = "合并线路…", Margin = new Thickness(0, 6, 0, 0) };
            mergeBtn.Click += (s, e) =>
            {
                var menu = new ContextMenu();
                foreach (var other in _lines.Values.Where(l => l.Id != line.Id))
                {
                    var mi = new MenuItem { Header = other.Name };
                    mi.Click += (s2, e2) => ConnectLines(line.Id, other.Id);
                    menu.Items.Add(mi);
                }
                if (menu.Items.Count == 0) { SetStatus("没有其它线路可合并"); return; }
                menu.PlacementTarget = mergeBtn; menu.IsOpen = true;
            };
            Add(mergeBtn);

            var del = new Button { Content = "删除线路", Margin = new Thickness(0, 6, 0, 0) };
            del.Click += (s, e) => DeleteLine(line.Id);
            Add(del);
        }

        // 线路样式控件（颜色/字体/字号/线宽/logo，支持分语言字体）
        void BuildLineStyleControls(Line line, StackPanel host)
        {
            void L(string text) => host.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 2) });

            // 精确线路颜色
            L("精确线路颜色");
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            var colorBox = new TextBox { Text = EffectiveLineColor(line), Width = 90, VerticalContentAlignment = VerticalAlignment.Center };
            colorBox.LostFocus += (s, e) =>
            {
                var t = colorBox.Text.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9a-fA-F]{6}$")) { line.LineColor = t; Render(); BuildLinesPanel(); SaveState(); }
                else if (t.Length == 0) { line.LineColor = ""; Render(); SaveState(); }
                else SetStatus("颜色格式应为 #RRGGBB");
            };
            var colorPickBtn = new Button { Content = "选择…", Width = 60, Margin = new Thickness(4, 0, 0, 0) };
            colorPickBtn.Click += (s, e) => ShowColorPicker(colorPickBtn, picked => { line.LineColor = picked; colorBox.Text = picked; Render(); BuildLinesPanel(); SaveState(); });
            colorRow.Children.Add(colorBox); colorRow.Children.Add(colorPickBtn);
            host.Children.Add(colorRow);

            // 分语言字体
            L("中文字体");
            host.Children.Add(BuildFontCombo(line.CnFont, f => { line.CnFont = f; Render(); SaveState(); }));
            L("英文字体");
            host.Children.Add(BuildFontCombo(line.EnFont, f => { line.EnFont = f; Render(); SaveState(); }));
            L("第三语言字体");
            host.Children.Add(BuildFontCombo(line.ThirdFont, f => { line.ThirdFont = f; Render(); SaveState(); }));

            // 字号 / 加粗 / 描边
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            sizeRow.Children.Add(new TextBlock { Text = "字号", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) });
            var sizeBox = new TextBox { Text = line.FontSize.ToString(), Width = 44 };
            sizeBox.LostFocus += (s, e) => { if (double.TryParse(sizeBox.Text, out var v)) { line.FontSize = Math.Max(6, v); Render(); SaveState(); } };
            sizeRow.Children.Add(sizeBox);
            var boldChk = new CheckBox { Content = "加粗", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), IsChecked = line.FontBold };
            boldChk.Checked += (s, e) => { line.FontBold = true; Render(); SaveState(); };
            boldChk.Unchecked += (s, e) => { line.FontBold = false; Render(); SaveState(); };
            sizeRow.Children.Add(boldChk);
            var outlineChk = new CheckBox { Content = "描边", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), IsChecked = line.FontOutline };
            outlineChk.Checked += (s, e) => { line.FontOutline = true; Render(); SaveState(); };
            outlineChk.Unchecked += (s, e) => { line.FontOutline = false; Render(); SaveState(); };
            sizeRow.Children.Add(outlineChk);
            host.Children.Add(sizeRow);

            // 线宽
            var widthRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            widthRow.Children.Add(new TextBlock { Text = "线宽", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) });
            var widthBox = new TextBox { Text = line.LineWidth.ToString(), Width = 44 };
            widthBox.LostFocus += (s, e) => { if (double.TryParse(widthBox.Text, out var v)) { line.LineWidth = Math.Max(1, v); Render(); SaveState(); } };
            widthRow.Children.Add(widthBox);
            var colinearChk = new CheckBox { Content = "共线并置", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), IsChecked = line.ColinearOffset, ToolTip = "与其他线路共线时上下/左右并置显示" };
            colinearChk.Checked += (s, e) => { line.ColinearOffset = true; Render(); SaveState(); };
            colinearChk.Unchecked += (s, e) => { line.ColinearOffset = false; Render(); SaveState(); };
            widthRow.Children.Add(colinearChk);
            host.Children.Add(widthRow);

            // logo
            L("线路 Logo");
            var logoRow = new StackPanel { Orientation = Orientation.Horizontal };
            var logoBox = new TextBox { Text = line.LogoText, Width = 70, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "如：M / 1 / 环" };
            logoBox.LostFocus += (s, e) => { line.LogoText = logoBox.Text.Trim(); Render(); SaveState(); };
            var logoColorBtn = new Button { Content = "颜色", Width = 50, Margin = new Thickness(4, 0, 0, 0) };
            logoColorBtn.Click += (s, e) => ShowColorPicker(logoColorBtn, picked => { line.LogoColor = picked; Render(); SaveState(); });
            var logoImportBtn = new Button { Content = "导入图…", Width = 64, Margin = new Thickness(4, 0, 0, 0) };
            logoImportBtn.Click += (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
                if (dlg.ShowDialog() == true) { line.LogoPath = dlg.FileName; Render(); SaveState(); SetStatus("已导入线路 Logo 图片"); }
            };
            logoRow.Children.Add(logoBox); logoRow.Children.Add(logoColorBtn); logoRow.Children.Add(logoImportBtn);
            host.Children.Add(logoRow);
            if (!string.IsNullOrEmpty(line.LogoPath))
            {
                var clearLogo = new Button { Content = "清除导入的 Logo", FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
                clearLogo.Click += (s, e) => { line.LogoPath = ""; Render(); SaveState(); };
                host.Children.Add(clearLogo);
            }

            var applyGlobal = new Button { Content = "应用到全部线路", Margin = new Thickness(0, 8, 0, 0) };
            applyGlobal.Click += (s, e) =>
            {
                foreach (var l in _lines.Values)
                {
                    l.CnFont = line.CnFont; l.EnFont = line.EnFont; l.ThirdFont = line.ThirdFont;
                    l.FontSize = line.FontSize; l.FontBold = line.FontBold; l.FontOutline = line.FontOutline; l.LineWidth = line.LineWidth;
                }
                Render(); SaveState(); SetStatus("样式已应用到全部线路");
            };
            host.Children.Add(applyGlobal);
        }

        // 字体下拉（枚举系统所有已安装字体）
        ComboBox BuildFontCombo(string current, Action<string> onPick)
        {
            var combo = new ComboBox();
            combo.Items.Add(new ComboBoxItem { Content = "（默认）", Tag = "" });
            foreach (var f in FontHelper.InstalledFonts())
                combo.Items.Add(new ComboBoxItem { Content = f, Tag = f });
            combo.SelectedIndex = 0;
            for (int i = 0; i < combo.Items.Count; i++)
                if (combo.Items[i] is ComboBoxItem it && (string)it.Tag == current) { combo.SelectedIndex = i; break; }
            combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is ComboBoxItem it) onPick((string)it.Tag); };
            return combo;
        }

        // ---- 设计工具面板（字体/颜色/描边，可单独某站，可同步全部）----
        void BuildDesignPanel()
        {
            ToolPanelHost.Children.Clear();
            AddPanelTitle("设计");
            AddHint("可为单个站点或整条线路精细设置字体、颜色、字号与描边；也可一键同步到全部站点。");

            // 作用范围切换
            AddLabel("作用范围");
            var scopeRow = new StackPanel { Orientation = Orientation.Horizontal };
            var scopeStation = new ToggleButton { Content = "单个站点", IsChecked = true, Width = 90, Margin = new Thickness(0, 0, 4, 0) };
            var scopeLine = new ToggleButton { Content = "整条线路", IsChecked = false, Width = 90 };
            scopeStation.Click += (s, e) => { scopeStation.IsChecked = true; scopeLine.IsChecked = false; BuildDesignPanel(); };
            scopeLine.Click += (s, e) => { scopeLine.IsChecked = true; scopeStation.IsChecked = false; BuildDesignPanel(); };
            scopeRow.Children.Add(scopeStation); scopeRow.Children.Add(scopeLine);
            ToolPanelHost.Children.Add(scopeRow);

            if (scopeLine.IsChecked == true)
            {
                BuildDesignLineSection();
                return;
            }
            BuildDesignStationSection();
        }

        // 设计面板——整条线路样式
        void BuildDesignLineSection()
        {
            AddSeparator();
            AddLabel("选择线路");
            var lineCombo = new ComboBox();
            foreach (var l in SortLines(_lines.Values))
                lineCombo.Items.Add(new ComboBoxItem { Content = l.Name, Tag = l.Id });
            if (_selectedLineId != null && _lines.ContainsKey(_selectedLineId))
                lineCombo.SelectedIndex = Math.Max(0, lineCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == _selectedLineId));
            else if (lineCombo.Items.Count > 0) lineCombo.SelectedIndex = 0;
            lineCombo.SelectionChanged += (s, e) => { if (lineCombo.SelectedItem is ComboBoxItem it && it.Tag is string id) { _selectedLineId = id; BuildDesignPanel(); } };
            ToolPanelHost.Children.Add(lineCombo);

            if (_selectedLineId == null || !_lines.TryGetValue(_selectedLineId, out var line))
            {
                AddHint("请先在线路管理或上方下拉中创建/选择一条线路。");
                return;
            }
            var host = new StackPanel();
            ToolPanelHost.Children.Add(host);
            BuildLineStyleControls(line, host);
        }

        // 设计面板——单个站点样式
        void BuildDesignStationSection()
        {
            AddSeparator();
            AddLabel("选择站点");
            var stationCombo = new ComboBox();
            foreach (var s in SortStations(_stations.Values))
                stationCombo.Items.Add(new ComboBoxItem { Content = s.NameCn, Tag = s.Id });
            if (_selectedStationId != null && _stations.ContainsKey(_selectedStationId))
                stationCombo.SelectedIndex = Math.Max(0, stationCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == _selectedStationId));
            else if (stationCombo.Items.Count > 0) stationCombo.SelectedIndex = 0;
            stationCombo.SelectionChanged += (s, e) => { if (stationCombo.SelectedItem is ComboBoxItem it && it.Tag is string id) { _selectedStationId = id; SelectStation(id); BuildDesignPanel(); } };
            ToolPanelHost.Children.Add(stationCombo);

            if (_selectedStationId == null || !_stations.TryGetValue(_selectedStationId, out var st))
            {
                AddHint("请先在地图上放置站点，或在上方下拉中选择一个站点。");
                return;
            }

            // 分语言字体
            AddLabel("中文字体");
            ToolPanelHost.Children.Add(BuildFontCombo(st.CnFont, f => { st.CnFont = f; Render(); SaveState(); }));
            AddLabel("英文字体");
            ToolPanelHost.Children.Add(BuildFontCombo(st.EnFont, f => { st.EnFont = f; Render(); SaveState(); }));
            AddLabel("第三语言字体");
            ToolPanelHost.Children.Add(BuildFontCombo(st.ThirdFont, f => { st.ThirdFont = f; Render(); SaveState(); }));

            // 文字颜色
            AddLabel("文字颜色");
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            var colorBox = new TextBox { Text = string.IsNullOrEmpty(st.FontColor) ? "#1f2733" : st.FontColor, Width = 84, VerticalContentAlignment = VerticalAlignment.Center };
            colorBox.LostFocus += (s, e) =>
            {
                var t = colorBox.Text.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9a-fA-F]{6}$")) { st.FontColor = t; Render(); SaveState(); }
                else if (t.Length == 0) { st.FontColor = ""; Render(); SaveState(); }
                else SetStatus("颜色格式应为 #RRGGBB");
            };
            var colorPick = new Button { Content = "选择…", Width = 60, Margin = new Thickness(4, 0, 0, 0) };
            colorPick.Click += (s, e) => ShowColorPicker(colorPick, picked => { st.FontColor = picked; colorBox.Text = picked; Render(); SaveState(); });
            colorRow.Children.Add(colorBox); colorRow.Children.Add(colorPick);
            ToolPanelHost.Children.Add(colorRow);

            // 字号 / 加粗 / 描边
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            sizeRow.Children.Add(new TextBlock { Text = "字号", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) });
            var sizeBox = new TextBox { Text = st.FontSize > 0 ? st.FontSize.ToString() : "", Width = 44 };
            sizeBox.LostFocus += (s, e) => { if (double.TryParse(sizeBox.Text, out var v)) { st.FontSize = Math.Max(0, v); Render(); SaveState(); } };
            sizeRow.Children.Add(sizeBox);
            var boldChk = new CheckBox { Content = "加粗", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), IsChecked = st.FontBold };
            boldChk.Checked += (s, e) => { st.FontBold = true; Render(); SaveState(); };
            boldChk.Unchecked += (s, e) => { st.FontBold = false; Render(); SaveState(); };
            sizeRow.Children.Add(boldChk);
            var outlineChk = new CheckBox { Content = "描边", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), IsChecked = st.FontOutline };
            outlineChk.Checked += (s, e) => { st.FontOutline = true; Render(); SaveState(); };
            outlineChk.Unchecked += (s, e) => { st.FontOutline = false; Render(); SaveState(); };
            sizeRow.Children.Add(outlineChk);
            ToolPanelHost.Children.Add(sizeRow);

            // 同步与重置
            AddSeparator();
            AddButton("同步此样式到全部站点", () =>
            {
                foreach (var other in _stations.Values)
                {
                    other.CnFont = st.CnFont; other.EnFont = st.EnFont; other.ThirdFont = st.ThirdFont;
                    other.FontColor = st.FontColor; other.FontSize = st.FontSize; other.FontBold = st.FontBold; other.FontOutline = st.FontOutline;
                }
                Render(); SaveState(); SetStatus("样式已同步到全部站点");
            });
            AddButton("重置此站点样式（沿用线路样式）", () =>
            {
                st.CnFont = ""; st.EnFont = ""; st.ThirdFont = ""; st.FontColor = "";
                st.FontSize = 0; st.FontBold = false; st.FontOutline = true;
                BuildDesignPanel(); Render(); SaveState();
            });
        }

        // ---- 项目设置面板 ----
        void BuildProjectPanel()
        {
            ToolPanelHost.Children.Clear();
            AddPanelTitle("项目设置");
            AddLabel("项目名称");
            var nameBox = new TextBox { Text = _projectName };
            nameBox.LostFocus += (s, e) => { _projectName = nameBox.Text; UpdateProjectUI(); SaveState(); };
            ToolPanelHost.Children.Add(nameBox);

            AddLabel("项目中心（城市）");
            ToolPanelHost.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(_anchorName) ? "（未设置）" : _anchorName, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 6) });

            var constrained = new CheckBox { Content = "限制在当前城市范围", IsChecked = _constrained, Margin = new Thickness(0, 4, 0, 0) };
            constrained.Checked += (s, e) => { _constrained = true; SaveState(); };
            constrained.Unchecked += (s, e) => { _constrained = false; SaveState(); };
            ToolPanelHost.Children.Add(constrained);

            var distRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var distBox = new TextBox { Text = _maxDistKm.ToString(), Width = 64 };
            distBox.LostFocus += (s, e) => { if (double.TryParse(distBox.Text, out var v)) _maxDistKm = Math.Max(0, v); SaveState(); };
            DockPanel.SetDock(distBox, Dock.Right);
            distRow.Children.Add(distBox);
            distRow.Children.Add(new TextBlock { Text = "最大范围（km）", VerticalAlignment = VerticalAlignment.Center });
            ToolPanelHost.Children.Add(distRow);

            var sideRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var sideBox = new TextBox { Text = _mapSideKm.ToString(), Width = 64 };
            sideBox.LostFocus += (s, e) => { if (double.TryParse(sideBox.Text, out var v)) _mapSideKm = Math.Max(0, v); SaveState(); Render(); };
            DockPanel.SetDock(sideBox, Dock.Right);
            sideRow.Children.Add(sideBox);
            sideRow.Children.Add(new TextBlock { Text = "地图加载边长（km）", VerticalAlignment = VerticalAlignment.Center });
            ToolPanelHost.Children.Add(sideRow);

            AddHint("关闭限制或调大范围后可模拟跨城线路。地图只加载城市周边边长范围的方形区域。");
            AddSeparator();
            AddPanelTitle("数据");
            AddButton("载入示例数据", LoadDemo);
            AddButton("清空全部数据", () =>
            {
                if (MessageBox.Show("确定清空全部数据？此操作不可撤销。", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                { _stations.Clear(); _lines.Clear(); _schematicPos.Clear(); _measure.Clear(); _activeLineId = null; _selectedLineId = null; _selectedStationId = null; Render(); BuildProjectPanel(); SaveState(); }
            });
        }

        // ---- 离线地图面板 ----
        void BuildOfflinePanel()
        {
            ToolPanelHost.Children.Clear();
            AddPanelTitle("离线地图");
            AddHint("下载指定区域瓦片后，断网仍可查看底图。");
            TextBox westBox = null, eastBox = null, southBox = null, northBox = null;
            var searchRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var searchBox = new TextBox { ToolTip = "如：北京市朝阳区" };
            var searchBtn = new Button { Content = "搜索", Width = 54 };
            DockPanel.SetDock(searchBtn, Dock.Right);
            searchRow.Children.Add(searchBtn);
            searchRow.Children.Add(searchBox);
            ToolPanelHost.Children.Add(searchRow);
            searchBtn.Click += async (s, e) =>
            {
                var q = searchBox.Text.Trim();
                if (q.Length == 0) { SetStatus("请输入区/城市名"); return; }
                SetStatus("正在搜索区域范围…");
                var r = await Geocoder.SearchAsync(q);
                if (r != null)
                {
                    westBox.Text = r.West.ToString("F5"); eastBox.Text = r.East.ToString("F5");
                    southBox.Text = r.South.ToString("F5"); northBox.Text = r.North.ToString("F5");
                    NavigateTo((r.West + r.East) / 2, (r.South + r.North) / 2, 11);
                    SetStatus($"已找到区域：{r.Name.Split(',')[0]}");
                }
                else SetStatus("未找到该区域，请尝试更准确的名称");
            };

            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            void AddBox(string label, int r, int c, ref TextBox box)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, c == 0 ? 6 : 0, 4) };
                sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.Gray });
                box = new TextBox();
                sp.Children.Add(box);
                Grid.SetRow(sp, r); Grid.SetColumn(sp, c);
                grid.Children.Add(sp);
            }
            AddBox("西经", 0, 0, ref westBox);
            AddBox("东经", 0, 1, ref eastBox);
            AddBox("南纬", 1, 0, ref southBox);
            AddBox("北纬", 1, 1, ref northBox);
            ToolPanelHost.Children.Add(grid);

            var zRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            zRow.ColumnDefinitions.Add(new ColumnDefinition());
            zRow.ColumnDefinitions.Add(new ColumnDefinition());
            var minZ = new TextBox { Text = "11", Margin = new Thickness(0, 0, 6, 0) };
            var maxZ = new TextBox { Text = "15" };
            var minSp = new StackPanel(); minSp.Children.Add(new TextBlock { Text = "最小级别", FontSize = 11, Foreground = Brushes.Gray }); minSp.Children.Add(minZ);
            var maxSp = new StackPanel(); maxSp.Children.Add(new TextBlock { Text = "最大级别", FontSize = 11, Foreground = Brushes.Gray }); maxSp.Children.Add(maxZ);
            Grid.SetColumn(minSp, 0); Grid.SetColumn(maxSp, 1);
            zRow.Children.Add(minSp); zRow.Children.Add(maxSp);
            ToolPanelHost.Children.Add(zRow);

            var boxSelectBtn = new Button { Content = "框选下载范围", Margin = new Thickness(0, 8, 0, 0) };
            boxSelectBtn.Click += (s, e) =>
            {
                _boxSelect = true; _boxStart = null; _boxCurrent = null;
                MapCanvas.Cursor = Cursors.Cross;
                SetStatus("请在地图上拖拽一个矩形框选下载范围（Esc 取消）");
            };
            ToolPanelHost.Children.Add(boxSelectBtn);

            var progress = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x0f, 0x7d, 0xc2)), Margin = new Thickness(0, 4, 0, 0) };
            ToolPanelHost.Children.Add(progress);

            var startBtn = new Button { Content = "开始下载", Margin = new Thickness(0, 8, 0, 0) };
            startBtn.Click += async (s, e) =>
            {
                if (!double.TryParse(westBox.Text, out var w) || !double.TryParse(eastBox.Text, out var es) ||
                    !double.TryParse(southBox.Text, out var so) || !double.TryParse(northBox.Text, out var no) ||
                    !(w < es && so < no)) { SetStatus("请先设置有效的下载范围（可用搜索或框选）"); return; }
                int mn = int.TryParse(minZ.Text, out var a) ? a : 11;
                int mx = int.TryParse(maxZ.Text, out var b) ? b : 15;
                startBtn.IsEnabled = false;
                await DownloadAreaAsync(w, so, es, no, mn, mx, progress);
                startBtn.IsEnabled = true;
                UpdateOfflineCount();
                progress.Text = "下载完成！可勾选「离线模式」断网查看。";
            };
            ToolPanelHost.Children.Add(startBtn);

            var offlineChk = new CheckBox { Content = "离线模式", IsChecked = _offlineMode, Margin = new Thickness(0, 8, 0, 0) };
            offlineChk.Checked += (s, e) => { _offlineMode = true; Render(); };
            offlineChk.Unchecked += (s, e) => { _offlineMode = false; Render(); };
            ToolPanelHost.Children.Add(offlineChk);

            var countRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            countRow.ColumnDefinitions.Add(new ColumnDefinition());
            countRow.ColumnDefinitions.Add(new ColumnDefinition());
            var countBtn = new Button { Content = $"已缓存：{_tiles.Count()} 张", Margin = new Thickness(0, 0, 3, 0) };
            countBtn.Click += (s, e) => UpdateOfflineCount();
            var clearBtn = new Button { Content = "清空缓存", Margin = new Thickness(3, 0, 0, 0) };
            clearBtn.Click += (s, e) =>
            {
                if (MessageBox.Show("确定清空所有离线缓存瓦片？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                { _tiles.Clear(); _tileCache.Clear(); UpdateOfflineCount(); SetStatus("已清空离线缓存"); BuildOfflinePanel(); }
            };
            Grid.SetColumn(countBtn, 0); Grid.SetColumn(clearBtn, 1);
            countRow.Children.Add(countBtn); countRow.Children.Add(clearBtn);
            ToolPanelHost.Children.Add(countRow);

            // 存引用以便框选填写
            _offlineBoxes = (westBox, eastBox, southBox, northBox);
        }
        (TextBox west, TextBox east, TextBox south, TextBox north)? _offlineBoxes;

        // ---- 显示设置（选择面板/站点面板共用）----
        void AddDisplaySettings()
        {
            var cn = new CheckBox { Content = "中文名", IsChecked = _showCn };
            var en = new CheckBox { Content = "英文名", IsChecked = _showEn };
            var third = new CheckBox { Content = "第三语 / 注音", IsChecked = _showThird };
            cn.Checked += (s, e) => { _showCn = true; Render(); };
            cn.Unchecked += (s, e) => { _showCn = false; Render(); };
            en.Checked += (s, e) => { _showEn = true; Render(); };
            en.Unchecked += (s, e) => { _showEn = false; Render(); };
            third.Checked += (s, e) => { _showThird = true; Render(); };
            third.Unchecked += (s, e) => { _showThird = false; Render(); };
            ToolPanelHost.Children.Add(cn);
            ToolPanelHost.Children.Add(en);
            ToolPanelHost.Children.Add(third);

            var radiusRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var radiusBox = new TextBox { Text = _curveRadiusM.ToString(), Width = 64 };
            radiusBox.LostFocus += (s, e) => { if (double.TryParse(radiusBox.Text, out var v)) _curveRadiusM = Math.Max(0, v); Render(); };
            DockPanel.SetDock(radiusBox, Dock.Right);
            radiusRow.Children.Add(radiusBox);
            radiusRow.Children.Add(new TextBlock { Text = "曲线转弯半径（米）", VerticalAlignment = VerticalAlignment.Center });
            ToolPanelHost.Children.Add(radiusRow);

            // 示意图设置（网格对齐）
            AddSeparator();
            AddPanelTitle("示意图设置");
            var gridChk = new CheckBox { Content = "显示网格 / 自动对齐", IsChecked = _schGrid };
            gridChk.Checked += (s, e) => { _schGrid = true; Render(); };
            gridChk.Unchecked += (s, e) => { _schGrid = false; Render(); };
            ToolPanelHost.Children.Add(gridChk);

            var gsRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var gsBox = new TextBox { Text = _schGridSize.ToString(), Width = 64 };
            gsBox.LostFocus += (s, e) => { if (double.TryParse(gsBox.Text, out var v)) _schGridSize = Math.Max(5, v); Render(); };
            DockPanel.SetDock(gsBox, Dock.Right);
            gsRow.Children.Add(gsBox);
            gsRow.Children.Add(new TextBlock { Text = "网格间距", VerticalAlignment = VerticalAlignment.Center });
            ToolPanelHost.Children.Add(gsRow);

            var thRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var thBox = new TextBox { Text = _schSnapThreshold.ToString(), Width = 64 };
            thBox.LostFocus += (s, e) => { if (double.TryParse(thBox.Text, out var v)) _schSnapThreshold = Math.Max(0, v); Render(); };
            DockPanel.SetDock(thBox, Dock.Right);
            thRow.Children.Add(thBox);
            thRow.Children.Add(new TextBlock { Text = "对齐容差", VerticalAlignment = VerticalAlignment.Center });
            ToolPanelHost.Children.Add(thRow);

            var mapBgChk = new CheckBox { Content = "显示真实地图背景", IsChecked = _schShowMapBg, Margin = new Thickness(0, 4, 0, 0) };
            mapBgChk.Checked += (s, e) => { _schShowMapBg = true; Render(); };
            mapBgChk.Unchecked += (s, e) => { _schShowMapBg = false; Render(); };
            ToolPanelHost.Children.Add(mapBgChk);

            var opRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var opSlider = new Slider { Minimum = 0.05, Maximum = 1, Value = _schMapBgOpacity, Width = 120 };
            opSlider.ValueChanged += (s, e) => { _schMapBgOpacity = opSlider.Value; if (_schShowMapBg) Render(); };
            DockPanel.SetDock(opSlider, Dock.Right);
            opRow.Children.Add(opSlider);
            opRow.Children.Add(new TextBlock { Text = "背景不透明度", VerticalAlignment = VerticalAlignment.Center });
            ToolPanelHost.Children.Add(opRow);

            AddHint("切换到示意图时会自动将站点坐标对齐最近网格；若两站 x 或 y 差值小于「对齐容差」，则视为同一网格线自动对齐。");
        }
        void AddDisplayCheckboxes()
        {
            var cn = new CheckBox { Content = "中文名", IsChecked = _showCn };
            var en = new CheckBox { Content = "英文名", IsChecked = _showEn };
            var third = new CheckBox { Content = "第三语 / 注音", IsChecked = _showThird };
            cn.Checked += (s, e) => { _showCn = true; Render(); };
            cn.Unchecked += (s, e) => { _showCn = false; Render(); };
            en.Checked += (s, e) => { _showEn = true; Render(); };
            en.Unchecked += (s, e) => { _showEn = false; Render(); };
            third.Checked += (s, e) => { _showThird = true; Render(); };
            third.Unchecked += (s, e) => { _showThird = false; Render(); };
            ToolPanelHost.Children.Add(cn);
            ToolPanelHost.Children.Add(en);
            ToolPanelHost.Children.Add(third);
        }

        void Tool_Click(object sender, RoutedEventArgs e) => SetTool((string)((ToggleButton)sender).Tag);

        // ================= 简易颜色选择器 =================
        static readonly string[] ColorPickerColors =
        {
            "#e4002b", "#0f7dc2", "#00a651", "#f08300", "#8a2be2", "#00b0b9", "#c58c3e",
            "#e6c700", "#e14b8a", "#5b6770", "#2c7a4d", "#d1495b", "#000000", "#ffffff",
            "#ff6600", "#ffcc00", "#cc0000", "#3366cc", "#33cccc", "#9933cc", "#ff3399", "#666666"
        };
        void ShowColorPicker(Button anchor, Action<string> onPick)
        {
            var menu = new ContextMenu();
            var grid = new Grid();
            grid.ColumnDefinitions.Clear();
            int cols = 8;
            for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            for (int i = 0; i < ColorPickerColors.Length; i++)
            {
                int r = i / cols, c = i % cols;
                if (grid.RowDefinitions.Count <= r) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                var hex = ColorPickerColors[i];
                var b = new Border
                {
                    Background = new SolidColorBrush(ParseColor(hex)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
                    BorderThickness = new Thickness(1), Margin = new Thickness(1), Cursor = Cursors.Hand
                };
                b.MouseLeftButtonDown += (s, e) => { onPick(hex); menu.IsOpen = false; };
                Grid.SetRow(b, r); Grid.SetColumn(b, c);
                grid.Children.Add(b);
            }
            var item = new MenuItem { Header = grid, StaysOpenOnClick = true };
            menu.Items.Add(item);
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        // ================= 渲染 =================
        void Render()
        {
            MapCanvas.Children.Clear();
            _placedLabels.Clear();
            _renderStationPos.Clear();
            if (_mode == "map")
            {
                DrawTiles();
                DrawLines();
                DrawStations();
                DrawMeasure();
                DrawBox();
                SchematicTopBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                DrawSchematic();
                SchematicTopBar.Visibility = Visibility.Visible;
            }
            UpdateLegend();
        }

        void DrawTiles()
        {
            int z = _zoom;
            double w = MapCanvas.ActualWidth, h = MapCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double cx = MapMath.LngToWorldX(_centerLng, z), cy = MapMath.LatToWorldY(_centerLat, z);
            double worldLeft = cx - w / 2, worldTop = cy - h / 2;
            int tx0 = (int)Math.Floor(worldLeft / 256), tx1 = (int)Math.Floor((worldLeft + w) / 256);
            int ty0 = (int)Math.Floor(worldTop / 256), ty1 = (int)Math.Floor((worldTop + h) / 256);
            int maxTiles = (int)Math.Pow(2, z);

            bool clip = (_anchorLng != 0 || _anchorLat != 0) && _mapSideKm > 0;
            double clipMinX = double.NegativeInfinity, clipMaxX = double.PositiveInfinity;
            double clipMinY = double.NegativeInfinity, clipMaxY = double.PositiveInfinity;
            if (clip)
            {
                double mpp = MapMath.MetersPerPixel(_anchorLat, z);
                double halfPx = (_mapSideKm * 1000 / 2) / mpp;
                double acx = MapMath.LngToWorldX(_anchorLng, z);
                double acy = MapMath.LatToWorldY(_anchorLat, z);
                clipMinX = acx - halfPx; clipMaxX = acx + halfPx;
                clipMinY = acy - halfPx; clipMaxY = acy + halfPx;
            }

            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    if (tx < 0 || ty < 0 || tx >= maxTiles || ty >= maxTiles) continue;
                    double tileMinX = tx * 256, tileMaxX = tx * 256 + 256;
                    double tileMinY = ty * 256, tileMaxY = ty * 256 + 256;
                    if (tileMaxX < clipMinX || tileMinX > clipMaxX || tileMaxY < clipMinY || tileMinY > clipMaxY) continue;
                    double sx = tx * 256 - (cx - w / 2);
                    double sy = ty * 256 - (cy - h / 2);
                    var key = $"{_tiles.Key}/{z}/{tx}/{ty}";
                    if (_tileCache.TryGetValue(key, out var bmp) && bmp != null)
                    {
                        var img = new Image { Source = bmp, Width = 256, Height = 256 };
                        Canvas.SetLeft(img, sx); Canvas.SetTop(img, sy);
                        MapCanvas.Children.Add(img);
                    }
                    else
                    {
                        var ph = new Rectangle { Width = 256, Height = 256, Fill = Brushes.White, Stroke = Brushes.LightGray, StrokeThickness = 0.5 };
                        Canvas.SetLeft(ph, sx); Canvas.SetTop(ph, sy);
                        MapCanvas.Children.Add(ph);
                        _ = LoadTileAsync(z, tx, ty, key);
                    }
                }

            if (clip)
            {
                double bx = clipMinX - (cx - w / 2), by = clipMinY - (cy - h / 2);
                double bw = clipMaxX - clipMinX, bh = clipMaxY - clipMinY;
                var r = new Rectangle
                {
                    Width = bw, Height = bh,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0x0f, 0x7d, 0xc2)),
                    StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 6, 4 }
                };
                Canvas.SetLeft(r, bx); Canvas.SetTop(r, by);
                MapCanvas.Children.Add(r);
            }
        }
        void TrimTileCache()
        {
            if (_tileCache.Count > 1500) _tileCache.Clear();
        }
        async Task LoadTileAsync(int z, int x, int y, string key)
        {
            if (_inflight.Contains(key)) return;
            _inflight.Add(key);
            try
            {
                BitmapImage bmp = null;
                if (_tiles.Exists(z, x, y))
                    bmp = await Task.Run(() => _tiles.Load(z, x, y));
                else if (!_offlineMode && await _tiles.DownloadAsync(z, x, y))
                    bmp = await Task.Run(() => _tiles.Load(z, x, y));
                if (bmp != null)
                {
                    _tileCache[key] = bmp;
                    TrimTileCache();
                    await Dispatcher.InvokeAsync(Render);
                }
            }
            finally { _inflight.Remove(key); }
        }

        Geometry BuildRoundedGeometry(List<Point> pts, double radius)
        {
            var cmds = PathBuilder.Rounded(pts, radius);
            var fig = new PathFigure { StartPoint = cmds[0].Point };
            foreach (var c in cmds.Skip(1))
            {
                if (c.IsArc)
                    fig.Segments.Add(new ArcSegment(c.Point, new Size(c.Radius, c.Radius), 0, false,
                        c.Sweep ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
                else
                    fig.Segments.Add(new LineSegment(c.Point, true));
            }
            var geo = new PathGeometry(); geo.Figures.Add(fig); geo.Freeze();
            return geo;
        }
        void ApplyDash(System.Windows.Shapes.Path p, string type)
        {
            if (type == "transition") { p.StrokeDashArray = new DoubleCollection { 1, 6 }; p.StrokeDashCap = PenLineCap.Round; }
            else if (type == "ground") { p.StrokeDashArray = new DoubleCollection { 12, 8 }; }
        }

        // 颜色取精确色优先，否则取基础色
        string EffectiveLineColor(Line line) => string.IsNullOrEmpty(line.LineColor) ? line.Color : line.LineColor;

        void DrawLines()
        {
            foreach (var line in _lines.Values)
            {
                var brush = new SolidColorBrush(ParseColor(EffectiveLineColor(line)));
                DrawLineGeometry(line.StationOrder, line.Segments, brush, line.Id, -1);
                for (int bi = 0; bi < line.Branches.Count; bi++)
                {
                    var br = line.Branches[bi];
                    var order = new List<string> { br.Junction };
                    order.AddRange(br.StationOrder);
                    DrawLineGeometry(order, br.Segments, brush, line.Id, bi);
                }
                DrawLineLogo(line);
            }
        }

        void DrawLineLogo(Line line)
        {
            if (string.IsNullOrEmpty(line.LogoPath) && string.IsNullOrEmpty(line.LogoText)) return;
            if (line.StationOrder.Count == 0 || !_stations.TryGetValue(line.StationOrder[0], out var first)) return;
            var p = GeoToScreen(first.Lng, first.Lat);
            double offX = 20, offY = -20;
            if (line.LogoPath != null && File.Exists(line.LogoPath))
            {
                try
                {
                    var bmp = new BitmapImage(new Uri(line.LogoPath));
                    var img = new Image { Source = bmp, Width = 20, Height = 20, IsHitTestVisible = false };
                    Canvas.SetLeft(img, p.X + offX); Canvas.SetTop(img, p.Y + offY);
                    MapCanvas.Children.Add(img);
                }
                catch { }
            }
            else
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(ParseColor(EffectiveLineColor(line))),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 1, 5, 1), IsHitTestVisible = false
                };
                badge.Child = new TextBlock { Text = line.LogoText, Foreground = new SolidColorBrush(ParseColor(line.LogoColor)), FontSize = 11, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(badge, p.X + offX); Canvas.SetTop(badge, p.Y + offY);
                MapCanvas.Children.Add(badge);
            }
        }

        void DrawLineGeometry(List<string> order, List<Segment> segs, SolidColorBrush brush, string lineId, int branchIdx)
        {
            if (segs == null || segs.Count == 0)
            {
                var pts = order.Where(id => _stations.ContainsKey(id))
                    .Select(id => GeoToScreen(_stations[id].Lng, _stations[id].Lat)).ToList();
                if (pts.Count < 2) return;
                var p = new System.Windows.Shapes.Path { Data = BuildRoundedGeometry(pts, RadiusPx()), Stroke = brush, StrokeThickness = EffectiveLineWidth(lineId), StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = 0.9 };
                MapCanvas.Children.Add(p);
                return;
            }

            for (int i = 0; i < segs.Count; i++)
            {
                var seg = segs[i];
                if (!_stations.TryGetValue(seg.A, out var sa) || !_stations.TryGetValue(seg.B, out var sb)) continue;

                // 若有钢笔式曲线锚点，用贝塞尔曲线绘制
                if (seg.CurveAnchors != null && seg.CurveAnchors.Count > 0)
                {
                    DrawBezierSegmentGeometry(seg, sa, sb, brush, lineId, branchIdx, i);
                    continue;
                }

                var way = new List<Point> { GeoToScreen(sa.Lng, sa.Lat) };
                if (seg.Anchors != null)
                    foreach (var an in seg.Anchors)
                        way.Add(GeoToScreen(an.Lng, an.Lat));
                way.Add(GeoToScreen(sb.Lng, sb.Lat));
                var p = new System.Windows.Shapes.Path
                {
                    Data = BuildRoundedGeometry(way, RadiusPx()),
                    Stroke = brush, StrokeThickness = EffectiveLineWidth(lineId),
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                };
                ApplyDash(p, seg.Type);
                MapCanvas.Children.Add(p);

                if (_mode == "map" && seg.Anchors != null)
                {
                    for (int ai = 0; ai < seg.Anchors.Count; ai++)
                    {
                        var ap = GeoToScreen(seg.Anchors[ai].Lng, seg.Anchors[ai].Lat);
                        var dot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = brush, StrokeThickness = 2 };
                        Canvas.SetLeft(dot, ap.X - 5); Canvas.SetTop(dot, ap.Y - 5);
                        dot.Tag = $"anchor|{lineId}|{branchIdx}|{i}|{ai}";
                        dot.MouseLeftButtonDown += Anchor_MouseLeftButtonDown;
                        dot.Cursor = Cursors.Hand;
                        MapCanvas.Children.Add(dot);
                    }
                }
            }
        }

        // 绘制带钢笔式锚点的贝塞尔曲线段：A →(入向手柄)→ 锚点 →(出向手柄)→ B
        void DrawBezierSegmentGeometry(Segment seg, Station sa, Station sb, SolidColorBrush brush, string lineId, int branchIdx, int segIdx)
        {
            var a = GeoToScreen(sa.Lng, sa.Lat);
            var b = GeoToScreen(sb.Lng, sb.Lat);
            double lw = EffectiveLineWidth(lineId);
            var anchors = seg.CurveAnchors;

            var fig = new PathFigure { StartPoint = a, IsClosed = false };
            // 第一段：A → 第一个锚点（用第一个锚点的入向手柄作为控制点）
            var first = anchors[0];
            var firstCtrl = GeoToScreen(first.InCtrl.Lng, first.InCtrl.Lat);
            fig.Segments.Add(new BezierSegment(firstCtrl, firstCtrl, GeoToScreen(first.Lng, first.Lat), true));
            // 中间段：锚点 i → 锚点 i+1（用 i 的出向手柄 + i+1 的入向手柄）
            for (int i = 1; i < anchors.Count; i++)
            {
                var prev = anchors[i - 1];
                var cur = anchors[i];
                var c1 = GeoToScreen(prev.OutCtrl.Lng, prev.OutCtrl.Lat);
                var c2 = GeoToScreen(cur.InCtrl.Lng, cur.InCtrl.Lat);
                fig.Segments.Add(new BezierSegment(c1, c2, GeoToScreen(cur.Lng, cur.Lat), true));
            }
            // 最后一段：最后一个锚点 → B（用其出向手柄）
            var last = anchors[anchors.Count - 1];
            var lastCtrl = GeoToScreen(last.OutCtrl.Lng, last.OutCtrl.Lat);
            fig.Segments.Add(new BezierSegment(lastCtrl, lastCtrl, b, true));

            var geo = new PathGeometry(); geo.Figures.Add(fig);
            var p = new System.Windows.Shapes.Path
            {
                Data = geo, Stroke = brush, StrokeThickness = lw,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
            ApplyDash(p, seg.Type);
            MapCanvas.Children.Add(p);

            if (_mode != "map") return;

            // 绘制锚点本体 + 手柄
            for (int ai = 0; ai < seg.CurveAnchors.Count; ai++)
            {
                var ca = seg.CurveAnchors[ai];
                var anchor = GeoToScreen(ca.Lng, ca.Lat);
                var ctrlIn = GeoToScreen(ca.InCtrl.Lng, ca.InCtrl.Lat);
                var ctrlOut = GeoToScreen(ca.OutCtrl.Lng, ca.OutCtrl.Lat);

                // 手柄线（锚点到控制点）
                var lineIn = new System.Windows.Shapes.Line { X1 = anchor.X, Y1 = anchor.Y, X2 = ctrlIn.X, Y2 = ctrlIn.Y, Stroke = brush, StrokeThickness = 1.2, Opacity = 0.6, StrokeDashArray = new DoubleCollection { 3, 2 } };
                MapCanvas.Children.Add(lineIn);
                var lineOut = new System.Windows.Shapes.Line { X1 = anchor.X, Y1 = anchor.Y, X2 = ctrlOut.X, Y2 = ctrlOut.Y, Stroke = brush, StrokeThickness = 1.2, Opacity = 0.6, StrokeDashArray = new DoubleCollection { 3, 2 } };
                MapCanvas.Children.Add(lineOut);

                // 锚点本体（方形，钢笔风格）
                var body = new Rectangle { Width = 10, Height = 10, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x1f, 0x27, 0x33)), StrokeThickness = 1.8 };
                Canvas.SetLeft(body, anchor.X - 5); Canvas.SetTop(body, anchor.Y - 5);
                body.Tag = $"curveanchor|{lineId}|{branchIdx}|{segIdx}|{ai}|0";
                body.MouseLeftButtonDown += CurveAnchor_MouseLeftButtonDown;
                body.Cursor = Cursors.SizeAll;
                MapCanvas.Children.Add(body);

                // 入向手柄
                var inDot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = brush, StrokeThickness = 2 };
                Canvas.SetLeft(inDot, ctrlIn.X - 5); Canvas.SetTop(inDot, ctrlIn.Y - 5);
                inDot.Tag = $"curveanchor|{lineId}|{branchIdx}|{segIdx}|{ai}|1";
                inDot.MouseLeftButtonDown += CurveAnchor_MouseLeftButtonDown;
                inDot.Cursor = Cursors.Hand;
                MapCanvas.Children.Add(inDot);

                // 出向手柄
                var outDot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = brush, StrokeThickness = 2 };
                Canvas.SetLeft(outDot, ctrlOut.X - 5); Canvas.SetTop(outDot, ctrlOut.Y - 5);
                outDot.Tag = $"curveanchor|{lineId}|{branchIdx}|{segIdx}|{ai}|2";
                outDot.MouseLeftButtonDown += CurveAnchor_MouseLeftButtonDown;
                outDot.Cursor = Cursors.Hand;
                MapCanvas.Children.Add(outDot);
            }
        }

        void CurveAnchor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tag = (string)((FrameworkElement)sender).Tag;
            var parts = tag.Split('|');
            if (parts.Length < 6) return;
            string lineId = parts[1]; int branchIdx = int.Parse(parts[2]); int segIdx = int.Parse(parts[3]); int anchorIdx = int.Parse(parts[4]); int ctrl = int.Parse(parts[5]);
            _dragCurveSegKey = $"{lineId}|{branchIdx}|{segIdx}";
            _dragCurveAnchorIdx = anchorIdx;
            _dragCurveCtrl = ctrl;
            e.Handled = true;
            MapCanvas.CaptureMouse();
        }

        Segment GetCurveSegment(string key)
        {
            var parts = key.Split('|');
            if (parts.Length < 3 || !_lines.TryGetValue(parts[0], out var line)) return null;
            int branchIdx = int.Parse(parts[1]);
            int segIdx = int.Parse(parts[2]);
            if (branchIdx < 0) return (segIdx >= 0 && segIdx < line.Segments.Count) ? line.Segments[segIdx] : null;
            if (branchIdx < line.Branches.Count) return (segIdx >= 0 && segIdx < line.Branches[branchIdx].Segments.Count) ? line.Branches[branchIdx].Segments[segIdx] : null;
            return null;
        }

        double EffectiveLineWidth(string lineId)
        {
            if (lineId != null && _lines.TryGetValue(lineId, out var l) && l.LineWidth > 0) return l.LineWidth;
            return 5;
        }

        void Anchor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var ell = (Ellipse)sender; var tag = (string)ell.Tag;
            var parts = tag.Split('|');
            if (parts.Length < 6) return;
            string lineId = parts[1]; int branchIdx = int.Parse(parts[2]); int segIdx = int.Parse(parts[3]); int anchorIdx = int.Parse(parts[4]);
            _dragAnchorSegKey = $"{lineId}|{branchIdx}|{segIdx}";
            _dragAnchorIdx = anchorIdx;
            e.Handled = true;
            MapCanvas.CaptureMouse();
        }

        Segment GetAnchorSegment(string key)
        {
            var parts = key.Split('|');
            if (parts.Length < 3 || !_lines.TryGetValue(parts[0], out var line)) return null;
            int branchIdx = int.Parse(parts[1]);
            int segIdx = int.Parse(parts[2]);
            if (branchIdx < 0) return (segIdx >= 0 && segIdx < line.Segments.Count) ? line.Segments[segIdx] : null;
            if (branchIdx < line.Branches.Count) return (segIdx >= 0 && segIdx < line.Branches[branchIdx].Segments.Count) ? line.Branches[branchIdx].Segments[segIdx] : null;
            return null;
        }

        void DrawStations()
        {
            // 先收集所有站点屏幕位置（供标签方向避让与防遮挡）
            foreach (var s in _stations.Values)
                _renderStationPos[s.Id] = GeoToScreen(s.Lng, s.Lat);
            foreach (var s in _stations.Values)
            {
                var p = _renderStationPos[s.Id];
                AddStationShape(s, p, s.IsTransfer ? 9 : 6, s.IsTransfer ? 2.2 : 1.8, s.Id == _selectedStationId);
            }
        }
        void AddStationShape(Station s, Point p, double r, double sw, bool selected)
        {
            var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x1f, 0x27, 0x33)), StrokeThickness = sw };
            Canvas.SetLeft(e, p.X - r); Canvas.SetTop(e, p.Y - r);
            e.Tag = s.Id;
            e.MouseLeftButtonDown += Station_MouseLeftButtonDown;
            MapCanvas.Children.Add(e);
            if (s.IsTransfer)
            {
                var inner = new Ellipse { Width = 6.8, Height = 6.8, Fill = new SolidColorBrush(Color.FromRgb(0x1f, 0x27, 0x33)), IsHitTestVisible = false };
                Canvas.SetLeft(inner, p.X - 3.4); Canvas.SetTop(inner, p.Y - 3.4);
                MapCanvas.Children.Add(inner);
            }
            if (selected)
            {
                double rr = r + 4;
                var ring = new Ellipse { Width = rr * 2, Height = rr * 2, Stroke = new SolidColorBrush(Color.FromRgb(0x0f, 0x7d, 0xc2)), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
                Canvas.SetLeft(ring, p.X - rr); Canvas.SetTop(ring, p.Y - rr);
                MapCanvas.Children.Add(ring);
            }
            AddStationLabel(s, p, r);
        }

        void AddStationLabel(Station s, Point p, double circleR)
        {
            var lines = new List<string>();
            if (_showCn && !string.IsNullOrEmpty(s.NameCn)) lines.Add(s.NameCn);
            if (_showEn && !string.IsNullOrEmpty(s.NameEn)) lines.Add(s.NameEn);
            if (_showThird && !string.IsNullOrEmpty(s.NameThird)) lines.Add(s.NameThird);
            if (lines.Count == 0) return;

            Line styleLine = null;
            foreach (var lid in s.Lines)
                if (_lines.TryGetValue(lid, out var l)) { styleLine = l; break; }
            double fontSize = ResolveStationFontSize(s, styleLine);
            bool bold = ResolveStationBold(s, styleLine);
            bool outline = ResolveStationOutline(s, styleLine);
            Color textColor = ResolveStationColor(s, styleLine);

            // 逐行精确测量（字体 + 加粗）
            var fontList = new List<(string text, string fam, double w)>();
            double maxW = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string fam = ResolveStationFont(s, styleLine, i);
                double w = MeasureLabelWidth(new List<(string, string)> { (lines[i], fam) }, fontSize, bold);
                fontList.Add((lines[i], fam, w));
                maxW = Math.Max(maxW, w);
            }
            double estW = maxW + 6;
            double lineH = fontSize + 3;
            double totalH = lines.Count * lineH;

            var layoutOpt = ComputeLabelLayout(s, p, circleR, fontSize, estW, totalH, lines);
            if (layoutOpt == null) return; // 站点在屏幕外，跳过标签绘制
            var layout = layoutOpt.Value;

            var tb = new TextBlock { FontSize = fontSize, LineHeight = lineH, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, IsHitTestVisible = false };
            if (outline)
                tb.Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 3, ShadowDepth = 0, Direction = 0, Opacity = 1 };
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) tb.Inlines.Add(new LineBreak());
                var run = new Run(lines[i]);
                string font = fontList[i].fam;
                if (!string.IsNullOrEmpty(font)) run.FontFamily = new FontFamily(font);
                if (i == 0 && _showCn) run.FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold;
                else if (bold) run.FontWeight = FontWeights.Bold;
                run.Foreground = new SolidColorBrush(textColor);
                tb.Inlines.Add(run);
            }

            // 设置对齐与宽度，保证多行文字整齐排列
            tb.Width = estW;
            if (layout.Horizontal)
            {
                tb.TextAlignment = TextAlignment.Center;      // 东西走向：居中于站点
                Canvas.SetLeft(tb, layout.Left);
            }
            else if (layout.Flip)
            {
                tb.TextAlignment = TextAlignment.Right;       // 左侧放置：右对齐
                Canvas.SetLeft(tb, layout.Left);
            }
            else
            {
                tb.TextAlignment = TextAlignment.Left;        // 右侧放置：左对齐
                Canvas.SetLeft(tb, layout.Left);
            }
            Canvas.SetTop(tb, layout.Top);
            MapCanvas.Children.Add(tb);

            // 记录已放置标签包围盒（用于后续站点防遮挡）
            _placedLabels[s.Id] = new Rect(layout.Left, layout.Top, estW, totalH);
        }

        // 精确测量多行标签的最大宽度（按分语言字体 + 加粗）
        double MeasureLabelWidth(List<(string text, string fam)> fontList, double fontSize, bool bold)
        {
            double w = 0;
            foreach (var (text, fam) in fontList)
            {
                string f = string.IsNullOrEmpty(fam) ? "Segoe UI" : fam;
                try
                {
                    var typeface = new Typeface(new FontFamily(f), FontStyles.Normal,
                        bold ? FontWeights.Bold : FontWeights.SemiBold, FontStretches.Normal);
                    var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1.0);
                    w = Math.Max(w, ft.Width);
                }
                catch { w = Math.Max(w, text.Length * fontSize * 1.05); }
            }
            return w;
        }

        // 根据语言索引返回字体（0=中文，1=英文，2=第三语言）
        string FontForLineAndLang(Line line, int lang)
        {
            if (line == null) return "";
            string f = lang switch
            {
                0 => line.CnFont,
                1 => line.EnFont,
                2 => line.ThirdFont,
                _ => ""
            };
            if (string.IsNullOrEmpty(f)) f = line.LabelFont;
            return f;
        }

        // ---- 站点级样式解析（站点级覆盖优先，空/0 回退到线路级/默认）----
        string ResolveStationFont(Station s, Line line, int lang)
        {
            string sf = lang switch { 0 => s.CnFont, 1 => s.EnFont, 2 => s.ThirdFont, _ => "" };
            if (!string.IsNullOrEmpty(sf)) return sf;
            return FontForLineAndLang(line, lang);
        }
        double ResolveStationFontSize(Station s, Line line)
        {
            if (s.FontSize > 0) return s.FontSize;
            return line != null && line.FontSize > 0 ? line.FontSize : 12;
        }
        bool ResolveStationBold(Station s, Line line)
        {
            if (s.FontBold) return true;
            return line != null && line.FontBold;
        }
        bool ResolveStationOutline(Station s, Line line)
        {
            if (!s.FontOutline) return false;
            return line == null || line.FontOutline;
        }
        Color ResolveStationColor(Station s, Line line)
        {
            if (!string.IsNullOrEmpty(s.FontColor)) return ParseColor(s.FontColor);
            return Color.FromRgb(0x1f, 0x27, 0x33);
        }

        // ---- 文字排布：线路方向判定 + 防遮挡 ----
        // 返回线路在该站处的近似切向向量（屏幕坐标，未归一化）。用于决定标签避让方向。
        // 东西走向（切向接近水平）→ 标签放线路侧上方/下方；南北走向 → 标签放左右侧。
        Point? ComputeLineTangent(Station s)
        {
            Point? prev = null, next = null;
            foreach (var lid in s.Lines)
            {
                if (!_lines.TryGetValue(lid, out var line)) continue;
                var order = line.StationOrder;
                int idx = order.IndexOf(s.Id);
                if (idx >= 0)
                {
                    if (idx > 0 && _renderStationPos.TryGetValue(order[idx - 1], out var pp)) prev = pp;
                    if (idx + 1 < order.Count && _renderStationPos.TryGetValue(order[idx + 1], out var pn)) next = pn;
                    if (prev != null || next != null) break;
                }
                // 支线
                foreach (var br in line.Branches)
                {
                    var bo = new List<string> { br.Junction };
                    bo.AddRange(br.StationOrder);
                    int bidx = bo.IndexOf(s.Id);
                    if (bidx < 0) continue;
                    if (bidx > 0 && _renderStationPos.TryGetValue(bo[bidx - 1], out var bp)) prev = bp;
                    if (bidx + 1 < bo.Count && _renderStationPos.TryGetValue(bo[bidx + 1], out var bn)) next = bn;
                    if (prev != null || next != null) goto done;
                }
            }
        done:
            if (prev == null && next == null) return null;
            Point p = _renderStationPos[s.Id];
            if (prev != null && next != null)
                return new Point(next.Value.X - prev.Value.X, next.Value.Y - prev.Value.Y);
            if (prev != null) return new Point(p.X - prev.Value.X, p.Y - prev.Value.Y);
            return new Point(next.Value.X - p.X, next.Value.Y - p.Y);
        }

        // 标签布局结果：放置位置、是否翻转、是否水平放置
        struct LabelLayout
        {
            public double Left;      // 文字框左上角 X（屏幕）
            public double Top;       // 文字框左上角 Y（屏幕）
            public bool Flip;        // 是否放站点左侧
            public bool Horizontal;  // 是否放站点上方（东西走向线路）
        }

        // 计算单个站点的标签布局。
        // 规则：站名固定在站点的避让侧（东西向放上方/下方，其余放左右），位置由线路方向一次性确定；
        //       若该侧与已放置的标签遮挡，则翻转到另一侧（另一侧仍遮挡则保持默认侧，不再移动）。
        // 文字位置固定，不做动态错位漂移。
        // 返回 null 表示站点在屏幕外、标签与视口完全无交集，应跳过绘制。
        LabelLayout? ComputeLabelLayout(Station s, Point p, double circleR, double fontSize,
            double estW, double totalH, List<string> labelLines)
        {
            double gap = circleR + 6;
            var tan = ComputeLineTangent(s);
            bool horizontal = tan.HasValue && Math.Abs(tan.Value.X) > Math.Abs(tan.Value.Y) * 1.4;

            double canvasW = MapCanvas.ActualWidth > 0 ? MapCanvas.ActualWidth : 2000;
            double canvasH = MapCanvas.ActualHeight > 0 ? MapCanvas.ActualHeight : 2000;
            bool haveViewport = MapCanvas.ActualWidth > 0 && MapCanvas.ActualHeight > 0;

            // 计算「默认侧」与「另一侧」两种候选位置（固定，不再移动）
            Rect Primary, Alt;
            bool primaryFlip, altFlip;
            if (horizontal)
            {
                // 东西走向线路：文字放站点上方；另一侧为下方
                primaryFlip = false;
                Primary = new Rect(p.X - estW / 2, p.Y - totalH - circleR - gap, estW, totalH);
                Alt = new Rect(p.X - estW / 2, p.Y + circleR + gap, estW, totalH);
            }
            else
            {
                // 南北走向（或无法判定）：默认右侧；另一侧为左侧
                primaryFlip = p.X + gap + estW > canvasW - 6;
                if (primaryFlip)
                {
                    Primary = new Rect(p.X - gap - estW, p.Y - totalH / 2, estW, totalH);
                    Alt = new Rect(p.X + gap, p.Y - totalH / 2, estW, totalH);
                }
                else
                {
                    Primary = new Rect(p.X + gap, p.Y - totalH / 2, estW, totalH);
                    Alt = new Rect(p.X - gap - estW, p.Y - totalH / 2, estW, totalH);
                }
            }
            altFlip = !primaryFlip;

            // 候选 1 = 默认侧；候选 2 = 另一侧。只要当前侧被遮挡就翻到另一侧，不再逐像素移动。
            Rect chosen = Primary; bool flip = primaryFlip;
            if (OverlapsAnyLabel(s.Id, Primary, labelLines) && !OverlapsAnyLabel(s.Id, Alt, labelLines))
            { chosen = Alt; flip = altFlip; }

            double left = chosen.X, top = chosen.Y;

            // 视口裁剪：标签框与屏幕完全无交集时直接跳过。
            // （否则下方「夹紧画布边界」会把屏幕外站点的站名拖进视野，产生大量不相关文字）
            if (haveViewport && !new Rect(left, top, estW, totalH)
                    .IntersectsWith(new Rect(0, 0, canvasW, canvasH)))
                return null;

            // 夹紧画布边界（仅把越界的标签压回边缘，不做方向性移动）
            if (left < 4) left = 4;
            if (left + estW > canvasW - 4) left = canvasW - 4 - estW;
            if (top < 2) top = 2;
            if (top + totalH > canvasH - 2) top = canvasH - 2 - totalH;

            return new LabelLayout { Left = left, Top = top, Flip = flip, Horizontal = horizontal };
        }

        bool OverlapsAnyLabel(string selfId, Rect r, List<string> labelLines)
        {
            foreach (var kv in _placedLabels)
            {
                if (kv.Key == selfId) continue;
                if (kv.Value.IntersectsWith(r)) return true;
            }
            return false;
        }

        void DrawMeasure()
        {
            if (_measure.Count == 0) return;
            var pts = _measure.Select(m => GeoToScreen(m.lng, m.lat)).ToList();
            var fig = new PathFigure { StartPoint = pts[0] };
            foreach (var p in pts.Skip(1)) fig.Segments.Add(new LineSegment(p, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig);
            var path = new System.Windows.Shapes.Path { Data = pg, Stroke = new SolidColorBrush(Color.FromRgb(0xe4, 0, 0x2b)), StrokeThickness = 2.5, StrokeDashArray = new DoubleCollection { 6, 4 } };
            MapCanvas.Children.Add(path);
            foreach (var p in pts)
            {
                var e = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xe4, 0, 0x2b)), Stroke = Brushes.White, StrokeThickness = 1.5 };
                Canvas.SetLeft(e, p.X - 4); Canvas.SetTop(e, p.Y - 4);
                MapCanvas.Children.Add(e);
            }
            if (pts.Count >= 2)
            {
                double total = 0;
                for (int i = 0; i < _measure.Count - 1; i++)
                    total += MapMath.Haversine(_measure[i].lng, _measure[i].lat, _measure[i + 1].lng, _measure[i + 1].lat);
                var mid = pts[pts.Count / 2];
                var tb = new TextBlock { Text = FormatDist(total), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xe4, 0, 0x2b)) };
                Canvas.SetLeft(tb, mid.X + 6); Canvas.SetTop(tb, mid.Y - 22);
                MapCanvas.Children.Add(tb);
            }
        }

        void DrawBox()
        {
            if (!_boxSelect || _boxStart == null || _boxCurrent == null) return;
            var a = GeoToScreen(_boxStart.Value.lng, _boxStart.Value.lat);
            var b = GeoToScreen(_boxCurrent.Value.lng, _boxCurrent.Value.lat);
            var r = new Rectangle
            {
                Width = Math.Abs(a.X - b.X), Height = Math.Abs(a.Y - b.Y),
                Fill = new SolidColorBrush(Color.FromArgb(0x20, 0x0f, 0x7d, 0xc2)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x0f, 0x7d, 0xc2)),
                StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 5, 4 }
            };
            Canvas.SetLeft(r, Math.Min(a.X, b.X)); Canvas.SetTop(r, Math.Min(a.Y, b.Y));
            MapCanvas.Children.Add(r);
        }

        // ================= 示意图渲染（网格对齐 + 共线并置） =================
        void DrawSchematic()
        {
            if (_schShowMapBg && _mode == "schematic") DrawSchematicMapBackground();
            if (_schGrid) DrawSchematicGrid();

            // 收集所有需要绘制的“线路段”（含主线与支线），用于共线检测
            var segments = BuildSchematicSegments();

            // 共线并置偏移计算
            ApplyColinearOffsets(segments);

            // 绘制
            foreach (var seg in segments)
            {
                var color = ParseColor(seg.Color);
                double lw = seg.Width > 0 ? seg.Width + 1 : 6;
                var pts = seg.RenderPoints.Select(p => SchToScreen(p)).ToList();
                DrawSchematicPolyline(pts, color, lw);
            }

            // 先收集所有站点示意屏幕位置（供标签方向避让与防遮挡）
            foreach (var s in _stations.Values)
                if (_schematicPos.ContainsKey(s.Id)) _renderStationPos[s.Id] = SchToScreen(_schematicPos[s.Id]);

            foreach (var s in _stations.Values)
            {
                if (!_schematicPos.ContainsKey(s.Id)) continue;
                var p = _renderStationPos[s.Id];
                AddStationShape(s, p, s.IsTransfer ? 10 : 7, s.IsTransfer ? 2.4 : 2, false);
            }
        }

        // 示意图线段（用于共线检测）
        class SchSeg
        {
            public string LineId;
            public string Color;
            public double Width;
            public List<Point> Points;       // 原始示意坐标（未偏移）
            public List<Point> RenderPoints; // 应用偏移后的坐标
        }

        List<SchSeg> BuildSchematicSegments()
        {
            var list = new List<SchSeg>();
            foreach (var line in _lines.Values)
            {
                var color = EffectiveLineColor(line);
                var trunk = line.StationOrder.Where(id => _schematicPos.ContainsKey(id))
                    .Select(id => _schematicPos[id]).ToList();
                if (trunk.Count >= 2) list.Add(new SchSeg { LineId = line.Id, Color = color, Width = line.LineWidth, Points = trunk, RenderPoints = new List<Point>(trunk) });
                foreach (var br in line.Branches)
                {
                    var order = new List<string> { br.Junction };
                    order.AddRange(br.StationOrder);
                    var bpts = order.Where(id => _schematicPos.ContainsKey(id)).Select(id => _schematicPos[id]).ToList();
                    if (bpts.Count >= 2) list.Add(new SchSeg { LineId = line.Id, Color = color, Width = line.LineWidth, Points = bpts, RenderPoints = new List<Point>(bpts) });
                }
            }
            return list;
        }

        // 共线检测与并置偏移：共享连续点的线路段在垂直方向并置（横线上下并置，纵线左右并置）
        void ApplyColinearOffsets(List<SchSeg> segments)
        {
            double spacing = 7; // 共线并置间距（示意坐标单位）
            for (int i = 0; i < segments.Count; i++)
            {
                var a = segments[i];
                if (!_lines.TryGetValue(a.LineId, out var la) || !la.ColinearOffset) continue;
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var b = segments[j];
                    if (a.LineId == b.LineId) continue;
                    if (!_lines.TryGetValue(b.LineId, out var lb) || !lb.ColinearOffset) continue;

                    // 找到共享的连续点对（a[i]↔b[j] 起）
                    for (int ai = 0; ai < a.Points.Count - 1; ai++)
                    {
                        for (int bj = 0; bj < b.Points.Count - 1; bj++)
                        {
                            bool same = Near(a.Points[ai], b.Points[bj]) && Near(a.Points[ai + 1], b.Points[bj + 1]);
                            bool reverse = Near(a.Points[ai], b.Points[bj + 1]) && Near(a.Points[ai + 1], b.Points[bj]);
                            if (!same && !reverse) continue;
                            // 方向向量
                            var dir = new Point(a.Points[ai + 1].X - a.Points[ai].X, a.Points[ai + 1].Y - a.Points[ai].Y);
                            double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                            if (len < 1e-6) continue;
                            double nx = -dir.Y / len, ny = dir.X / len; // 法向量（横线→上下，纵线→左右）
                            // a 向法向量一侧，b 向另一侧
                            a.RenderPoints[ai] = new Point(a.Points[ai].X + nx * spacing, a.Points[ai].Y + ny * spacing);
                            a.RenderPoints[ai + 1] = new Point(a.Points[ai + 1].X + nx * spacing, a.Points[ai + 1].Y + ny * spacing);
                            b.RenderPoints[bj] = new Point(b.Points[bj].X - nx * spacing, b.Points[bj].Y - ny * spacing);
                            if (reverse)
                                b.RenderPoints[bj + 1] = new Point(b.Points[bj + 1].X - nx * spacing, b.Points[bj + 1].Y - ny * spacing);
                            else
                                b.RenderPoints[bj + 1] = new Point(b.Points[bj + 1].X - nx * spacing, b.Points[bj + 1].Y - ny * spacing);
                        }
                    }
                }
            }
        }
        bool Near(Point a, Point b) => Dist(a, b) < 0.01;
        double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        void DrawSchematicGrid()
        {
            double gs = _schGridSize * _schScale;
            if (gs < 8) return;
            double w = MapCanvas.ActualWidth, h = MapCanvas.ActualHeight;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x99, 0xa7, 0xb5)), 0.6);
            for (double x = _schOffX % gs; x < w; x += gs)
                MapCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = pen.Brush, StrokeThickness = pen.Thickness });
            for (double y = _schOffY % gs; y < h; y += gs)
                MapCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = pen.Brush, StrokeThickness = pen.Thickness });
        }

        void DrawSchematicMapBackground()
        {
            var geoIds = _schematicPos.Keys.Where(id => _stations.ContainsKey(id)).ToList();
            if (geoIds.Count < 2) return;
            int refZ = _zoom;
            var src = geoIds.Select(id => (MapMath.LngToWorldX(_stations[id].Lng, refZ), MapMath.LatToWorldY(_stations[id].Lat, refZ))).ToList();
            var dst = geoIds.Select(id => _schematicPos[id]).ToList();
            double sx = 0, sy = 0, tx = 0, ty = 0, n = geoIds.Count;
            for (int i = 0; i < n; i++) { sx += src[i].Item1; sy += src[i].Item2; tx += dst[i].X; ty += dst[i].Y; }
            sx /= n; sy /= n; tx /= n; ty /= n;
            double numA = 0, denA = 0, numB = 0, denB = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = src[i].Item1 - sx, dy = src[i].Item2 - sy;
                numA += dx * (dst[i].X - tx); denA += dx * dx;
                numB += dy * (dst[i].Y - ty); denB += dy * dy;
            }
            double kx = denA > 1e-9 ? numA / denA : 0;
            double ky = denB > 1e-9 ? numB / denB : 0;
            if (Math.Abs(kx) < 1e-9 || Math.Abs(ky) < 1e-9) return;
            double ox = tx - kx * sx, oy = ty - ky * sy;

            double w = MapCanvas.ActualWidth, h = MapCanvas.ActualHeight;
            double tileWorld = 256;
            foreach (var kv in _tileCache)
            {
                var parts = kv.Key.Split('/');
                if (parts.Length < 4) continue;
                int tz = int.Parse(parts[1]), txidx = int.Parse(parts[2]), tyidx = int.Parse(parts[3]);
                if (tz != refZ) continue;
                double wx = txidx * tileWorld, wy = tyidx * tileWorld;
                var p = SchToScreen(new Point(wx * kx + ox, wy * ky + oy));
                double tw = tileWorld * kx * _schScale, th = tileWorld * ky * _schScale;
                if (p.X + tw < 0 || p.Y + th < 0 || p.X > w || p.Y > h) continue;
                var img = new Image { Source = kv.Value, Width = tw, Height = th, Opacity = _schMapBgOpacity, IsHitTestVisible = false };
                Canvas.SetLeft(img, p.X); Canvas.SetTop(img, p.Y);
                MapCanvas.Children.Add(img);
            }
        }

        void DrawSchematicPolyline(List<Point> pts, Color color, double width)
        {
            if (pts.Count < 2) return;
            var oct = PathBuilder.Octilinear(pts, 14);
            var fig = new PathFigure { StartPoint = oct[0] };
            foreach (var p in oct.Skip(1)) fig.Segments.Add(new LineSegment(p, true));
            var pg = new PathGeometry(); pg.Figures.Add(fig);
            MapCanvas.Children.Add(new System.Windows.Shapes.Path { Data = pg, Stroke = new SolidColorBrush(color), StrokeThickness = width, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = 0.92 });
        }

        void UpdateLegend()
        {
            LegendPanel.Children.Clear();
            var dot = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center };
            LegendPanel.Children.Add(Row(dot, "普通站"));
            var td = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center };
            var tdi = new Ellipse { Width = 4, Height = 4, Fill = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var tg = new Grid { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
            tg.Children.Add(td); tg.Children.Add(tdi);
            LegendPanel.Children.Add(Row(tg, "换乘站"));
            foreach (var line in _lines.Values)
            {
                var sw = new Rectangle { Width = 24, Height = 4, Fill = new SolidColorBrush(ParseColor(EffectiveLineColor(line))), RadiusX = 2, RadiusY = 2, VerticalAlignment = VerticalAlignment.Center };
                var label = line.Name;
                if (!string.IsNullOrEmpty(line.LogoText)) label += $"  [{line.LogoText}]";
                LegendPanel.Children.Add(Row(sw, label));
            }
            LegendPanel.Children.Add(new TextBlock { Text = "实线=地下 · 虚线=地上 · 点线=切换", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });
        }
        StackPanel Row(UIElement swatch, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            sp.Children.Add(swatch);
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        // ================= 交互 =================
        void Station_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var ell = (Ellipse)sender; var id = (string)ell.Tag;
            e.Handled = true;
            if (_mode == "schematic")
            {
                if (_tool == "select") { _dragStationId = id; _panning = false; MapCanvas.CaptureMouse(); }
                return;
            }
            if (e.ClickCount == 2) { _dragStationId = null; if (MapCanvas.IsMouseCaptured) MapCanvas.ReleaseMouseCapture(); EditStation(id); return; }
            if (_tool == "drawLine")
            {
                if (_drawSubMode == "branch") BranchClick(id);
                else AppendToLine(id);
            }
            else if (_tool == "select") { SelectStation(id); _dragStationId = id; _lastMouse = e.GetPosition(MapCanvas); MapCanvas.CaptureMouse(); }
            else SelectStation(id);
        }

        void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(MapCanvas);
            if (_mode == "schematic") return;
            _rightPanning = true;
            _rightDownPos = p;
            _rightMoved = false;
            _lastMouse = p;
            MapCanvas.CaptureMouse();
            e.Handled = true;
        }

        void MapCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mode == "schematic") return;
            if (MapCanvas.IsMouseCaptured) MapCanvas.ReleaseMouseCapture();
            if (_rightPanning)
            {
                _rightPanning = false;
                // 仅当没有发生拖动（移动距离小于阈值）时才视为右键单击弹出菜单
                if (!_rightMoved)
                    ShowContextMenu(e.GetPosition(MapCanvas));
            }
            _rightMoved = false;
        }

        void ShowContextMenu(Point p)
        {
            var st = FindStationAt(p, 16);
            var segHit = FindSegmentAt(p);
            var menu = new ContextMenu();
            if (st != null)
            {
                var editItem = new MenuItem { Header = "编辑属性" };
                editItem.Click += (s2, e2) => EditStation(st.Id);
                menu.Items.Add(editItem);
                var delSkip = new MenuItem { Header = "删除（跳过，保持线路连接）" };
                delSkip.Click += (s2, e2) => DeleteStationSkip(st.Id);
                menu.Items.Add(delSkip);
                var delSplit = new MenuItem { Header = "删除（断开，两侧各成线路）" };
                delSplit.Click += (s2, e2) => DeleteStationSplit(st.Id);
                menu.Items.Add(delSplit);
            }
            else if (segHit != null)
            {
                var addAnchor = new MenuItem { Header = "在此处添加钢笔锚点（曲线手柄）" };
                addAnchor.Click += (s2, e2) =>
                {
                    var (lng, lat) = ScreenToGeo(p);
                    PushUndo();
                    segHit.CurveAnchors ??= new List<CurveAnchor>();
                    double dlng = 0.0004;
                    segHit.CurveAnchors.Add(new CurveAnchor { Lng = lng, Lat = lat, InDlng = -dlng, InDlat = 0, OutDlng = dlng, OutDlat = 0, Mode = "smooth" });
                    Render(); SaveState();
                };
                menu.Items.Add(addAnchor);
                if (segHit.CurveAnchors != null && segHit.CurveAnchors.Count > 0)
                {
                    var rmAnchor = new MenuItem { Header = "移除最近的钢笔锚点" };
                    rmAnchor.Click += (s2, e2) => { PushUndo(); segHit.CurveAnchors.RemoveAt(segHit.CurveAnchors.Count - 1); Render(); SaveState(); };
                    menu.Items.Add(rmAnchor);
                }
                if (segHit.Anchors != null && segHit.Anchors.Count > 0)
                {
                    var rmAnchor = new MenuItem { Header = "移除最近的绕行锚点" };
                    rmAnchor.Click += (s2, e2) => { PushUndo(); segHit.Anchors.RemoveAt(segHit.Anchors.Count - 1); Render(); SaveState(); };
                    menu.Items.Add(rmAnchor);
                }
            }
            else
            {
                var addItem = new MenuItem { Header = "在此新建普通站" };
                addItem.Click += (s2, e2) => { var (lng, lat) = ScreenToGeo(p); AddStation(lng, lat, false); };
                menu.Items.Add(addItem);
                var addTr = new MenuItem { Header = "在此新建换乘站" };
                addTr.Click += (s2, e2) => { var (lng, lat) = ScreenToGeo(p); AddStation(lng, lat, true); };
                menu.Items.Add(addTr);
                if (_measure.Count > 0)
                {
                    var clearM = new MenuItem { Header = "清除测距" };
                    clearM.Click += (s2, e2) => { _measure.Clear(); Render(); };
                    menu.Items.Add(clearM);
                }
            }
            menu.PlacementTarget = MapCanvas;
            menu.IsOpen = true;
        }

        Station FindStationAt(Point screen, double threshold = 16)
        {
            Station best = null; double bestD = threshold;
            foreach (var s in _stations.Values)
            {
                var p = _mode == "schematic" && _schematicPos.ContainsKey(s.Id) ? SchToScreen(_schematicPos[s.Id]) : GeoToScreen(s.Lng, s.Lat);
                double d = PathBuilder.Dist(p, screen);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        Segment FindSegmentAt(Point screen)
        {
            Segment best = null; double bestD = 10;
            void Test(List<Segment> segs)
            {
                if (segs == null) return;
                foreach (var seg in segs)
                {
                    if (!_stations.TryGetValue(seg.A, out var sa) || !_stations.TryGetValue(seg.B, out var sb)) continue;
                    var way = new List<Point> { GeoToScreen(sa.Lng, sa.Lat) };
                    if (seg.Anchors != null) foreach (var an in seg.Anchors) way.Add(GeoToScreen(an.Lng, an.Lat));
                    way.Add(GeoToScreen(sb.Lng, sb.Lat));
                    for (int i = 0; i < way.Count - 1; i++)
                    {
                        double d = DistToSegment(screen, way[i], way[i + 1]);
                        if (d < bestD) { bestD = d; best = seg; }
                    }
                }
            }
            foreach (var line in _lines.Values)
            {
                Test(line.Segments);
                foreach (var br in line.Branches) Test(br.Segments);
            }
            return best;
        }
        double DistToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (dx == 0 && dy == 0) return PathBuilder.Dist(p, a);
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            double px = a.X + t * dx, py = a.Y + t * dy;
            return PathBuilder.Dist(p, new Point(px, py));
        }

        void MapCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(MapCanvas);
            MapCanvas.Focus();
            if (e.ChangedButton != MouseButton.Left) return;

            if (_mode == "schematic")
            {
                _panning = true; _lastMouse = p; MapCanvas.CaptureMouse();
                return;
            }
            if (_boxSelect)
            {
                var (lng, lat) = ScreenToGeo(p);
                _boxStart = (lng, lat); _boxCurrent = (lng, lat);
                return;
            }
            if (_tool == "station")
            {
                // 设站工具：测距子模式优先，否则放置站点
                if (_measureActive)
                {
                    var (lng, lat) = ScreenToGeo(p);
                    _measure.Add((lng, lat)); Render(); BuildStationToolPanel();
                }
                else
                {
                    var (lng, lat) = ScreenToGeo(p);
                    AddStation(lng, lat, _placeTransfer);
                }
            }
            else if (_tool == "drawLine")
            {
                if (_drawSubMode == "branch")
                {
                    SetStatus("请点击站点：先点分叉站，再点支线站点（点击已有站点）");
                }
                else if (_drawSubMode == "anchor")
                {
                    // 锚点子模式：点击线段附近添加钢笔式锚点
                    var segHit = FindSegmentAt(p);
                    if (segHit != null)
                    {
                        var (lng, lat) = ScreenToGeo(p);
                        PushUndo();
                        segHit.CurveAnchors ??= new List<CurveAnchor>();
                        // 初始手柄：入向/出向沿水平方向反/正各偏移一段
                        double dlng = 0.0004;
                        segHit.CurveAnchors.Add(new CurveAnchor
                        {
                            Lng = lng, Lat = lat,
                            InDlng = -dlng, InDlat = 0,
                            OutDlng = dlng, OutDlat = 0,
                            Mode = "smooth"
                        });
                        Render(); SaveState();
                        SetStatus("已添加钢笔锚点，拖动手柄调整曲线");
                    }
                    else SetStatus("请点击线路附近添加锚点");
                }
                else
                {
                    var st = FindStationAt(p);
                    if (st != null) AppendToLine(st.Id); else SetStatus("请点击已有站点（先用设站工具放置）");
                }
            }
            else if (_tool == "select")
            {
                var st = FindStationAt(p);
                if (st != null) SelectStation(st.Id);
                else { SelectStation(null); _panning = true; _lastMouse = p; MapCanvas.CaptureMouse(); }
            }
        }

        void MapCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(MapCanvas);
            if (_mode == "schematic")
            {
                if (_panning) { _schOffX += p.X - _lastMouse.X; _schOffY += p.Y - _lastMouse.Y; _lastMouse = p; Render(); }
                else if (_dragStationId != null)
                {
                    var w = ScreenToSch(p);
                    if (_schGrid)
                    {
                        w = new Point(Math.Round(w.X / _schGridSize) * _schGridSize, Math.Round(w.Y / _schGridSize) * _schGridSize);
                        SnapSchematicStation(_dragStationId, ref w);
                    }
                    else
                        w = new Point(Math.Round(w.X / 20) * 20, Math.Round(w.Y / 20) * 20);
                    _schematicPos[_dragStationId] = w; Render();
                }
                return;
            }
            if (_boxSelect && _boxStart != null) { var (lng, lat) = ScreenToGeo(p); _boxCurrent = (lng, lat); Render(); return; }
            if (_rightPanning)
            {
                // 判定是否发生了实质拖动（超过 4 像素视为拖动）
                if (!_rightMoved && Math.Abs(p.X - _rightDownPos.X) + Math.Abs(p.Y - _rightDownPos.Y) > 4)
                    _rightMoved = true;
                var (l0, la0) = ScreenToGeo(_lastMouse);
                var (l1, la1) = ScreenToGeo(p);
                _centerLng -= (l1 - l0); _centerLat -= (la1 - la0);
                _lastMouse = p; Render();
                return;
            }
            if (_dragAnchorSegKey != null)
            {
                var (lng, lat) = ScreenToGeo(p);
                var seg = GetAnchorSegment(_dragAnchorSegKey);
                if (seg != null && seg.Anchors != null && _dragAnchorIdx >= 0 && _dragAnchorIdx < seg.Anchors.Count)
                {
                    seg.Anchors[_dragAnchorIdx].Lng = lng;
                    seg.Anchors[_dragAnchorIdx].Lat = lat;
                    Render();
                }
                return;
            }
            if (_dragCurveSegKey != null)
            {
                var (lng, lat) = ScreenToGeo(p);
                var seg = GetCurveSegment(_dragCurveSegKey);
                if (seg != null && seg.CurveAnchors != null && _dragCurveAnchorIdx >= 0 && _dragCurveAnchorIdx < seg.CurveAnchors.Count)
                {
                    var ca = seg.CurveAnchors[_dragCurveAnchorIdx];
                    if (_dragCurveCtrl == 0)
                    {
                        // 移动锚点本体（手柄随之平移）
                        double dlng = lng - ca.Lng, dlat = lat - ca.Lat;
                        ca.Lng = lng; ca.Lat = lat;
                        ca.InDlng += dlng; ca.InDlat += dlat;
                        ca.OutDlng += dlng; ca.OutDlat += dlat;
                    }
                    else if (_dragCurveCtrl == 1)
                    {
                        // 拖动入向手柄
                        ca.InDlng = lng - ca.Lng; ca.InDlat = lat - ca.Lat;
                        // 平滑模式：反向带动出向手柄
                        if (ca.Mode == "smooth") { ca.OutDlng = -ca.InDlng; ca.OutDlat = -ca.InDlat; }
                    }
                    else if (_dragCurveCtrl == 2)
                    {
                        // 拖动出向手柄
                        ca.OutDlng = lng - ca.Lng; ca.OutDlat = lat - ca.Lat;
                        if (ca.Mode == "smooth") { ca.InDlng = -ca.OutDlng; ca.InDlat = -ca.OutDlat; }
                    }
                    Render();
                }
                return;
            }
            if (_dragStationId != null)
            {
                var (lng, lat) = ScreenToGeo(p);
                var s = _stations[_dragStationId]; s.Lng = lng; s.Lat = lat; Render();
                return;
            }
            if (_panning)
            {
                var (l0, la0) = ScreenToGeo(_lastMouse);
                var (l1, la1) = ScreenToGeo(p);
                _centerLng -= (l1 - l0); _centerLat -= (la1 - la0);
                _lastMouse = p; Render();
            }
        }

        void MapCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_boxSelect && _boxStart != null)
            {
                double west = Math.Min(_boxStart.Value.lng, _boxCurrent.Value.lng);
                double east = Math.Max(_boxStart.Value.lng, _boxCurrent.Value.lng);
                double south = Math.Min(_boxStart.Value.lat, _boxCurrent.Value.lat);
                double north = Math.Max(_boxStart.Value.lat, _boxCurrent.Value.lat);
                if (_offlineBoxes is { } ob)
                {
                    ob.west.Text = west.ToString("F5"); ob.east.Text = east.ToString("F5");
                    ob.south.Text = south.ToString("F5"); ob.north.Text = north.ToString("F5");
                }
                _boxSelect = false; _boxStart = null; _boxCurrent = null;
                SetStatus("已框选区域，可点击「开始下载」");
                Render(); return;
            }
            _panning = false; _dragStationId = null; _rightPanning = false;
            if (_dragAnchorSegKey != null) { _dragAnchorSegKey = null; _dragAnchorIdx = -1; SaveState(); }
            if (_dragCurveSegKey != null) { _dragCurveSegKey = null; _dragCurveAnchorIdx = -1; SaveState(); }
            if (MapCanvas.IsMouseCaptured) MapCanvas.ReleaseMouseCapture();
            SaveState();
        }

        void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_mode == "schematic")
            {
                double factor = e.Delta > 0 ? 1.12 : 0.89;
                var p = e.GetPosition(MapCanvas);
                var before = ScreenToSch(p);
                _schScale = Math.Max(0.1, Math.Min(8, _schScale * factor));
                _schOffX = p.X - before.X * _schScale;
                _schOffY = p.Y - before.Y * _schScale;
                Render();
                return;
            }
            int dz = e.Delta > 0 ? 1 : -1;
            int nz = Math.Max(3, Math.Min(19, _zoom + dz));
            if (nz == _zoom) return;
            var cur = e.GetPosition(MapCanvas);
            var (lng, lat) = ScreenToGeo(cur);
            _zoom = nz;
            double wx = MapMath.LngToWorldX(lng, _zoom), wy = MapMath.LatToWorldY(lat, _zoom);
            double newCx = wx + MapCanvas.ActualWidth / 2 - cur.X;
            double newCy = wy + MapCanvas.ActualHeight / 2 - cur.Y;
            _centerLng = MapMath.WorldXToLng(newCx, _zoom);
            _centerLat = MapMath.WorldYToLat(newCy, _zoom);
            Render();
        }

        void FitToStations()
        {
            if (_stations.Count == 0) return;
            var lngs = _stations.Values.Select(s => s.Lng).ToList();
            var lats = _stations.Values.Select(s => s.Lat).ToList();
            double midLng = (lngs.Min() + lngs.Max()) / 2, midLat = (lats.Min() + lats.Max()) / 2;
            _centerLng = midLng; _centerLat = midLat;
            // 估算缩放级别
            double span = Math.Max(lngs.Max() - lngs.Min(), lats.Max() - lats.Min());
            int z = span > 0.5 ? 10 : span > 0.2 ? 11 : span > 0.08 ? 12 : span > 0.03 ? 13 : 14;
            _zoom = z;
            Render();
        }

        // ================= 站点 =================
        void AddStation(double lng, double lat, bool transfer)
        {
            if (!CheckWithinProject(lng, lat)) return;
            var dlg = new StationDialog("", "", "", "", transfer) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                PushUndo();
                var s = new Station { NameCn = dlg.NameCn, NameEn = dlg.NameEn, NameThird = dlg.NameThird, ThirdLabel = dlg.ThirdLabel, Transfer = transfer, Lng = lng, Lat = lat };
                _stations[s.Id] = s;
                SelectStation(s.Id);
                Render(); SaveState();
            }
        }
        void EditStation(string id)
        {
            if (!_stations.TryGetValue(id, out var s)) return;
            var dlg = new StationDialog(s.NameCn, s.NameEn, s.NameThird, s.ThirdLabel, s.Transfer) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                PushUndo();
                s.NameCn = dlg.NameCn; s.NameEn = dlg.NameEn; s.NameThird = dlg.NameThird; s.ThirdLabel = dlg.ThirdLabel; s.Transfer = dlg.Transfer;
                SelectStation(id); Render(); SaveState();
            }
        }
        void DeleteStation(string id) => DeleteStationSkip(id);

        void DeleteStationSkip(string id)
        {
            if (!_stations.ContainsKey(id)) return;
            PushUndo();
            foreach (var line in _lines.Values.ToList())
            {
                if (line.StationOrder.Contains(id)) { line.StationOrder.RemoveAll(x => x == id); RebuildSegments(line.Id); }
                CleanBranchesForStation(line, id);
            }
            _stations.Remove(id);
            _schematicPos.Remove(id);
            if (_selectedStationId == id) _selectedStationId = null;
            SelectStation(null); Render(); BuildLinesPanelIfActive(); SaveState();
        }
        void RebuildBranchSegments(Branch br)
        {
            var seq = new List<string> { br.Junction };
            seq.AddRange(br.StationOrder);
            var segs = new List<Segment>();
            for (int i = 0; i < seq.Count - 1; i++)
            {
                var a = seq[i]; var b = seq[i + 1];
                var old = br.Segments.FirstOrDefault(s => (s.A == a && s.B == b) || (s.A == b && s.B == a));
                segs.Add(new Segment { A = a, B = b, Type = old != null ? old.Type : "underground", Anchors = old?.Anchors ?? new List<GeoPoint>() });
            }
            br.Segments = segs;
        }
        void CleanBranchesForStation(Line line, string id)
        {
            line.Branches.RemoveAll(br => br.Junction == id);
            foreach (var br in line.Branches)
                if (br.StationOrder.Contains(id))
                {
                    br.StationOrder.RemoveAll(x => x == id);
                    RebuildBranchSegments(br);
                }
        }
        void DeleteStationSplit(string id)
        {
            if (!_stations.ContainsKey(id)) return;
            PushUndo();
            foreach (var line in _lines.Values.ToList())
            {
                CleanBranchesForStation(line, id);
                int idx = line.StationOrder.IndexOf(id);
                if (idx < 0) continue;
                var before = line.StationOrder.Take(idx).ToList();
                var after = line.StationOrder.Skip(idx + 1).ToList();
                if (before.Count >= 2 && after.Count >= 2)
                {
                    line.StationOrder = before; RebuildSegments(line.Id);
                    var nl = new Line { Name = line.Name + "②", NameEn = line.NameEn, Color = line.Color, StationOrder = after };
                    _lines[nl.Id] = nl; RebuildSegments(nl.Id);
                    foreach (var sid in after)
                        if (_stations.TryGetValue(sid, out var s)) { s.Lines.Remove(line.Id); s.Lines.Add(nl.Id); }
                }
                else
                {
                    line.StationOrder.RemoveAll(x => x == id);
                    RebuildSegments(line.Id);
                }
            }
            _stations.Remove(id);
            _schematicPos.Remove(id);
            if (_selectedStationId == id) _selectedStationId = null;
            SelectStation(null); Render(); BuildLinesPanelIfActive(); SaveState();
        }
        void SelectStation(string id)
        {
            _selectedStationId = id;
            if (id == null || !_stations.TryGetValue(id, out _))
            {
                if (_selectInfoText != null) _selectInfoText.Text = "点击地图上的站点查看详情。";
                if (_tool == "select") ShowToolPanel("select");
                Render();
                return;
            }
            if (_selectInfoText != null) _selectInfoText.Text = GetStationInfoText();
            if (_tool == "select") ShowToolPanel("select");
            else if (_tool == "station") BuildStationToolPanel();
            Render();
        }
        void BuildLinesPanelIfActive()
        {
            if (_tool == "lines") BuildLinesPanel();
        }

        // ================= 线路 =================
        string NextLineName()
        {
            int i = 1; var used = new HashSet<string>(_lines.Values.Select(l => l.Name));
            while (used.Contains($"{i}号线")) i++;
            return $"{i}号线";
        }
        string CreateLine()
        {
            var line = new Line { Name = NextLineName(), Color = Palette[_lines.Count % Palette.Length].Hex };
            _lines[line.Id] = line;
            return line.Id;
        }
        void AppendToLine(string stationId)
        {
            if (_activeLineId == null || !_lines.TryGetValue(_activeLineId, out var line))
            {
                // 尚未开始一条线路：自动新建
                _activeLineId = CreateLine();
                _selectedLineId = _activeLineId;
                line = _lines[_activeLineId];
            }
            var order = line.StationOrder;
            if (order.Count > 0 && order[order.Count - 1] == stationId) { SetStatus("已连接该站，请选择下一站"); return; }
            PushUndo();
            if (_extendMode == "head")
            {
                order.Insert(0, stationId);
                line.Segments.Insert(0, new Segment { A = stationId, B = order[1], Type = _segTypeDraw });
            }
            else
            {
                order.Add(stationId);
                if (order.Count >= 2) line.Segments.Add(new Segment { A = order[order.Count - 2], B = stationId, Type = _segTypeDraw });
            }
            _stations[stationId].Lines.Add(_activeLineId);
            Render(); ShowToolPanel("drawLine"); SaveState();
            SetStatus($"已连接：{_stations[stationId].NameCn} → {line.Name}（{order.Count} 站）");
        }
        void StartExtend(string mode)
        {
            if (_selectedLineId == null) { SetStatus("请先选中一条线路"); return; }
            _activeLineId = _selectedLineId;
            _extendMode = mode;
            SetTool("drawLine");
            SetStatus(mode == "head" ? "延长模式：点击站点将添加到线路首端（完成后点「完成绘制」）" : "延长模式：点击站点将添加到线路末端（完成后点「完成绘制」）");
        }
        void UndoDraw()
        {
            if (_activeLineId == null || !_lines.TryGetValue(_activeLineId, out var line) || line.StationOrder.Count == 0) return;
            var removed = line.StationOrder[line.StationOrder.Count - 1];
            line.StationOrder.RemoveAt(line.StationOrder.Count - 1);
            if (line.Segments.Count > 0 && line.Segments[line.Segments.Count - 1].B == removed) line.Segments.RemoveAt(line.Segments.Count - 1);
            if (!line.StationOrder.Contains(removed) && !_lines.Values.Any(l => l.Id != line.Id && l.StationOrder.Contains(removed)))
                _stations[removed].Lines.Remove(_activeLineId);
            // 若线路不足两站且无支线，则不再是有效地铁线，删除该空线路
            if (line.StationOrder.Count < 2 && line.Branches.Count == 0)
            {
                _lines.Remove(line.Id);
                _activeLineId = null; _selectedLineId = null;
            }
            Render(); ShowToolPanel(_tool); SaveState();
        }

        // ================= 支线 =================
        void BranchClick(string id)
        {
            if (_activeLineId == null || !_lines.TryGetValue(_activeLineId, out var line)) return;
            if (_branchJunction == null)
            {
                if (!line.StationOrder.Contains(id)) { SetStatus("请点击本线路上已有的站点作为分叉点"); return; }
                _branchJunction = id;
                SetStatus($"分叉站：{_stations[id].NameCn}，继续点击支线站点");
            }
            else
            {
                if (id == _branchJunction) { SetStatus("支线站点不能与分叉站相同"); return; }
                if (_branchStations.Count > 0 && _branchStations[_branchStations.Count - 1] == id) { SetStatus("已连接该站，请选下一站"); return; }
                _branchStations.Add(id);
                SetStatus($"支线：{string.Join("→", _branchStations.Select(sid => _stations[sid].NameCn))}");
            }
        }
        void FinishBranch()
        {
            if (_activeLineId == null || !_lines.TryGetValue(_activeLineId, out var line)) { SetStatus("请先选中一条线路并绘制支线"); return; }
            if (_branchJunction == null || _branchStations.Count == 0) { SetStatus("请先点击分叉站并绘制至少一个支线站点"); return; }
            PushUndo();
            var br = new Branch { Junction = _branchJunction, StationOrder = new List<string>(_branchStations) };
            var seq = new List<string> { _branchJunction };
            seq.AddRange(_branchStations);
            for (int i = 0; i < seq.Count - 1; i++)
                br.Segments.Add(new Segment { A = seq[i], B = seq[i + 1], Type = _segTypeDraw });
            line.Branches.Add(br);
            foreach (var sid in _branchStations)
                if (_stations.TryGetValue(sid, out var s)) s.Lines.Add(line.Id);
            _branchJunction = null; _branchStations = new();
            Render(); BuildLinesPanelIfActive(); SaveState();
            SetStatus($"支线已添加到 {line.Name}");
        }

        void DeleteLine(string id)
        {
            if (!_lines.TryGetValue(id, out var line)) return;
            PushUndo();
            foreach (var sid in line.StationOrder)
                if (_stations.TryGetValue(sid, out var s)) s.Lines.Remove(id);
            foreach (var br in line.Branches)
                foreach (var sid in br.StationOrder)
                    if (_stations.TryGetValue(sid, out var s)) s.Lines.Remove(id);
            _lines.Remove(id);
            if (_activeLineId == id) _activeLineId = null;
            if (_selectedLineId == id) _selectedLineId = null;
            Render(); BuildLinesPanelIfActive(); SaveState();
        }
        void RemoveStationFromLine(string lineId, string stationId)
        {
            if (!_lines.TryGetValue(lineId, out var line)) return;
            PushUndo();
            line.StationOrder.RemoveAll(x => x == stationId);
            RebuildSegments(lineId);
            if (!_lines.Values.Any(l => l.StationOrder.Contains(stationId)))
                if (_stations.TryGetValue(stationId, out var s)) s.Lines.Remove(lineId);
            Render(); BuildLinesPanelIfActive(); SaveState();
        }
        void MoveStationInLine(string lineId, string stationId, int dir)
        {
            if (!_lines.TryGetValue(lineId, out var line)) return;
            int i = line.StationOrder.IndexOf(stationId);
            int j = i + dir;
            if (i < 0 || j < 0 || j >= line.StationOrder.Count) return;
            PushUndo();
            (line.StationOrder[i], line.StationOrder[j]) = (line.StationOrder[j], line.StationOrder[i]);
            RebuildSegments(lineId);
            Render(); BuildLinesPanelIfActive(); SaveState();
        }
        void RebuildSegments(string lineId)
        {
            var line = _lines[lineId];
            var segs = new List<Segment>();
            for (int i = 0; i < line.StationOrder.Count - 1; i++)
            {
                var a = line.StationOrder[i]; var b = line.StationOrder[i + 1];
                var old = line.Segments.FirstOrDefault(s => (s.A == a && s.B == b) || (s.A == b && s.B == a));
                segs.Add(new Segment { A = a, B = b, Type = old != null ? old.Type : "underground", Anchors = old?.Anchors ?? new List<GeoPoint>() });
            }
            line.Segments = segs;
        }
        void InsertStationBetween(string lineId, int segIndex, string newStationId)
        {
            if (!_lines.TryGetValue(lineId, out var line)) return;
            if (segIndex < 0 || segIndex >= line.Segments.Count) return;
            PushUndo();
            int pos = segIndex + 1;
            line.StationOrder.Insert(pos, newStationId);
            RebuildSegments(lineId);
            if (_stations.TryGetValue(newStationId, out var s)) s.Lines.Add(lineId);
            Render(); BuildLinesPanelIfActive(); SaveState();
        }
        void InsertNewStationBetween(string lineId, int segIndex)
        {
            if (!_lines.TryGetValue(lineId, out var line)) return;
            if (segIndex < 0 || segIndex >= line.Segments.Count) return;
            var seg = line.Segments[segIndex];
            if (!_stations.TryGetValue(seg.A, out var a) || !_stations.TryGetValue(seg.B, out var b)) return;
            double mlng = (a.Lng + b.Lng) / 2, mlat = (a.Lat + b.Lat) / 2;
            var dlg = new StationDialog("新站", "", "", "", false) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var s = new Station { NameCn = dlg.NameCn, NameEn = dlg.NameEn, NameThird = dlg.NameThird, ThirdLabel = dlg.ThirdLabel, Transfer = dlg.Transfer, Lng = mlng, Lat = mlat };
            _stations[s.Id] = s;
            InsertStationBetween(lineId, segIndex, s.Id);
        }
        void SplitLineAtSegment(string lineId, int segIndex)
        {
            if (!_lines.TryGetValue(lineId, out var line)) return;
            if (segIndex < 0 || segIndex >= line.Segments.Count) return;
            PushUndo();
            int cut = segIndex + 1;
            var head = line.StationOrder.Take(cut).ToList();
            var tail = line.StationOrder.Skip(cut).ToList();
            if (head.Count < 1 || tail.Count < 1) return;
            line.StationOrder = head; RebuildSegments(lineId);
            var nl = new Line { Name = line.Name + "②", NameEn = line.NameEn, Color = line.Color, StationOrder = tail };
            _lines[nl.Id] = nl; RebuildSegments(nl.Id);
            foreach (var sid in tail)
                if (_stations.TryGetValue(sid, out var s)) { s.Lines.Remove(lineId); s.Lines.Add(nl.Id); }
            Render(); BuildLinesPanelIfActive(); SaveState();
            SetStatus($"已断开：{line.Name} / {nl.Name}");
        }
        void ConnectLines(string lineAId, string lineBId)
        {
            if (!_lines.TryGetValue(lineAId, out var a) || !_lines.TryGetValue(lineBId, out var b) || a.Id == b.Id) return;
            PushUndo();
            if (a.StationOrder.Count > 0 && b.StationOrder.Count > 0 && a.StationOrder[a.StationOrder.Count - 1] == b.StationOrder[0])
            {
                foreach (var sid in b.StationOrder.Skip(1)) a.StationOrder.Add(sid);
                RebuildSegments(a.Id);
                foreach (var sid in b.StationOrder) if (_stations.TryGetValue(sid, out var s)) s.Lines.Remove(b.Id);
                foreach (var sid in a.StationOrder) if (_stations.TryGetValue(sid, out var s)) s.Lines.Add(a.Id);
                _lines.Remove(b.Id);
            }
            else if (a.StationOrder.Count > 0 && b.StationOrder.Count > 0 && b.StationOrder[b.StationOrder.Count - 1] == a.StationOrder[0])
            {
                foreach (var sid in a.StationOrder.Skip(1)) b.StationOrder.Add(sid);
                RebuildSegments(b.Id);
                foreach (var sid in a.StationOrder) if (_stations.TryGetValue(sid, out var s)) s.Lines.Remove(a.Id);
                foreach (var sid in b.StationOrder) if (_stations.TryGetValue(sid, out var s)) s.Lines.Add(b.Id);
                _lines.Remove(a.Id);
            }
            else { SetStatus("两条线路需首尾相连（共用端点站）才能合并"); return; }
            _selectedLineId = null; _activeLineId = null;
            Render(); BuildLinesPanelIfActive(); SaveState();
            SetStatus("线路已合并");
        }
        void ShowInsertMenu(Button btn, string lineId, int segIndex)
        {
            var menu = new ContextMenu();
            var newItem = new MenuItem { Header = "新建站点（中点）" };
            newItem.Click += (s, e) => InsertNewStationBetween(lineId, segIndex);
            menu.Items.Add(newItem);
            menu.Items.Add(new Separator());
            foreach (var st in _stations.Values)
            {
                var mi = new MenuItem { Header = st.NameCn };
                mi.Click += (s, e) => InsertStationBetween(lineId, segIndex, st.Id);
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = btn; menu.IsOpen = true;
        }

        double LineLength(string lineId)
        {
            var line = _lines[lineId];
            double total = OrderLength(line.StationOrder);
            foreach (var br in line.Branches)
            {
                var order = new List<string> { br.Junction };
                order.AddRange(br.StationOrder);
                total += OrderLength(order);
            }
            return total;
        }
        double OrderLength(List<string> order)
        {
            double total = 0;
            for (int i = 0; i < order.Count - 1; i++)
                if (_stations.TryGetValue(order[i], out var a) && _stations.TryGetValue(order[i + 1], out var b))
                    total += MapMath.Haversine(a.Lng, a.Lat, b.Lng, b.Lat);
            return total;
        }
        string LineBaseName(Line line) => !string.IsNullOrEmpty(line.Name) ? line.Name : (!string.IsNullOrEmpty(line.NameEn) ? line.NameEn : "Line");
        string StationNumber(Line line, int branchIdx, int stationIdx)
        {
            string baseName = LineBaseName(line);
            return branchIdx < 0 ? $"{baseName}-{stationIdx + 1:D2}" : $"{baseName}-{(char)('a' + branchIdx)}-{stationIdx + 1:D2}";
        }

        // ================= 示意图对齐/种子 =================
        void SeedSchematicFromGeo(bool overwrite)
        {
            if (_stations.Count == 0) return;
            var lngs = _stations.Values.Select(s => s.Lng).ToList();
            var lats = _stations.Values.Select(s => s.Lat).ToList();
            double midLng = (lngs.Min() + lngs.Max()) / 2, midLat = (lats.Min() + lats.Max()) / 2;
            double k = 100000, cosLat = Math.Cos(midLat * Math.PI / 180);
            foreach (var s in _stations.Values)
            {
                if (!overwrite && _schematicPos.ContainsKey(s.Id)) continue;
                _schematicPos[s.Id] = new Point((s.Lng - midLng) * cosLat * k, -(s.Lat - midLat) * k);
            }
            if (_schGrid) SnapAllSchematicToGrid();
        }

        // 将单个站点的示意坐标吸附到网格，并考虑与其他站的共线对齐
        void SnapSchematicStation(string id, ref Point w)
        {
            // w 已吸附到网格点；若某相邻站 x/y 差小于阈值，则对齐到同一网格线
            foreach (var kv in _schematicPos)
            {
                if (kv.Key == id) continue;
                var other = kv.Value;
                if (Math.Abs(other.X - w.X) < _schSnapThreshold * _schScale && Math.Abs(other.X - w.X) > 0) w = new Point(other.X, w.Y);
                if (Math.Abs(other.Y - w.Y) < _schSnapThreshold * _schScale && Math.Abs(other.Y - w.Y) > 0) w = new Point(w.X, other.Y);
            }
        }

        // 全部站点对齐网格：先将 x/y 差小于容差的站点合并到同一网格线，再吸附到最近网格
        void SnapAllSchematicToGrid()
        {
            if (_schGridSize <= 0 || _schematicPos.Count == 0) return;
            var ids = _schematicPos.Keys.ToList();
            // 按原始坐标聚类：若两站 x 差 < 容差 → 视为同一竖网格线；y 差 < 容差 → 同一横网格线
            // 对 X 做并查集式合并
            var xGroups = ClusterBy(ids, id => _schematicPos[id].X, _schSnapThreshold);
            var yGroups = ClusterBy(ids, id => _schematicPos[id].Y, _schSnapThreshold);
            // 对每个聚类计算其均值，吸附到最近网格
            var newX = new Dictionary<string, double>();
            var newY = new Dictionary<string, double>();
            foreach (var grp in xGroups)
            {
                double avg = grp.Average(id => _schematicPos[id].X);
                double snapped = Math.Round(avg / _schGridSize) * _schGridSize;
                foreach (var id in grp) newX[id] = snapped;
            }
            foreach (var grp in yGroups)
            {
                double avg = grp.Average(id => _schematicPos[id].Y);
                double snapped = Math.Round(avg / _schGridSize) * _schGridSize;
                foreach (var id in grp) newY[id] = snapped;
            }
            foreach (var id in ids)
                _schematicPos[id] = new Point(newX[id], newY[id]);
        }

        // 按值聚类：排序后相邻差 < 容差 的归为一组
        List<List<string>> ClusterBy(List<string> ids, Func<string, double> val, double tol)
        {
            var sorted = ids.OrderBy(val).ToList();
            var groups = new List<List<string>>();
            var cur = new List<string> { sorted[0] };
            double last = val(sorted[0]);
            for (int i = 1; i < sorted.Count; i++)
            {
                double v = val(sorted[i]);
                if (v - last < tol) { cur.Add(sorted[i]); }
                else { groups.Add(cur); cur = new List<string> { sorted[i] }; }
                last = v;
            }
            groups.Add(cur);
            return groups;
        }

        void FitSchematic()
        {
            if (_schematicPos.Count == 0) return;
            double w = MapCanvas.ActualWidth, h = MapCanvas.ActualHeight;
            if (w <= 0 || h <= 0) { w = 800; h = 600; }
            var xs = _schematicPos.Values.Select(p => p.X).ToList();
            var ys = _schematicPos.Values.Select(p => p.Y).ToList();
            double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
            double bw = Math.Max(maxX - minX, 1), bh = Math.Max(maxY - minY, 1);
            _schScale = Math.Max(0.05, Math.Min((w - 120) / bw, (h - 120) / bh));
            _schOffX = (w - bw * _schScale) / 2 - minX * _schScale;
            _schOffY = (h - bh * _schScale) / 2 - minY * _schScale;
        }

        void AutoArrange()
        {
            SeedSchematicFromGeo(true);
            const int grid = 40;
            foreach (var k in _schematicPos.Keys.ToList())
            {
                var p = _schematicPos[k];
                _schematicPos[k] = new Point(Math.Round(p.X / grid) * grid, Math.Round(p.Y / grid) * grid);
            }
            var ordered = _lines.Values.OrderByDescending(l => l.StationOrder.Count).ToList();
            var locked = new HashSet<string>();
            foreach (var line in ordered)
            {
                var order = line.StationOrder.Where(id => _schematicPos.ContainsKey(id)).ToList();
                if (order.Count < 2) { foreach (var id in order) locked.Add(id); continue; }
                var p0 = _schematicPos[order[0]]; var p1 = _schematicPos[order[order.Count - 1]];
                double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
                double ang = Math.Atan2(dy, dx);
                double a = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                double ux = Math.Cos(a), uy = Math.Sin(a);

                // 计算每个站沿主轴的投影，并按顺序强制单调递增（消除折返锯齿）
                var ts = new List<double>();
                var movable = new List<string>();
                foreach (var id in order)
                {
                    if (locked.Contains(id)) continue;
                    var p = _schematicPos[id];
                    double t = (p.X - p0.X) * ux + (p.Y - p0.Y) * uy;
                    ts.Add(t); movable.Add(id);
                }
                // 单调化：确保后一个 t >= 前一个 t + grid
                for (int i = 1; i < ts.Count; i++)
                    if (ts[i] < ts[i - 1] + grid) ts[i] = ts[i - 1] + grid;
                for (int i = 0; i < movable.Count; i++)
                {
                    double tsnap = Math.Round(ts[i] / grid) * grid;
                    _schematicPos[movable[i]] = new Point(p0.X + tsnap * ux, p0.Y + tsnap * uy);
                }
                foreach (var id in order) locked.Add(id);
            }
            foreach (var line in _lines.Values)
            {
                foreach (var br in line.Branches)
                {
                    if (!_schematicPos.ContainsKey(br.Junction) || br.StationOrder.Count == 0) continue;
                    var jp = _schematicPos[br.Junction];
                    var first = _schematicPos[br.StationOrder[0]];
                    double dx = first.X - jp.X, dy = first.Y - jp.Y;
                    double ang = Math.Atan2(dy, dx);
                    double a = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                    double ux = Math.Cos(a), uy = Math.Sin(a);
                    for (int j = 0; j < br.StationOrder.Count; j++)
                    {
                        var sid = br.StationOrder[j];
                        if (locked.Contains(sid)) continue;
                        double dist = grid * (j + 1);
                        _schematicPos[sid] = new Point(jp.X + dist * ux, jp.Y + dist * uy);
                    }
                }
            }
            // 额外优化：对齐到网格 + 合并近点，消除残留的微小曲折
            SnapAllSchematicToGrid();
            FitSchematic();
            Render(); SaveState();
        }

        // 一键优化：对每条线路按首尾主轴重新拉直，并保证顺序单调（消除诡异曲折）
        void OptimizeSchematicStraighten()
        {
            int grid = _schGridSize > 0 ? (int)_schGridSize : 40;
            var ordered = _lines.Values.OrderByDescending(l => l.StationOrder.Count).ToList();
            var locked = new HashSet<string>();
            foreach (var line in ordered)
            {
                var order = line.StationOrder.Where(id => _schematicPos.ContainsKey(id)).ToList();
                if (order.Count < 2) { foreach (var id in order) locked.Add(id); continue; }
                var p0 = _schematicPos[order[0]]; var p1 = _schematicPos[order[order.Count - 1]];
                double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
                double ang = Math.Atan2(dy, dx);
                double a = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                double ux = Math.Cos(a), uy = Math.Sin(a);

                // 对可移动站：按顺序等间距铺设在主轴线上（最直观的直线）
                var movable = order.Where(id => !locked.Contains(id)).ToList();
                double spacing = grid;
                // 保持首尾之间的总跨度，中间站均匀分布
                double total = Math.Sqrt(dx * dx + dy * dy);
                if (order.Count >= 2 && total > 0)
                    spacing = Math.Max(grid, Math.Round(total / (order.Count - 1) / grid) * grid);
                for (int i = 0; i < movable.Count; i++)
                {
                    // 用该站在 order 中的原始序号估算位置，保证顺序
                    int idx = order.IndexOf(movable[i]);
                    double t = idx * spacing;
                    _schematicPos[movable[i]] = new Point(p0.X + t * ux, p0.Y + t * uy);
                }
                foreach (var id in order) locked.Add(id);
            }
            SnapAllSchematicToGrid();
            FitSchematic();
            Render(); SaveState();
            SetStatus("示意图已自动拉直优化");
        }

        // ================= 持久化 =================
        string StatePath => IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroPlanner", "project.json");

        ProjectData BuildProjectData() => new ProjectData
        {
            Stations = _stations, Lines = _lines, SchematicPos = _schematicPos,
            ProjectName = _projectName, AnchorLng = _anchorLng, AnchorLat = _anchorLat,
            AnchorName = _anchorName, Constrained = _constrained, MaxDistKm = _maxDistKm, MapSideKm = _mapSideKm
        };

        ProjectData CloneProjectData()
        {
            var json = JsonSerializer.Serialize(BuildProjectData());
            return JsonSerializer.Deserialize<ProjectData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        void PushUndo()
        {
            _undoStack.Add(CloneProjectData());
            if (_undoStack.Count > 60) _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }
        void Undo()
        {
            if (_undoStack.Count == 0) { SetStatus("没有可撤销的操作"); return; }
            _redoStack.Add(CloneProjectData());
            var snap = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            ApplyProject(snap);
            SetStatus("已撤销");
        }
        void Redo()
        {
            if (_redoStack.Count == 0) { SetStatus("没有可重做的操作"); return; }
            _undoStack.Add(CloneProjectData());
            var snap = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            ApplyProject(snap);
            SetStatus("已重做");
        }
        void SaveState()
        {
            try
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(StatePath));
                File.WriteAllText(StatePath, JsonSerializer.Serialize(BuildProjectData(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
        bool LoadAutoSave()
        {
            try
            {
                if (!File.Exists(StatePath)) return false;
                var data = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(StatePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (data == null) return false;
                ApplyProject(data);
                return true;
            }
            catch { return false; }
        }

        // ================= 事件：模式/设置 =================
        void MapModeBtn_Click(object sender, RoutedEventArgs e) => SwitchMode("map");
        void SchematicModeBtn_Click(object sender, RoutedEventArgs e) => SwitchMode("schematic");
        void AutoArrangeBtn_Click(object sender, RoutedEventArgs e) => AutoArrange();
        void SchStraightenBtn_Click(object sender, RoutedEventArgs e) => OptimizeSchematicStraighten();
        void SchResetGeoBtn_Click(object sender, RoutedEventArgs e)
        {
            SeedSchematicFromGeo(true);
            if (_schGrid) SnapAllSchematicToGrid();
            FitSchematic();
            Render(); SaveState();
            SetStatus("示意图已重置为真实地理走向");
        }
        void FitBtn_Click(object sender, RoutedEventArgs e) { FitSchematic(); Render(); }
        void SwitchMode(string mode)
        {
            _mode = mode;
            MapModeBtn.IsChecked = mode == "map";
            SchematicModeBtn.IsChecked = mode == "schematic";
            if (mode == "schematic")
            {
                SeedSchematicFromGeo(false);
                if (_schGrid) SnapAllSchematicToGrid();
                FitSchematic();
            }
            Render();
        }

        void UndoDrawBtn_Click(object sender, RoutedEventArgs e) => UndoDraw();
        void RedoBtn_Click(object sender, RoutedEventArgs e) => Redo();
        void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStationId != null) DeleteStation(_selectedStationId);
            else if (_selectedLineId != null) DeleteLine(_selectedLineId);
            else SetStatus("请先选中一个站点或线路");
        }
        void BasemapCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BasemapCombo.SelectedItem is not ComboBoxItem it || it.Tag is not string key) return;
            _tiles = key == "sat"
                ? new TileService("sat", "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}")
                : new TileService("osm", "https://tile.openstreetmap.org/{z}/{x}/{y}.png");
            _tileCache.Clear();
            Render();
        }

        // ================= 事件：城市/离线/导出 =================
        async void CitySearchBtn_Click(object sender, RoutedEventArgs e) => await DoCitySearch(CitySearchBox.Text);
        async void CitySearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await DoCitySearch(CitySearchBox.Text);
        }
        (string Name, double Lng, double Lat)? FindLocalCity(string q)
        {
            string Norm(string s)
            {
                s = s.Trim();
                string[] suf = { "特别行政区", "自治区", "自治州", "自治县", "地区", "新区", "市", "县", "盟" };
                foreach (var x in suf)
                    if (s.EndsWith(x)) { s = s.Substring(0, s.Length - x.Length); break; }
                return s;
            }
            var qn = Norm(q);
            if (qn.Length == 0) return null;
            var m = _cityList.FirstOrDefault(c => Norm(c.Name) == qn);
            if (m.Name == null) m = _cityList.FirstOrDefault(c => Norm(c.Name).StartsWith(qn));
            if (m.Name == null) m = _cityList.FirstOrDefault(c => c.Name.Contains(qn) || qn.Contains(c.Name));
            return m.Name == null ? null : m;
        }
        async Task DoCitySearch(string q)
        {
            q = q.Trim(); if (q.Length == 0) return;
            var local = FindLocalCity(q);
            if (local != null)
            {
                SetAnchor(local.Value.Lng, local.Value.Lat, local.Value.Name);
                NavigateTo(local.Value.Lng, local.Value.Lat, 12);
                SetStatus($"已定位：{local.Value.Name}");
                return;
            }
            SetStatus("本地未找到，正在联网搜索…");
            var r = await Geocoder.SearchAsync(q);
            if (r != null)
            {
                SetAnchor(r.Lng, r.Lat, r.Name.Split(',')[0]);
                NavigateTo(r.Lng, r.Lat, 12);
                SetStatus($"已定位：{r.Name.Split(',')[0]}");
            }
            else SetStatus("未找到该城市，请尝试更准确的名称，或从下方常用城市列表选择");
        }
        void NavigateTo(double lng, double lat, int zoom)
        {
            _centerLng = lng; _centerLat = lat; _zoom = zoom;
            Render();
        }
        void CityPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CityPresetCombo.SelectedItem is not ComboBoxItem it || it.Tag is not string tag || tag.Length == 0) return;
            var parts = tag.Split(',');
            if (parts.Length == 3 && double.TryParse(parts[0], out var lng) && double.TryParse(parts[1], out var lat) && int.TryParse(parts[2], out var z))
            {
                SetAnchor(lng, lat, it.Content as string);
                NavigateTo(lng, lat, z);
            }
        }

        async Task DownloadAreaAsync(double west, double south, double east, double north, int minZ, int maxZ, TextBlock progress)
        {
            long total = 0;
            for (int z = minZ; z <= maxZ; z++) { var (x0, y0, x1, y1) = TileMath.BboxTiles(west, south, east, north, z); total += (long)(x1 - x0 + 1) * (y1 - y0 + 1); }
            long done = 0;
            for (int z = minZ; z <= maxZ; z++)
            {
                var (x0, y0, x1, y1) = TileMath.BboxTiles(west, south, east, north, z);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        await _tiles.DownloadAsync(z, x, y);
                        done++;
                        int pct = total > 0 ? (int)(done * 100 / total) : 100;
                        progress.Text = $"已下载 {done}/{total} 张瓦片（{pct}%）";
                        if (done % 20 == 0) await Dispatcher.InvokeAsync(() => { });
                    }
            }
        }
        void UpdateOfflineCount()
        {
            // 无固定按钮时仅更新状态
        }

        void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_stations.Count == 0) { SetStatus("暂无站点数据，请先添加站点"); return; }
            // 切换到导出工具面板
            SetTool("export");
        }
        BitmapSource RenderMapBitmap()
        {
            // 渲染全部线路全貌（非屏幕尺寸），含底图与站点标注
            return RenderFullExtentBitmap(true, 1.0, 1.0, false);
        }

        // 渲染全貌位图：按所有站点边界，带或不带底图瓦片；fontScale<0 表示自动防重叠
        BitmapSource RenderFullExtentBitmap(bool withTiles, double fontScale, double lineScale, bool embedTilesForSvg)
        {
            if (_stations.Count == 0) return null;
            double minLng = _stations.Values.Min(s => s.Lng), maxLng = _stations.Values.Max(s => s.Lng);
            double minLat = _stations.Values.Min(s => s.Lat), maxLat = _stations.Values.Max(s => s.Lat);
            double padLng = Math.Max((maxLng - minLng) * 0.15, 0.01);
            double padLat = Math.Max((maxLat - minLat) * 0.15, 0.01);
            double west = minLng - padLng, east = maxLng + padLng;
            double south = minLat - padLat, north = maxLat + padLat;

            int z = _exportMapZoom > 0 ? _exportMapZoom : _zoom;
            double x0 = MapMath.LngToWorldX(west, z), x1 = MapMath.LngToWorldX(east, z);
            double y0 = MapMath.LatToWorldY(north, z), y1 = MapMath.LatToWorldY(south, z);
            double wpx = Math.Abs(x1 - x0), hpx = Math.Abs(y1 - y0);
            const double maxDim = 4000;
            while ((wpx > maxDim || hpx > maxDim) && z > 3)
            {
                z--; x0 = MapMath.LngToWorldX(west, z); x1 = MapMath.LngToWorldX(east, z);
                y0 = MapMath.LatToWorldY(north, z); y1 = MapMath.LatToWorldY(south, z);
                wpx = Math.Abs(x1 - x0); hpx = Math.Abs(y1 - y0);
            }

            // 缩放倍率（元素像素不变 → 相对变小）与分辨率系数（元素同步放大 → 更清晰）
            double zf = Math.Max(0.25, _exportSchZoom);
            double res = Math.Max(1.0, _exportRes);

            // 输出尺寸：支持用户指定像素宽高（0=自动）
            int W, H;
            if (_exportPngW > 0 || _exportPngH > 0)
            {
                // 指定了尺寸：按指定尺寸渲染，地图内容等比缩放适配
                W = _exportPngW > 0 ? _exportPngW : Math.Max(200, (int)Math.Round(wpx * _exportPngH / hpx));
                H = _exportPngH > 0 ? _exportPngH : Math.Max(200, (int)Math.Round(hpx * _exportPngW / wpx));
                W = Math.Max(1, W); H = Math.Max(1, H);
                // 重算投影：让整个地图内容映射到指定画布（叠加缩放倍率与分辨率）
                double sx = W / wpx, sy = H / hpx;
                double sc = Math.Min(sx, sy);   // 保持等比，留边
                sc *= res * zf;
                double ox = (W - wpx * sc) / 2, oy = (H - hpx * sc) / 2;
                // 用局部变换投影函数覆盖（elem = res/zf：缩放倍率让元素相对变小）
                return RenderFullExtentBitmapEx(withTiles, fontScale, lineScale, z, west, south, east, north,
                    new Rect(0, 0, W, H), (lng, lat) => new Point((MapMath.LngToWorldX(lng, z) - x0) * sc + ox, (MapMath.LatToWorldY(lat, z) - y0) * sc + oy), sc, res / zf);
            }

            // 自动尺寸：基础像素尺寸 × 缩放倍率 × 分辨率
            W = Math.Max(200, (int)Math.Ceiling(wpx * res * zf));
            H = Math.Max(200, (int)Math.Ceiling(hpx * res * zf));
            double scale = res * zf;
            Point Proj(double lng, double lat) => new Point((MapMath.LngToWorldX(lng, z) - x0) * scale, (MapMath.LatToWorldY(lat, z) - y0) * scale);
            return RenderFullExtentBitmapEx(withTiles, fontScale, lineScale, z, west, south, east, north,
                new Rect(0, 0, W, H), Proj, scale, res / zf);
        }

        // 全貌位图渲染核心：支持自定义投影（用于指定画布尺寸）；elem = 元素缩放系数 = res/zf（分辨率更清晰，缩放倍率让元素相对变小）
        BitmapSource RenderFullExtentBitmapEx(bool withTiles, double fontScale, double lineScale, int z,
            double west, double south, double east, double north, Rect canvas, Func<double, double, Point> proj, double scaleFactor, double elem)
        {
            double x0 = MapMath.LngToWorldX(west, z), y0 = MapMath.LatToWorldY(north, z);
            int W = (int)Math.Round(canvas.Width), H = (int)Math.Round(canvas.Height);
            W = Math.Max(1, W); H = Math.Max(1, H);

            // 自动字号缩放计算：基于「未缩放」站点间距的系数 × 元素缩放系数 elem（res/zf）。
            // 不乘 scaleFactor（=res*zf），否则字体随缩放倍率一起放大，违背「缩放倍率越大字体相对越小」。
            double autoScale = 1.0;
            if (fontScale < 0)
                autoScale = ComputeAutoLabelScale(z, x0, y0);
            double effFont = (fontScale < 0 ? autoScale : fontScale) * elem;

            // 预填充站点位置 + 清空已放置标签（供方向避让与防遮挡）
            _renderStationPos.Clear();
            _placedLabels.Clear();
            foreach (var s in _stations.Values)
                _renderStationPos[s.Id] = proj(s.Lng, s.Lat);

            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (_exportShowBackground)
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
                    if (withTiles)
                    {
                        int tx0 = (int)Math.Floor(x0 / 256), tx1 = (int)Math.Floor(MapMath.LngToWorldX(east, z) / 256);
                        int ty0 = (int)Math.Floor(y0 / 256), ty1 = (int)Math.Floor(MapMath.LatToWorldY(south, z) / 256);
                        int maxTiles = (int)Math.Pow(2, z);
                        for (int ty = ty0; ty <= ty1; ty++)
                            for (int tx = tx0; tx <= tx1; tx++)
                            {
                                if (tx < 0 || ty < 0 || tx >= maxTiles || ty >= maxTiles) continue;
                                var key = $"{_tiles.Key}/{z}/{tx}/{ty}";
                                BitmapImage bmp = null;
                                if (_tileCache.TryGetValue(key, out bmp) && bmp != null)
                                    dc.DrawImage(bmp, new Rect((tx * 256 - x0) * scaleFactor, (ty * 256 - y0) * scaleFactor, 256 * scaleFactor, 256 * scaleFactor));
                                else if (_tiles.Exists(z, tx, ty))
                                {
                                    try { bmp = _tiles.Load(z, tx, ty); if (bmp != null) dc.DrawImage(bmp, new Rect((tx * 256 - x0) * scaleFactor, (ty * 256 - y0) * scaleFactor, 256 * scaleFactor, 256 * scaleFactor)); } catch { }
                                }
                            }
                    }
                }
                else
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
                }

                if (_exportShowLines)
                {
                    foreach (var line in _lines.Values)
                    {
                        if (line.StationOrder.Count < 2) continue;
                        var brush = new SolidColorBrush(ParseColor(EffectiveLineColor(line)));
                        DrawLineToDcEx(dc, line.StationOrder, line.Segments, brush, line.Id, z, proj, lineScale * elem);
                        foreach (var br in line.Branches)
                        {
                            var order = new List<string> { br.Junction };
                            order.AddRange(br.StationOrder);
                            DrawLineToDcEx(dc, order, br.Segments, brush, line.Id, z, proj, lineScale * elem);
                        }
                    }
                }
                foreach (var s in _stations.Values)
                {
                    var p = proj(s.Lng, s.Lat);
                    double r = (s.IsTransfer ? 9 : 6) * elem;
                    if (_exportShowStations)
                    {
                        var stroke = new SolidColorBrush(Color.FromRgb(0x1f, 0x27, 0x33));
                        dc.DrawEllipse(Brushes.White, new Pen(stroke, (s.IsTransfer ? 2.2 : 1.8) * elem), p, r, r);
                        if (s.IsTransfer) dc.DrawEllipse(stroke, null, p, 3.4 * elem, 3.4 * elem);
                    }
                    if (_exportShowText) DrawLabelToDc(dc, s, p, r, effFont, W);
                }
                // 比例尺
                if (_exportScaleBar)
                {
                    double pxPerMeter = 1.0 / MapMath.MetersPerPixel((south + north) / 2, z) * scaleFactor;
                    DrawScaleBarToDc(dc, pxPerMeter, W, H);
                }
            }
            rtb.Render(dv);
            return rtb;
        }

        // 计算自动字号缩放（基于最小站点间距）
        double ComputeAutoLabelScale(int z, double x0, double y0)
        {
            var pts = _stations.Values
                .Select(s => new Point(MapMath.LngToWorldX(s.Lng, z) - x0, MapMath.LatToWorldY(s.Lat, z) - y0)).ToList();
            if (pts.Count < 2) return 1.0;
            double minDist = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    double d = Math.Sqrt(Math.Pow(pts[i].X - pts[j].X, 2) + Math.Pow(pts[i].Y - pts[j].Y, 2));
                    if (d > 1) minDist = Math.Min(minDist, d);
                }
            if (minDist >= double.MaxValue) return 1.0;
            return Math.Max(0.5, Math.Min(1.5, minDist / 50.0));
        }

        void DrawScaleBarToDc(DrawingContext dc, double pxPerMeter, double W, double H)
        {
            double targetM = 200 / Math.Max(pxPerMeter, 1e-9);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(targetM)));
            double norm = targetM / mag;
            double nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
            double lenM = nice * mag;
            double lenPx = lenM * pxPerMeter;
            double x = 24, y = H - 26;
            var pen = new Pen(Brushes.Black, 1.5);
            dc.DrawLine(pen, new Point(x, y), new Point(x + lenPx, y));
            dc.DrawLine(pen, new Point(x, y - 4), new Point(x, y + 4));
            dc.DrawLine(pen, new Point(x + lenPx, y - 4), new Point(x + lenPx, y + 4));
            string label = lenM >= 1000 ? $"{lenM / 1000:0.#} km" : $"{lenM:0} m";
            var ft = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 13, Brushes.Black, 1.0);
            dc.DrawText(ft, new Point(x + lenPx / 2 - ft.Width / 2, y - 20));
        }

        // 渲染示意图位图（应用文字/线宽缩放；支持自定义输出尺寸、缩放等级与底图）
        // 缩放等级 _exportSchZoom：放大图片尺寸与站点间距（提高投影分辨率），
        // 线路/站点图标/字体保持像素大小 → 图片变大后元素相对更小。
        BitmapSource RenderSchematicBitmap(double fontScale, double lineScale)
        {
            if (_stations.Count == 0 || _schematicPos.Count == 0) return null;

            // ---- 内容边界：以四边最远到达的坐标 + 四边留白计算，保证居中、不偏向一侧 ----
            // 线路经八向折线布线后会超出站点端点，用站点端点的外接框 + 对称留白作为内容范围。
            var pts = _schematicPos.Values.ToList();
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            // 留白按内容尺寸比例放大（相对留白），避免大网或小网时留白失当
            double spanX = Math.Max(maxX - minX, 1), spanY = Math.Max(maxY - minY, 1);
            double padX = Math.Max(40, spanX * 0.12);
            double padY = Math.Max(40, spanY * 0.12);
            // 内容边界（示意坐标）
            double cL = minX - padX, cR = maxX + padX;
            double cT = minY - padY, cB = maxY + padY;
            double cw = cR - cL, ch = cB - cT;   // 含四边留白的内容宽高

            // 缩放等级：放大图片尺寸与站点间距（元素像素不变 → 相对变小）
            double zf = Math.Max(0.25, _exportSchZoom);
            // 分辨率系数：提高整体清晰度（投影与元素尺寸同步放大，视觉不变）
            double res = Math.Max(1.0, _exportRes);

            // 输出尺寸：支持用户指定像素宽高（0=自动），内容始终居中；投影同时乘以缩放等级与分辨率
            double W, H, S;
            if (_exportPngW > 0 && _exportPngH > 0)
            {
                // 指定画布：内容等比缩放放入画布并居中（不裁剪）
                W = _exportPngW; H = _exportPngH;
                S = zf * res * Math.Max(0.05, Math.Min(W / cw, H / ch));
            }
            else if (_exportPngW > 0)
            {
                W = _exportPngW; S = zf * res * (W / cw);
                H = Math.Max(1, Math.Round(ch * S));
            }
            else if (_exportPngH > 0)
            {
                H = _exportPngH; S = zf * res * (H / ch);
                W = Math.Max(1, Math.Round(cw * S));
            }
            else
            {
                // 默认：按内容自适应，缩放等级与分辨率共同放大整体
                S = zf * res * (1400.0 / cw);
                W = Math.Max(1, Math.Round(cw * S));
                H = Math.Max(1, Math.Round(ch * S));
            }
            // 居中偏移（内容框在画布内居中）
            double extraX = Math.Max(0, (W - cw * S) / 2);
            double extraY = Math.Max(0, (H - ch * S) / 2);
            // 内容框左上角映射到 (extraX, extraY)，四边对称留白 → 居中
            Point ToXY(Point p) => new Point((p.X - cL) * S + extraX, (p.Y - cT) * S + extraY);

            // 字体像素大小：基础值 × 分辨率系数 ÷ 缩放倍率。
            // zf 放大的是投影 S（站点间距/图片尺寸）；元素像素必须除以 zf，才呈现「图片越大、字体/元素相对越小」的效果。
            // 自动字号基于「未缩放」的站点间距计算（用 S/(zf*res) 还原 1× 基线）。
            double autoScale = fontScale < 0 ? ComputeSchematicAutoScale(S / (zf * res), minX, minY, padX) : 1.0;
            double effFont = (fontScale < 0 ? autoScale : fontScale) * res / zf;
            // 元素统一缩放系数：基础像素 × res / zf（随分辨率提升清晰度，随缩放倍率相对变小）
            double elem = res / zf;

            // 预填充站点位置 + 清空已放置标签
            _renderStationPos.Clear();
            _placedLabels.Clear();
            foreach (var s in _stations.Values)
                if (_schematicPos.ContainsKey(s.Id)) _renderStationPos[s.Id] = ToXY(_schematicPos[s.Id]);

            int iW = Math.Max(1, (int)Math.Round(W)), iH = Math.Max(1, (int)Math.Round(H));
            var rtb = new RenderTargetBitmap(iW, iH, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
                if (_exportShowBackground)
                {
                    // 画布可见范围对应的示意坐标（供底图瓦片范围反推）
                    double schL = cL + (0 - extraX) / S, schR = cL + (W - extraX) / S;
                    double schT = cT + (0 - extraY) / S, schB = cT + (H - extraY) / S;
                    DrawSchematicTilesToDc(dc, ToXY, S, W, H, schL, schT, schR, schB);
                }
                if (_exportShowLines)
                {
                    foreach (var line in _lines.Values)
                    {
                        if (line.StationOrder.Count < 2) continue;
                        var color = ParseColor(EffectiveLineColor(line));
                        var brush = new SolidColorBrush(color);
                        // 线宽 = 基础值 × 元素缩放系数（res/zf）：随分辨率更清晰，随缩放倍率相对变细
                        var pen = new Pen(brush, 7 * lineScale * elem) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        var list = line.StationOrder.Where(id => _schematicPos.ContainsKey(id)).Select(id => ToXY(_schematicPos[id])).ToList();
                        if (list.Count >= 2) DrawOctilinearToDc(dc, list, pen, 18 * elem);
                        foreach (var br in line.Branches)
                        {
                            var order = new List<string> { br.Junction };
                            order.AddRange(br.StationOrder);
                            var bpts = order.Where(id => _schematicPos.ContainsKey(id)).Select(id => ToXY(_schematicPos[id])).ToList();
                            if (bpts.Count >= 2) DrawOctilinearToDc(dc, bpts, pen, 18 * elem);
                        }
                    }
                }
                foreach (var s in _stations.Values)
                {
                    if (!_schematicPos.ContainsKey(s.Id)) continue;
                    var p = ToXY(_schematicPos[s.Id]);
                    // 站点圆半径 = 基础值 × 元素缩放系数（res/zf）
                    double r = (s.IsTransfer ? 12 : 8) * elem;
                    if (_exportShowStations)
                    {
                        var stroke = new SolidColorBrush(Color.FromRgb(0x1f, 0x27, 0x33));
                        dc.DrawEllipse(Brushes.White, new Pen(stroke, (s.IsTransfer ? 3 : 2.5) * elem), p, r, r);
                        if (s.IsTransfer) dc.DrawEllipse(stroke, null, p, 5 * elem, 5 * elem);
                    }
                    if (_exportShowText) DrawLabelToDc(dc, s, p, r, effFont, W);
                }
            }
            rtb.Render(dv);
            return rtb;
        }

        // 示意图导出：按「地理→示意」线性拟合变换绘制底图瓦片（与屏幕上 DrawSchematicMapBackground 视觉一致）
        void DrawSchematicTilesToDc(DrawingContext dc, Func<Point, Point> toXY, double S,
            double W, double H, double schL, double schT, double schR, double schB)
        {
            try
            {
                var geoIds = _schematicPos.Keys.Where(id => _stations.ContainsKey(id)).ToList();
                if (geoIds.Count < 2) return;
                int z0 = Math.Max(3, Math.Min(19, _zoom));
                // 最小二乘拟合：世界坐标(z0) → 示意坐标（轴对齐缩放 + 平移）
                var src = geoIds.Select(id => (MapMath.LngToWorldX(_stations[id].Lng, z0), MapMath.LatToWorldY(_stations[id].Lat, z0))).ToList();
                var dst = geoIds.Select(id => _schematicPos[id]).ToList();
                double sx = 0, sy = 0, tx = 0, ty = 0;
                for (int i = 0; i < geoIds.Count; i++) { sx += src[i].Item1; sy += src[i].Item2; tx += dst[i].X; ty += dst[i].Y; }
                sx /= geoIds.Count; sy /= geoIds.Count; tx /= geoIds.Count; ty /= geoIds.Count;
                double numA = 0, denA = 0, numB = 0, denB = 0;
                for (int i = 0; i < geoIds.Count; i++)
                {
                    double dx = src[i].Item1 - sx, dy = src[i].Item2 - sy;
                    numA += dx * (dst[i].X - tx); denA += dx * dx;
                    numB += dy * (dst[i].Y - ty); denB += dy * dy;
                }
                if (denA < 1e-9 || denB < 1e-9) return;
                double kx0 = numA / denA, ky0 = numB / denB, ox0 = tx - kx0 * sx, oy0 = ty - ky0 * sy;
                if (Math.Abs(kx0) < 1e-12 || Math.Abs(ky0) < 1e-12) return;

                // 选择使瓦片在输出画布上接近原始分辨率的 zoom（世界坐标 ∝ 2^z，k ∝ 2^-z）
                double want = z0 + Math.Log(Math.Max(1e-9, kx0 * S), 2);
                int refZ = Math.Max(3, Math.Min(19, (int)Math.Round(want)));
                double f = Math.Pow(2, refZ - z0);
                double kx = kx0 / f, ky = ky0 / f;   // 世界坐标(refZ) → 示意坐标（平移量不变）

                // 画布可见范围 → 世界坐标(refZ) → 瓦片索引范围
                double wxL = (schL - ox0) / kx, wxR = (schR - ox0) / kx;
                double wyT = (schT - oy0) / ky, wyB = (schB - oy0) / ky;
                if (wxL > wxR) (wxL, wxR) = (wxR, wxL);
                if (wyT > wyB) (wyT, wyB) = (wyB, wyT);
                int maxTiles = (int)Math.Pow(2, refZ);
                int tx0 = Math.Max(0, (int)Math.Floor(wxL / 256)), tx1 = Math.Min(maxTiles - 1, (int)Math.Floor(wxR / 256));
                int ty0 = Math.Max(0, (int)Math.Floor(wyT / 256)), ty1 = Math.Min(maxTiles - 1, (int)Math.Floor(wyB / 256));
                if ((long)(tx1 - tx0 + 1) * (ty1 - ty0 + 1) > 900) return; // 防退化：范围异常时不绘制底图

                dc.PushOpacity(Math.Max(0, Math.Min(1, _schMapBgOpacity)));
                for (int tyy = ty0; tyy <= ty1; tyy++)
                    for (int txx = tx0; txx <= tx1; txx++)
                    {
                        var key = $"{_tiles.Key}/{refZ}/{txx}/{tyy}";
                        BitmapImage bmp = null;
                        if (_tileCache.TryGetValue(key, out var cached) && cached != null) bmp = cached;
                        else if (_tiles.Exists(refZ, txx, tyy))
                        {
                            try { bmp = _tiles.Load(refZ, txx, tyy); } catch { }
                        }
                        if (bmp == null) continue;
                        var sp = toXY(new Point(txx * 256 * kx + ox0, tyy * 256 * ky + oy0));
                        double tw = 256 * kx * S, th = 256 * ky * S;
                        if (tw <= 0 || th <= 0) continue;
                        if (sp.X + tw < 0 || sp.Y + th < 0 || sp.X > W || sp.Y > H) continue;
                        dc.DrawImage(bmp, new Rect(sp.X, sp.Y, tw, th));
                    }
                dc.Pop();
            }
            catch { }
        }

        double ComputeSchematicAutoScale(double S, double minX, double minY, double pad)
        {
            Point ToXY(Point p) => new Point((p.X - minX + pad) * S, (p.Y - minY + pad) * S);
            var pts = _schematicPos.Values.Select(ToXY).ToList();
            if (pts.Count < 2) return 1.0;
            double minDist = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    double d = Math.Sqrt(Math.Pow(pts[i].X - pts[j].X, 2) + Math.Pow(pts[i].Y - pts[j].Y, 2));
                    if (d > 1) minDist = Math.Min(minDist, d);
                }
            if (minDist >= double.MaxValue) return 1.0;
            return Math.Max(0.5, Math.Min(1.5, minDist / 60.0));
        }

        void DrawOctilinearToDc(DrawingContext dc, List<Point> pts, Pen pen, double chamfer)
        {
            var cmds = PathBuilder.Octilinear(pts, chamfer);
            var fig = new PathFigure { StartPoint = cmds[0] };
            foreach (var c in cmds.Skip(1)) fig.Segments.Add(new LineSegment(c, true));
            var geo = new PathGeometry(); geo.Figures.Add(fig);
            dc.DrawGeometry(null, pen, geo);
        }

        void DrawLineToDc(DrawingContext dc, List<string> order, List<Segment> segs, SolidColorBrush brush, string lineId, double x0, double y0, int z, double lineScale = 1.0)
        {
            double radiusPx = _curveRadiusM / MapMath.MetersPerPixel(_centerLat, z);
            double lw = EffectiveLineWidth(lineId) * lineScale;
            Point Proj(string id) { var st = _stations[id]; return new Point(MapMath.LngToWorldX(st.Lng, z) - x0, MapMath.LatToWorldY(st.Lat, z) - y0); }
            Point Gp(double lng, double lat) => new Point(MapMath.LngToWorldX(lng, z) - x0, MapMath.LatToWorldY(lat, z) - y0);
            if (segs == null || segs.Count == 0)
            {
                var pts = order.Where(id => _stations.ContainsKey(id)).Select(Proj).ToList();
                if (pts.Count >= 2) DrawPathToDc(dc, pts, brush, lw, radiusPx);
                return;
            }
            for (int i = 0; i < segs.Count; i++)
            {
                var seg = segs[i];
                if (!_stations.ContainsKey(seg.A) || !_stations.ContainsKey(seg.B)) continue;
                var pen = new Pen(brush, lw) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (seg.Type == "transition") pen.DashStyle = new DashStyle(new double[] { 1, 6 }, 0);
                else if (seg.Type == "ground") pen.DashStyle = new DashStyle(new double[] { 12, 8 }, 0);

                if (seg.CurveAnchors != null && seg.CurveAnchors.Count > 0)
                {
                    var geo = BuildBezierGeometry(Proj(seg.A), Proj(seg.B), seg.CurveAnchors, Gp);
                    if (geo != null) dc.DrawGeometry(null, pen, geo);
                    continue;
                }

                var way = new List<Point> { Proj(seg.A) };
                if (seg.Anchors != null)
                    foreach (var an in seg.Anchors) way.Add(new Point(MapMath.LngToWorldX(an.Lng, z) - x0, MapMath.LatToWorldY(an.Lat, z) - y0));
                way.Add(Proj(seg.B));
                DrawPathToDc(dc, way, brush, lw, radiusPx, pen);
            }
        }
        void DrawPathToDc(DrawingContext dc, List<Point> pts, SolidColorBrush brush, double width, double radius, Pen overridePen = null)
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

        // 用自定义投影函数绘制线路（用于指定画布尺寸的 PNG 导出）
        void DrawLineToDcEx(DrawingContext dc, List<string> order, List<Segment> segs, SolidColorBrush brush, string lineId, int z, Func<double, double, Point> proj, double lineScale = 1.0)
        {
            // 半径需按投影缩放比例调整：用 1° 经度差估算像素/度
            double pxPerDeg = Math.Abs(proj(0, 0).X - proj(1, 0).X);
            double radiusPx = _curveRadiusM / 111320.0 * pxPerDeg;
            double lw = EffectiveLineWidth(lineId) * lineScale;
            Point P(string id) { var st = _stations[id]; return proj(st.Lng, st.Lat); }
            if (segs == null || segs.Count == 0)
            {
                var pts = order.Where(id => _stations.ContainsKey(id)).Select(P).ToList();
                if (pts.Count >= 2) DrawPathToDc(dc, pts, brush, lw, radiusPx);
                return;
            }
            for (int i = 0; i < segs.Count; i++)
            {
                var seg = segs[i];
                if (!_stations.ContainsKey(seg.A) || !_stations.ContainsKey(seg.B)) continue;
                var pen = new Pen(brush, lw) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (seg.Type == "transition") pen.DashStyle = new DashStyle(new double[] { 1, 6 }, 0);
                else if (seg.Type == "ground") pen.DashStyle = new DashStyle(new double[] { 12, 8 }, 0);

                // 钢笔式曲线锚点：绘制贝塞尔曲线（与屏幕渲染一致）
                if (seg.CurveAnchors != null && seg.CurveAnchors.Count > 0)
                {
                    var geo = BuildBezierGeometry(P(seg.A), P(seg.B), seg.CurveAnchors, proj);
                    if (geo != null) dc.DrawGeometry(null, pen, geo);
                    continue;
                }

                var way = new List<Point> { P(seg.A) };
                if (seg.Anchors != null)
                    foreach (var an in seg.Anchors) way.Add(proj(an.Lng, an.Lat));
                way.Add(P(seg.B));
                DrawPathToDc(dc, way, brush, lw, radiusPx, pen);
            }
        }

        // 用投影函数构建贝塞尔曲线几何（A→锚点1→…→锚点n→B），与屏幕 DrawBezierSegmentGeometry 逻辑一致
        Geometry BuildBezierGeometry(Point a, Point b, List<CurveAnchor> anchors, Func<double, double, Point> proj)
        {
            if (anchors == null || anchors.Count == 0) return null;
            var fig = new PathFigure { StartPoint = a, IsClosed = false };
            // 第一段：A → 第一个锚点（用其入向手柄作控制点）
            var first = anchors[0];
            var firstCtrl = proj(first.InCtrl.Lng, first.InCtrl.Lat);
            fig.Segments.Add(new BezierSegment(firstCtrl, firstCtrl, proj(first.Lng, first.Lat), true));
            // 中间段：锚点 i-1 → 锚点 i
            for (int i = 1; i < anchors.Count; i++)
            {
                var prev = anchors[i - 1];
                var cur = anchors[i];
                var c1 = proj(prev.OutCtrl.Lng, prev.OutCtrl.Lat);
                var c2 = proj(cur.InCtrl.Lng, cur.InCtrl.Lat);
                fig.Segments.Add(new BezierSegment(c1, c2, proj(cur.Lng, cur.Lat), true));
            }
            // 最后一段：最后一个锚点 → B（用其出向手柄）
            var last = anchors[anchors.Count - 1];
            var lastCtrl = proj(last.OutCtrl.Lng, last.OutCtrl.Lat);
            fig.Segments.Add(new BezierSegment(lastCtrl, lastCtrl, b, true));

            var geo = new PathGeometry(); geo.Figures.Add(fig); geo.Freeze();
            return geo;
        }
        void DrawLabelToDc(DrawingContext dc, Station s, Point p, double circleR, double fontScale = 1.0, double canvasW = 0)
        {
            var lines = new List<string>();
            if (_showCn && !string.IsNullOrEmpty(s.NameCn)) lines.Add(s.NameCn);
            if (_showEn && !string.IsNullOrEmpty(s.NameEn)) lines.Add(s.NameEn);
            if (_showThird && !string.IsNullOrEmpty(s.NameThird)) lines.Add(s.NameThird);
            if (lines.Count == 0) return;
            Line styleLine = null;
            foreach (var lid in s.Lines) if (_lines.TryGetValue(lid, out var l)) { styleLine = l; break; }
            double fontSize = ResolveStationFontSize(s, styleLine) * fontScale;
            bool bold = ResolveStationBold(s, styleLine);
            bool outline = ResolveStationOutline(s, styleLine);
            Color textColor = ResolveStationColor(s, styleLine);
            var textBrush = new SolidColorBrush(textColor);
            double lineH = fontSize + 3;
            double totalH = lines.Count * lineH;

            // 精确测量最大宽度
            var fontList = new List<(string text, string fam)>();
            for (int i = 0; i < lines.Count; i++) fontList.Add((lines[i], ResolveStationFont(s, styleLine, i)));
            double estW = MeasureLabelWidth(fontList, fontSize, bold) + 6;
            double cw = canvasW > 0 ? canvasW : (MapCanvas.ActualWidth > 0 ? MapCanvas.ActualWidth : 2000);

            // 方向避让：东西走向线路文字放上方（居中），其余放左右侧
            var tan = ComputeLineTangent(s);
            bool horizontal = tan.HasValue && Math.Abs(tan.Value.X) > Math.Abs(tan.Value.Y) * 1.4;
            double gap = circleR + 6;

            // 计算「默认侧」与「另一侧」两种候选位置（固定，不再移动）
            Rect primary, alt;
            bool primaryFlip, altFlip;
            if (horizontal)
            {
                primaryFlip = false;
                primary = new Rect(p.X - estW / 2, p.Y - totalH - circleR - gap, estW, totalH);
                alt = new Rect(p.X - estW / 2, p.Y + circleR + gap, estW, totalH);
            }
            else
            {
                primaryFlip = p.X + gap + estW > cw - 6;
                if (primaryFlip)
                {
                    primary = new Rect(p.X - gap - estW, p.Y - totalH / 2, estW, totalH);
                    alt = new Rect(p.X + gap, p.Y - totalH / 2, estW, totalH);
                }
                else
                {
                    primary = new Rect(p.X + gap, p.Y - totalH / 2, estW, totalH);
                    alt = new Rect(p.X - gap - estW, p.Y - totalH / 2, estW, totalH);
                }
            }
            altFlip = !primaryFlip;

            // 默认侧被遮挡且另一侧空闲 → 翻转到另一侧；否则保持默认侧（固定，不移动）
            Rect rect = primary; bool flip = primaryFlip;
            if (OverlapsAnyLabel(s.Id, primary, lines) && !OverlapsAnyLabel(s.Id, alt, lines))
            { rect = alt; flip = altFlip; }

            double left = Math.Max(4, Math.Min(rect.X, cw - 4 - estW));
            double top = Math.Max(2, rect.Y);

            for (int i = 0; i < lines.Count; i++)
            {
                string fam = fontList[i].fam;
                if (string.IsNullOrEmpty(fam)) fam = "Segoe UI";
                var typeface = new Typeface(new FontFamily(fam), FontStyles.Normal,
                    bold ? FontWeights.Bold : FontWeights.SemiBold, FontStretches.Normal);
                var ft = new FormattedText(lines[i], System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, fontSize, textBrush, 1.0);
                // 逐行水平对齐：水平放置居中，翻转右对齐，否则左对齐
                double lx;
                if (horizontal) lx = left + (estW - ft.Width) / 2;
                else if (flip) lx = left + estW - ft.Width;
                else lx = left;
                var pos = new Point(lx, top + i * lineH);
                var geo = ft.BuildGeometry(pos);
                if (outline)
                {
                    var outlinePen = new Pen(Brushes.White, 3) { LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(Brushes.White, outlinePen, geo);
                }
                dc.DrawText(ft, pos);
            }
            _placedLabels[s.Id] = new Rect(left, top, estW, totalH);
        }

        void SaveBtn_Click(object sender, RoutedEventArgs e) => SaveProject();
        void SaveAsBtn_Click(object sender, RoutedEventArgs e) => SaveAs();
        void OpenBtn_Click(object sender, RoutedEventArgs e) => OpenProject();

        string SerializeProject() => JsonSerializer.Serialize(BuildProjectData(), new JsonSerializerOptions { WriteIndented = true });

        void SaveProject()
        {
            if (_currentFile == null) { SaveAs(); return; }
            SaveProjectTo(_currentFile);
        }
        void SaveAs()
        {
            var dlg = new SaveFileDialog
            {
                FileName = _currentFile == null ? "地铁规划项目.json" : IOPath.GetFileName(_currentFile),
                Filter = "地铁规划项目 (*.json)|*.json|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true) SaveProjectTo(dlg.FileName);
        }
        void SaveProjectTo(string path)
        {
            try
            {
                File.WriteAllText(path, SerializeProject(), Encoding.UTF8);
                _currentFile = path;
                _projectLoaded = true;
                UpdateWelcomeVisibility();
                AddRecent(path);
                UpdateTitle();
                SetStatus("项目已保存：" + IOPath.GetFileName(path));
            }
            catch (Exception ex) { MessageBox.Show("保存失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        void OpenProject()
        {
            var dlg = new OpenFileDialog { Filter = "地铁规划项目 (*.json)|*.json|所有文件 (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            OpenProjectFile(dlg.FileName);
        }
        void OpenProjectFile(string path)
        {
            try
            {
                var data = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (data == null) return;
                ApplyProject(data);
                _currentFile = path;
                _projectLoaded = true;
                UpdateProjectUI();
                UpdateWelcomeVisibility();
                AddRecent(path);
                UpdateTitle();
                SetStatus("项目已打开：" + IOPath.GetFileName(path));
            }
            catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        void ApplyProject(ProjectData data)
        {
            _stations = data.Stations ?? new();
            _lines = data.Lines ?? new();
            _schematicPos = data.SchematicPos ?? new();
            foreach (var s in _stations.Values) if (s.Lines == null) s.Lines = new HashSet<string>();
            foreach (var l in _lines.Values)
            {
                if (l.StationOrder == null) l.StationOrder = new();
                if (l.Segments == null) l.Segments = new();
                if (l.Branches == null) l.Branches = new();
                foreach (var sg in l.Segments) if (sg.Anchors == null) sg.Anchors = new List<GeoPoint>();
                foreach (var br in l.Branches) foreach (var sg in br.Segments) if (sg.Anchors == null) sg.Anchors = new List<GeoPoint>();
                if (l.LineWidth <= 0) l.LineWidth = 5;
                if (l.FontSize <= 0) l.FontSize = 12;
            }
            _projectName = string.IsNullOrEmpty(data.ProjectName) ? "未命名项目" : data.ProjectName;
            _anchorLng = data.AnchorLng; _anchorLat = data.AnchorLat;
            _anchorName = data.AnchorName ?? "";
            _constrained = data.Constrained;
            _maxDistKm = data.MaxDistKm <= 0 ? 60 : data.MaxDistKm;
            _mapSideKm = data.MapSideKm <= 0 ? 40 : data.MapSideKm;
            _selectedLineId = null; _selectedStationId = null; _activeLineId = null;
            UpdateProjectUI();
            ShowToolPanel(_tool); Render(); SaveState();
        }
        void UpdateTitle()
        {
            Title = _currentFile == null ? "地铁线路规划器 Metro Planner" : "地铁线路规划器 Metro Planner — " + IOPath.GetFileName(_currentFile);
        }

        // ================= 项目 / 欢迎 / 最近 =================
        void NewProject_Click(object sender, RoutedEventArgs e) => NewProject();
        void NewProject()
        {
            var dlg = new ProjectDialog(_cityList) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            _projectName = dlg.ProjectName;
            _anchorName = dlg.CityName;
            _anchorLng = dlg.Lng; _anchorLat = dlg.Lat;
            _stations.Clear(); _lines.Clear(); _schematicPos.Clear(); _measure.Clear();
            _activeLineId = null; _selectedLineId = null; _selectedStationId = null;
            _currentFile = null;
            if (dlg.Lng != 0 || dlg.Lat != 0) NavigateTo(dlg.Lng, dlg.Lat, 12);
            _projectLoaded = true;
            UpdateProjectUI();
            UpdateWelcomeVisibility();
            ShowToolPanel(_tool); Render(); SaveState(); UpdateTitle();
            SetStatus($"已新建项目：{_projectName}（{_anchorName}）");
        }
        void WelcomeContinueBtn_Click(object sender, RoutedEventArgs e)
        {
            _projectLoaded = true;
            UpdateWelcomeVisibility();
            Render();
        }
        void UpdateProjectUI()
        {
            // 项目名等已移入项目面板，此处仅保留内部状态同步
        }
        void UpdateWelcomeVisibility()
        {
            WelcomePanel.Visibility = _projectLoaded ? Visibility.Collapsed : Visibility.Visible;
        }
        void SetAnchor(double lng, double lat, string name)
        {
            _anchorLng = lng; _anchorLat = lat; _anchorName = name ?? "";
        }
        bool CheckWithinProject(double lng, double lat)
        {
            if (!_constrained) return true;
            if (_anchorLng == 0 && _anchorLat == 0) return true;
            double m = MapMath.Haversine(_anchorLng, _anchorLat, lng, lat);
            if (m > _maxDistKm * 1000)
            {
                MessageBox.Show($"该位置距离项目中心（{_anchorName}）约 {FormatDist(m)}，超出限制范围（{_maxDistKm} km）。\n如需跨城（如广佛线），请在「项目设置」中取消勾选「限制在当前城市范围」或调大范围。",
                    "超出项目范围", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        // ================= 最近项目 =================
        string RecentPath => IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroPlanner", "recent.json");
        void LoadRecent()
        {
            try
            {
                if (File.Exists(RecentPath))
                    _recent = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentPath)) ?? new List<string>();
            }
            catch { _recent = new List<string>(); }
            RefreshRecent();
        }
        void SaveRecent()
        {
            try
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(RecentPath));
                File.WriteAllText(RecentPath, JsonSerializer.Serialize(_recent));
            }
            catch { }
        }
        void AddRecent(string path)
        {
            _recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _recent.Insert(0, path);
            if (_recent.Count > 10) _recent = _recent.Take(10).ToList();
            SaveRecent();
            RefreshRecent();
        }
        void RefreshRecent()
        {
            RecentList.Items.Clear();
            var existing = _recent.Where(File.Exists).ToList();
            foreach (var p in existing)
                RecentList.Items.Add(new ListBoxItem { Content = IOPath.GetFileName(p), ToolTip = p, Tag = p });
            RecentEmpty.Visibility = existing.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        void RecentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RecentList.SelectedItem is ListBoxItem it && it.Tag is string path && File.Exists(path))
                OpenProjectFile(path);
        }

        // ================= 菜单 =================
        void Exit_Click(object sender, RoutedEventArgs e) => Close();
        void ClearMeasure_Click(object sender, RoutedEventArgs e) { _measure.Clear(); Render(); }
        void Help_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "地铁线路规划器 使用说明\n\n" +
                "1. 「新建项目」设置项目名称与所在城市；\n" +
                "2. 左侧图标工具条选择工具，右侧面板会切换为当前工具专属的功能；\n" +
                "3. 普通站/换乘站工具：点击地图放置站点并填写名称（可自动生成拼音/注音）；\n" +
                "4. 绘制线路工具：依次点击站点连线，可设路段类型、延长线路、添加锚点绕行；\n" +
                "5. 锚点工具：在线段上点击/右键添加锚点实现绕行；\n" +
                "6. 支线工具：先点分叉站再点支线站点，绘制 Y 形线路；\n" +
                "7. 线路管理：命名、换色、样式（分语言字体）、分段、断开/合并、插入站点；\n" +
                "8. 项目设置：名称、城市范围、地图加载区域、示例数据；\n" +
                "9. 离线地图：下载指定区域瓦片供断网使用；\n" +
                "10. 切换「示意图」自动对齐网格，共线线路上下/左右并置显示；\n" +
                "11. 「导出」可输出 SVG/PNG/GeoJSON，按全貌与比例尺导出。",
                "使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void LoadDemoBtn_Click(object sender, RoutedEventArgs e) => LoadDemo();

        void LoadDemo()
        {
            Station S(string cn, string en, string third, string label, double lng, double lat, bool tr)
            {
                var s = new Station { NameCn = cn, NameEn = en, NameThird = third, ThirdLabel = label, Transfer = tr, Lng = lng, Lat = lat };
                _stations[s.Id] = s; return s;
            }
            var A = S("复兴门", "Fuxingmen", "Fùxīngmén", "拼音", 116.356, 39.908, false);
            var B = S("西单", "Xidan", "Xīdān", "拼音", 116.374, 39.907, false);
            var C = S("天安门西", "Tiananmen West", "Tiānānménxī", "拼音", 116.391, 39.908, false);
            var D = S("王府井", "Wangfujing", "Wángfǔjǐng", "拼音", 116.411, 39.909, false);
            var E = S("东单", "Dongdan", "Dōngdān", "拼音", 116.418, 39.909, true);
            var F = S("积水潭", "Jishuitan", "Jīshuǐtán", "拼音", 116.373, 39.948, false);
            var G = S("鼓楼大街", "Guloudajie", "Gǔlóudàjiē", "拼音", 116.400, 39.943, false);
            var H = S("南锣鼓巷", "Nanluoguxiang", "Nánluógǔxiàng", "拼音", 116.403, 39.934, true);
            var I = S("崇文门", "Chongwenmen", "Chóngwénmén", "拼音", 116.417, 39.899, false);
            var J = S("天坛东门", "Tiantandongmen", "Tiāntándōngmén", "拼音", 116.421, 39.887, false);
            var K = S("西直门", "Xizhimen", "Xīzhímén", "拼音", 116.355, 39.940, false);
            var L = S("平安里", "Pinganli", "Píngānlǐ", "拼音", 116.370, 39.934, false);
            var M = S("东四", "Dongsi", "Dōngsì", "拼音", 116.417, 39.924, false);
            var N = S("朝阳门", "Chaoyangmen", "Cháoyángmén", "拼音", 116.433, 39.924, false);

            string l1 = CreateLine(); _lines[l1].Name = "1号线"; _lines[l1].Color = "#e4002b";
            _lines[l1].StationOrder = new List<string> { A.Id, B.Id, C.Id, D.Id, E.Id };
            foreach (var id in _lines[l1].StationOrder) _stations[id].Lines.Add(l1);
            RebuildSegments(l1);

            string l2 = CreateLine(); _lines[l2].Name = "2号线"; _lines[l2].Color = "#0f7dc2";
            _lines[l2].StationOrder = new List<string> { F.Id, G.Id, H.Id, E.Id, I.Id, J.Id };
            foreach (var id in _lines[l2].StationOrder) _stations[id].Lines.Add(l2);
            RebuildSegments(l2);
            _lines[l2].Segments[0].Type = "ground";
            _lines[l2].Segments[3].Type = "transition";

            string l3 = CreateLine(); _lines[l3].Name = "13号线"; _lines[l3].Color = "#00a651";
            _lines[l3].StationOrder = new List<string> { K.Id, L.Id, H.Id, M.Id, N.Id };
            foreach (var id in _lines[l3].StationOrder) _stations[id].Lines.Add(l3);
            RebuildSegments(l3);

            SeedSchematicFromGeo(true);
            AutoArrange();
            _projectName = "示例 · 北京地铁";
            _anchorName = "北京"; _anchorLng = 116.40; _anchorLat = 39.93;
            _constrained = true; _maxDistKm = 60;
            _projectLoaded = true;
            UpdateProjectUI();
            UpdateWelcomeVisibility();
            NavigateTo(116.40, 39.93, 12);
            ShowToolPanel(_tool); SaveState();
            SetStatus("示例数据已载入（3 条线路，2 个换乘站）");
        }
    }
}
