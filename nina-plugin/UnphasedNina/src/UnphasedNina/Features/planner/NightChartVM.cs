using Zakfo.Unphased.Core;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// Chart geometry for one night payload, in abstract canvas units. The X
    /// axis spans the whole noon-to-noon+2 frame (Unphased's fixed scale, so
    /// nights stay comparable); Y maps sun altitude +90..-90 to 0..1. The view
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

        /// <summary>Sun altitude band: +90 at top, -90 at bottom.</summary>
        public static double YSun(double altitude) => (90 - altitude) / 180.0 * Height;

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

        private static double Clip(double y) => Math.Clamp(y, 0, Height);
    }
}
