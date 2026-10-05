using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MetroPlanner
{
    /// <summary>地图瓦片下载与本地磁盘缓存。</summary>
    public class TileService
    {
        public static string CacheRoot { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroPlanner", "tiles");

        private static readonly HttpClient Http = new HttpClient();
        static TileService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("MetroPlanner/1.0 (personal use)");
        }

        public string Key { get; }
        public string BaseUrl { get; }

        public TileService(string key, string baseUrl)
        {
            Key = key;
            BaseUrl = baseUrl;
        }

        public string Dir => Path.Combine(CacheRoot, Key);
        public string TilePath(int z, int x, int y) => Path.Combine(Dir, z.ToString(), x.ToString(), $"{y}.png");

        public bool Exists(int z, int x, int y) => File.Exists(TilePath(z, x, y));

        public BitmapImage Load(int z, int x, int y)
        {
            var p = TilePath(z, x, y);
            if (!File.Exists(p)) return null;
            try
            {
                var bytes = File.ReadAllBytes(p);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public async Task<bool> DownloadAsync(int z, int x, int y)
        {
            var p = TilePath(z, x, y);
            if (File.Exists(p)) return true;
            var url = BaseUrl.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
            try
            {
                var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllBytes(p, bytes);
                return true;
            }
            catch { return false; }
        }

        public long Count()
        {
            if (!Directory.Exists(Dir)) return 0;
            long c = 0;
            foreach (var f in Directory.EnumerateFiles(Dir, "*.png", SearchOption.AllDirectories)) c++;
            return c;
        }

        public void Clear()
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        }
    }

    public static class TileMath
    {
        public static int LonToTileX(double lng, int z) => (int)Math.Floor((lng + 180.0) / 360.0 * Math.Pow(2, z));
        public static int LatToTileY(double lat, int z)
        {
            double s = Math.Sin(lat * Math.PI / 180.0);
            return (int)Math.Floor((0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * Math.Pow(2, z));
        }
        public static (int x0, int y0, int x1, int y1) BboxTiles(double west, double south, double east, double north, int z)
        {
            int x0 = LonToTileX(west, z), x1 = LonToTileX(east, z);
            int y0 = LatToTileY(north, z), y1 = LatToTileY(south, z);
            if (x0 > x1) (x0, x1) = (x1, x0);
            if (y0 > y1) (y0, y1) = (y1, y0);
            return (x0, y0, x1, y1);
        }
    }
}
