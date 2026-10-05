using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MetroPlanner
{
    /// <summary>基于 OpenStreetMap Nominatim 的地理编码（城市 / 区域边界）。</summary>
    public static class Geocoder
    {
        private static readonly HttpClient Http = new HttpClient();
        static Geocoder()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("MetroPlanner/1.0 (personal use)");
        }

        public class GeoResult
        {
            public double Lng, Lat;
            public double South, North, West, East;
            public string Name;
        }

        public static async Task<GeoResult> SearchAsync(string q)
        {
            try
            {
                var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(q)}&format=json&limit=1&countrycodes=cn";
                var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var e = doc.RootElement[0];
                    var r = new GeoResult
                    {
                        Lng = e.GetProperty("lon").GetDouble(),
                        Lat = e.GetProperty("lat").GetDouble(),
                        Name = e.TryGetProperty("display_name", out var dn) ? dn.GetString() : q
                    };
                    if (e.TryGetProperty("boundingbox", out var bb) && bb.GetArrayLength() >= 4)
                    {
                        r.South = bb[0].GetDouble();
                        r.North = bb[1].GetDouble();
                        r.West = bb[2].GetDouble();
                        r.East = bb[3].GetDouble();
                    }
                    return r;
                }
            }
            catch { }
            return null;
        }
    }
}
