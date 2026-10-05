using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace MetroPlanner
{
    /// <summary>枚举系统已安装字体，供站名/线路样式选择。</summary>
    public static class FontHelper
    {
        static List<string> _cached;

        /// <summary>返回系统所有已安装字体的 FamilyName（去重、按名称排序）。</summary>
        public static List<string> InstalledFonts()
        {
            if (_cached != null) return _cached;
            try
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var ff in Fonts.SystemFontFamilies)
                {
                    var name = ff.Source ?? ff.FamilyNames.Values.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(name)) set.Add(name);
                }
                _cached = set.OrderBy(s => s, StringComparer.CurrentCulture).ToList();
            }
            catch
            {
                _cached = new List<string>();
            }
            return _cached;
        }
    }
}
