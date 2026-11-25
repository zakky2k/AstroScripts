using Zakfo.Unphased.Core;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// Chart geometry for one night payload, in abstract canvas units. The X
    /// axis spans the whole noon-to-noon+2 frame (Unphased's fixed scale, so
    /// nights stay comparable); Y maps the usable altitude band to 0..Height. The view
    /// scales these by its own pixel size, which keeps this class testable and
    /// free of WPF types.
    /// </summary>
    public sealed class NightChartVM {
        public const double Width = 1000;
        public const double Height = 320;

        /// <summary>The payload always covers 48 hours; the view may show less.</summary>
        public const double FullFrameMinutes = 48 * 60;

        /// <summary>One night: local noon to the next local noon.</summary>
        public const double OneNightMinutes = 24 * 60;

        /// <summary>Below this many pixels two nights are too cramped to read.</summary>
        public const double TwoNightMinimumWidth = 1040;

        /// <summary>
        /// Visible span for a chart of the given pixel width: two nights side by
        /// side on a wide panel, otherwise one noon-to-noon night stretched to
        /// the same width so the time scale adapts instead of the text shrinking.
        /// </summary>
        public static double FrameSpanMinutes(double pixelWidth) =>
            pixelWidth >= TwoNightMinimumWidth ? FullFrameMinutes : OneNightMinutes;

        public NightPayload Payload { get; }

        public NightChartVM(NightPayload payload) => Payload = payload;

        /// <summary>
        /// Altitude window the chart shows, top edge first. A +-90 domain spent two
        /// thirds of the panel on empty sky, which squashed the twilight bands and the
        /// labels between them together; +80 still shows high summer moons, -32 leaves
        /// a visible band under astronomical night. Content outside it is flattened
        /// against the edge by Clip rather than dropped.
        /// </summary>
        public const double TopAltitude = 80;
        public const double BottomAltitude = -32;

        /// <summary>Sun altitude band: TopAltitude at top, BottomAltitude at bottom.</summary>
        public static double YSun(double altitude) =>
            (TopAltitude - altitude) / (TopAltitude - BottomAltitude) * Height;

        /// <summary>Moon altitude band: same mapping as the sun curve.</summary>
        public static double YMoon(double altitude) => YSun(altitude);

        /// <summary>X position for an instant on the abstract canvas (full 48 h frame = 0..Width).</summary>
        public static double XAt(NightPayload payload, DateTimeOffset t) =>
            (t - payload.FrameNoon).TotalMinutes / FullFrameMinutes * Width;

        public double XAt(DateTimeOffset t) => XAt(Payload, t);

        /// <summary>Polyline points ("x,y x,y ...") for the sun series, clipped to canvas height.</summary>
        public string SunPolyline {
            get {
                var pts = new System.Text.StringBuilder();
                foreach (var s in Payload.SunSeries) {
                    if (pts.Length > 0) pts.Append(' ');
                    pts.Append(XAt(s.Time).ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                        .Append(',')
                        .Append(Clip(YSun(s.Altitude)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return pts.ToString();
            }
        }

        /// <summary>
        /// Polyline for the moon track over the whole frame, like the sun curve:
        /// the moon is worth watching before sunset and after sunrise too, and
        /// a track that stops at the horizon reads as if the moon were missing.
        /// </summary>
        public string MoonPolyline {
            get {
                var pts = new System.Text.StringBuilder();
                foreach (var m in Payload.MoonSamples) {
                    if (pts.Length > 0) pts.Append(' ');
                    pts.Append(XAt(m.Time).ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                        .Append(',')
                        .Append(Clip(YMoon(m.Altitude)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return pts.ToString();
            }
        }

        /// <summary>
        /// Highest moon sample of the night itself (sunset to sunrise), which is
        /// where the phase glyph belongs - the useful altitude for an imaging run.
        /// </summary>
        public MoonSample NightMoonPeak() {
            MoonSample best = null;
            foreach (var m in Payload.MoonSamples) {
                if (m.Time < Payload.Sunset || m.Time > Payload.Sunrise) continue;
                if (best == null || m.Altitude > best.Altitude) best = m;
            }
            return best ?? Payload.MoonSamples[0];
        }

        /// <summary>
        /// One continuous stretch of background shading: canvas x range plus the
        /// twilight band (see PlannerPalette.TwilightBand, -1 = full night).
        /// </summary>
        public readonly struct ShadingRun {
            public ShadingRun(double x0, double x1, int band) { X0 = x0; X1 = x1; Band = band; }
            public double X0 { get; }
            public double X1 { get; }
            public int Band { get; }
        }

        /// <summary>
        /// The sun series collapsed into one shading run per stretch of band, not one
        /// rectangle per sample. At canvas scale a 10-minute sample is about 3 canvas
        /// units wide, and a row of hairline-width antialiased rectangles in the same
        /// colour reads as a comb of vertical lines - the "lines that mean nothing"
        /// complaint. Neighbouring runs here share an exact edge, so no overlap
        /// fudge factor is needed either.
        /// </summary>
        public static System.Collections.Generic.List<ShadingRun> ShadingRuns(NightPayload payload) {
            var series = payload.SunSeries;
            var runs = new System.Collections.Generic.List<ShadingRun>();
            for (int i = 0; i + 1 < series.Count; i++) {
                int band = PlannerPalette.TwilightBand((series[i].Altitude + series[i + 1].Altitude) / 2);
                double x1 = XAt(payload, series[i + 1].Time);
                if (runs.Count > 0 && runs[runs.Count - 1].Band == band) {
                    var prev = runs[runs.Count - 1];
                    runs[runs.Count - 1] = new ShadingRun(prev.X0, x1, band);
                    continue;
                }
                runs.Add(new ShadingRun(XAt(payload, series[i].Time), x1, band));
            }
            return runs;
        }

        private static double Clip(double y) => Math.Clamp(y, 0, Height);
    }
}
