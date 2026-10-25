using System;
using System.IO;

namespace NaturalWeather
{
    /// <summary>
    /// Where a world's cloud usually is (since 2026-10-21; stock Kerbin's alone): its total cover, its low cloud's alone and its high
    /// cloud's, by place and the time of day, the year's average of a weather model run over the world itself: the Model for
    /// Prediction Across Scales, by the Kerbal Weather Project (cmac994; MIT licence, Copyright (c) 2021 cmac994: see
    /// PluginData/kerbin-clouds.txt). Over Kerbin the weather's own regional wet and dry (Model.Sample) are set by it: the storm
    /// tracks, the subtropics and the poles, the land and the sea, where a physical model of that air over that land and sea puts
    /// them. (Asked by the owner, 2026-10-21, of that mod's model's satellite pictures: "their clouds are better /more realistic".)
    /// The file: "NWKC", then int32 version, latitudes, longitudes, times, fields; then bytes [field][time][latitude][longitude],
    /// 0 to 255 for 0 to 1; latitude from -90 every 2 degrees, longitude from -180 every 2, the times every hour of Kerbin's day
    /// by the model's own clock (the game's time plus 6316.5375 s, as the Kerbal Weather Project keeps it).
    /// </summary>
    sealed class CloudClimate
    {
        public const int Total = 0, LowAlone = 1, High = 2;
        const double Offset = 6316.5375, Day = 21600.0;

        readonly byte[] data;
        readonly int lats, lons, times, fields;
        /// <summary>The average of the total cover over the world and the day (each place weighed by its area).</summary>
        public readonly float Mean;

        CloudClimate(byte[] data, int lats, int lons, int times, int fields)
        {
            this.data = data; this.lats = lats; this.lons = lons; this.times = times; this.fields = fields;
            double sum = 0.0, area = 0.0;
            for (int t = 0; t < times; t++)
                for (int j = 0; j < lats; j++)
                {
                    double wgt = Math.Cos((-90.0 + 180.0 * j / (lats - 1)) * Math.PI / 180.0);
                    for (int i = 0; i < lons; i++) { sum += wgt * data[(t * lats + j) * lons + i]; area += wgt; }
                }
            Mean = (float)(sum / Math.Max(area, 1e-9) / 255.0);
        }

        /// <summary>The climate kept for a world, if it has one: stock Kerbin's alone, by its name and size (a planet pack's or Real Worlds'
        /// world of that name is another world, with other land and sea). On the game's thread: the game's folder is asked of Unity.</summary>
        public static CloudClimate For(string name, double radius) =>
            name == "Kerbin" && Math.Abs(radius - 600000.0) < 1.0 ? Load(KSPUtil.ApplicationRootPath + "GameData/NaturalWeather/PluginData/kerbin-clouds.bin") : null;

        /// <summary>The climate read from its file, or nothing if it is not there or not whole.</summary>
        public static CloudClimate Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] all = File.ReadAllBytes(path);
                if (all.Length < 24 || all[0] != (byte)'N' || all[1] != (byte)'W' || all[2] != (byte)'K' || all[3] != (byte)'C') return null;
                int version = BitConverter.ToInt32(all, 4), lats = BitConverter.ToInt32(all, 8), lons = BitConverter.ToInt32(all, 12), times = BitConverter.ToInt32(all, 16), fields = BitConverter.ToInt32(all, 20);
                if (version != 1 || lats < 2 || lons < 2 || times < 1 || fields < 3 || all.Length != 24 + lats * lons * times * fields) return null;
                var data = new byte[lats * lons * times * fields];
                Buffer.BlockCopy(all, 24, data, 0, data.Length);
                return new CloudClimate(data, lats, lons, times, fields);
            }
            catch (Exception) { return null; }
        }

        /// <summary>One field at a place and moment, 0 to 1: between the grid's points, and between its hours.</summary>
        public float At(int field, double lat, double lon, double ut)
        {
            double t = ((ut + Offset) % Day + Day) % Day / Day * times;
            int t0 = (int)t % times, t1 = (t0 + 1) % times;
            float ft = (float)(t - Math.Floor(t));
            double y = (Math.Max(-90.0, Math.Min(90.0, lat)) + 90.0) / 180.0 * (lats - 1);
            double x = ((lon + 180.0) % 360.0 + 360.0) % 360.0 / 360.0 * lons;
            int y0 = Math.Min(lats - 2, (int)y), x0 = (int)x % lons, x1 = (x0 + 1) % lons;
            float fy = (float)(y - y0), fx = (float)(x - Math.Floor(x));
            float a = Read(field, t0, y0, x0, x1, fy, fx), b = Read(field, t1, y0, x0, x1, fy, fx);
            return (a + (b - a) * ft) / 255f;
        }

        float Read(int field, int t, int y0, int x0, int x1, float fy, float fx)
        {
            int row = (field * times + t) * lats;
            int r0 = (row + y0) * lons, r1 = (row + y0 + 1) * lons;
            float p = data[r0 + x0] + (data[r0 + x1] - data[r0 + x0]) * fx;
            float q = data[r1 + x0] + (data[r1 + x1] - data[r1 + x0]) * fx;
            return p + (q - p) * fy;
        }
    }
}
