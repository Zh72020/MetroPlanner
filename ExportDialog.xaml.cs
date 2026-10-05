using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MetroPlanner.Models;
using System.Collections.Generic;
using System.Linq;

namespace MetroPlanner
{
    public partial class ExportDialog : Window
    {
        Dictionary<string, Station> _stations;
        Dictionary<string, Line> _lines;
        Dictionary<string, Point> _schematicPos;
        TileService _tiles;
        int _mapZoom;
        double _west, _south, _east, _north;
        bool _showCn, _showEn, _showThird;
        double _curveRadiusM;
        Func<BitmapSource> _renderPng;
        BitmapSource _previewBmp;

        public ExportDialog(Dictionary<string, Station> stations, Dictionary<string, Line> lines,
            Dictionary<string, Point> schematicPos, TileService tiles, int mapZoom,
            double west, double south, double east, double north,
            bool showCn, bool showEn, bool showThird, double curveRadiusM,
            Func<BitmapSource> renderPng)
        {
            InitializeComponent();
            _stations = stations; _lines = lines; _schematicPos = schematicPos;
            _tiles = tiles; _mapZoom = mapZoom;
            _west = west; _south = south; _east = east; _north = north;
            _showCn = showCn; _showEn = showEn; _showThird = showThird;
            _curveRadiusM = curveRadiusM;
            _renderPng = renderPng;

            TypeCombo.Items.Add(new ComboBoxItem { Content = "线路图 SVG（矢量，不含底图）", Tag = "map" });
            TypeCombo.Items.Add(new ComboBoxItem { Content = "线路图 SVG（嵌入底图瓦片）", Tag = "maptiles" });
            TypeCombo.Items.Add(new ComboBoxItem { Content = "示意图 SVG（矢量）", Tag = "schematic" });
            TypeCombo.Items.Add(new ComboBoxItem { Content = "PNG 图片（含底图，全貌）", Tag = "png" });
            TypeCombo.Items.Add(new ComboBoxItem { Content = "GeoJSON", Tag = "geojson" });
            TypeCombo.SelectedIndex = 0;
            ZoomSlider.Value = _mapZoom;
            RefreshPreview();
        }

        string CurrentTag => TypeCombo.SelectedItem is ComboBoxItem it && it.Tag is string t ? t : "map";
        int ZoomVal => (int)Math.Round(ZoomSlider.Value);
        double FontScale => AutoTextChk.IsChecked == true ? 1.0 : Math.Round(FontScaleSlider.Value, 2);
        double LineScale => Math.Round(LineScaleSlider.Value, 2);
        bool UseScaleBar => ScaleBarChk.IsChecked == true;

        void ZoomSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ZoomValText != null) ZoomValText.Text = ZoomVal.ToString();
            if (IsLoaded) RefreshPreview();
        }
        void FontScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontValText != null) FontValText.Text = FontScaleSlider.Value.ToString("F1") + "×";
            if (IsLoaded && AutoTextChk.IsChecked != true) RefreshPreview();
        }
        void LineScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LineValText != null) LineValText.Text = LineScaleSlider.Value.ToString("F1") + "×";
            if (IsLoaded) RefreshPreview();
        }
        void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TypeCombo != null) RefreshPreview();
        }
        void AutoText_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoTextChk == null || FontScaleSlider == null) return;
            FontScaleSlider.IsEnabled = AutoTextChk.IsChecked != true;
            if (IsLoaded) RefreshPreview();
        }
        void FullExtent_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshPreview(); }
        void ScaleBar_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshPreview(); }
        void PreviewZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PreviewImage != null && _previewBmp != null)
            {
                double z = PreviewZoomSlider.Value;
                var scaled = new TransformedBitmap(_previewBmp, new System.Windows.Media.ScaleTransform(z, z));
                PreviewImage.Source = scaled;
            }
        }
        void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshPreview();

        void RefreshPreview()
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewEmpty.Visibility = Visibility.Collapsed;
            _previewBmp = null;

            var tag = CurrentTag;
            if (tag == "geojson")
            {
                PreviewEmpty.Text = "GeoJSON 为文本格式，无图形预览（导出后可用 QGIS 等打开）";
                PreviewEmpty.Visibility = Visibility.Visible;
                return;
            }
            if (tag == "png")
            {
                try
                {
                    var bmp = _renderPng?.Invoke();
                    if (bmp != null) { _previewBmp = bmp; ShowPreviewBitmap(); }
                    else PreviewEmpty.Visibility = Visibility.Visible;
                }
                catch { PreviewEmpty.Visibility = Visibility.Visible; }
                return;
            }
            // SVG：直接渲染一个缩略图（渲染 SVG 内容到临时位图不可行，用画布预览代替）
            try
            {
                var bmp = _renderPng?.Invoke();
                if (bmp != null) { _previewBmp = bmp; ShowPreviewBitmap(); }
                else PreviewEmpty.Visibility = Visibility.Visible;
            }
            catch { PreviewEmpty.Visibility = Visibility.Visible; }
        }

        void ShowPreviewBitmap()
        {
            double z = PreviewZoomSlider.Value;
            var scaled = new TransformedBitmap(_previewBmp, new System.Windows.Media.ScaleTransform(z, z));
            PreviewImage.Source = scaled;
            PreviewImage.Visibility = Visibility.Visible;
        }

        string BuildSvgForTag(string tag)
        {
            switch (tag)
            {
                case "map":
                    return SvgExporter.BuildMapSvg(_stations, _lines, _showCn, _showEn, _showThird, _curveRadiusM, FontScale, LineScale);
                case "maptiles":
                    return SvgExporter.BuildMapSvgWithTilesEx(_stations, _lines, _tiles, _west, _south, _east, _north, ZoomVal,
                        _showCn, _showEn, _showThird, _curveRadiusM, FontScale, LineScale);
                case "schematic":
                    return SvgExporter.BuildSchematicSvg(_stations, _lines, _schematicPos, _showCn, _showEn, _showThird, FontScale);
                case "geojson":
                    return SvgExporter.BuildGeoJson(_stations, _lines);
                default: return null;
            }
        }

        void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            var tag = CurrentTag;
            if (tag == "png")
            {
                BitmapSource bmp = null;
                try { bmp = _renderPng?.Invoke(); } catch { }
                if (bmp == null) { MessageBox.Show("无法渲染当前视图", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                var dlg = new SaveFileDialog { FileName = "地铁线路图.png", Filter = "PNG 图片 (*.png)|*.png" };
                if (dlg.ShowDialog() != true) return;
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(dlg.FileName);
                enc.Save(fs);
            }
            else
            {
                string content = BuildSvgForTag(tag);
                if (string.IsNullOrEmpty(content)) { MessageBox.Show("该格式当前无内容", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                var (fn, filter) = tag switch
                {
                    "geojson" => ("地铁线路.geojson", "GeoJSON (*.geojson)|*.geojson|所有文件 (*.*)|*.*"),
                    _ => (tag == "schematic" ? "地铁示意图.svg" : "地铁线路图.svg", "SVG (*.svg)|*.svg")
                };
                var dlg = new SaveFileDialog { FileName = fn, Filter = filter };
                if (dlg.ShowDialog() != true) return;
                File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
            }
            MessageBox.Show("导出完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
