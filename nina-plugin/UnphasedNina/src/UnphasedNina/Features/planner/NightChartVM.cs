using Zakfo.Unphased.Core;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// Chart geometry for one night payload, in abstract canvas units. The X
    /// axis spans the whole noon-to-noon+2 frame (Unphased's fixed scale, so
    /// nights stay comparable); Y maps the visible altitude band to 0..Height. The view
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
        /// Altitude window the chart shows, top edge first: the whole sky, +90 at the
        /// zenith down to -90 at the floor. A narrower window (it was +80/-32 for a
        /// while) makes the twilight bands roomier, but it also cut off high moons and
        /// flattened the deep part of the night against the bottom edge, so the full
        /// domain stays. Legibility comes from the label chips and their de-stacking
        /// walk instead, and content outside the window is cut off at the edge by
        /// TrimmedPolyline rather than smeared along it.
        /// </summary>
        public const double TopAltitude = 90;
        public const double BottomAltitude = -90;

        /// <summary>Sun altitude band: TopAltitude at top, BottomAltitude at bottom.</summary>
        public static double YSun(double altitude) =>
            (TopAltitude - altitude) / (TopAltitude - BottomAltitude) * Height;

        /// <summary>Moon altitude band: same mapping as the sun curve.</summary>
        public static double YMoon(double altitude) => YSun(altitude);

        /// <summary>X position for an instant on the abstract canvas (full 48 h frame = 0..Width).</summary>
        public static double XAt(NightPayload payload, DateTimeOffset t) =>
            (t - payload.FrameNoon).TotalMinutes / FullFrameMinutes * Width;

        public double XAt(DateTimeOffset t) => XAt(Payload, t);

        /// <summary>Canvas x for a minute offset from the frame start (local noon).</summary>
        public static double XAtMinutes(double minutes) => minutes / FullFrameMinutes * Width;

        /// <summary>Minute offset from the frame start for a canvas x - the inverse of XAtMinutes.</summary>
        public static double MinutesAtX(double x) => x / Width * FullFrameMinutes;

        /// <summary>The instant a minute offset from the frame start points at.</summary>
        public DateTimeOffset TimeAtMinutes(double minutes) => Payload.FrameNoon.AddMinutes(minutes);

        /// <summary>
        /// Snap a minute offset to the nearest whole hour. The frame starts at local
        /// noon, so whole hours from the frame start are whole clock hours.
        /// </summary>
        public static double SnapToHour(double minutes) =>
            System.Math.Round(minutes / 60, System.MidpointRounding.AwayFromZero) * 60;

        private System.Collections.Generic.List<(System.DateTimeOffset Time, double Altitude)> sunPoints, moonPoints;

        private System.Collections.Generic.List<(DateTimeOffset Time, double Altitude)> SunPoints =>
            sunPoints ??= ToPoints(Payload.SunSeries);

        private System.Collections.Generic.List<(DateTimeOffset Time, double Altitude)> MoonPoints =>
            moonPoints ??= ToPoints(Payload.MoonSamples);

        private static System.Collections.Generic.List<(DateTimeOffset Time, double Altitude)> ToPoints(
            System.Collections.Generic.IReadOnlyList<SunSample> series) {
            var pts = new System.Collections.Generic.List<(DateTimeOffset, double)>(series.Count);
            foreach (var s in series) pts.Add((s.Time, s.Altitude));
            return pts;
        }

        private static System.Collections.Generic.List<(DateTimeOffset Time, double Altitude)> ToPoints(
            System.Collections.Generic.IReadOnlyList<MoonSample> series) {
            var pts = new System.Collections.Generic.List<(DateTimeOffset, double)>(series.Count);
            foreach (var m in series) pts.Add((m.Time, m.Altitude));
            return pts;
        }

        /// <summary>
        /// Sun curve over the whole frame, cut off where it leaves the altitude window.
        /// </summary>
        public string SunPolyline => TrimmedPolyline(Payload, SunPoints, YSun);

        /// <summary>
        /// Moon track over the whole frame, like the sun curve: the moon is worth
        /// watching before sunset and after sunrise too, and a track that stops at the
        /// horizon reads as if the moon were missing.
        /// </summary>
        public string MoonPolyline => TrimmedPolyline(Payload, MoonPoints, YMoon);

        /// <summary>
        /// The path data for a curve: points joined by spaces, and separate drawable
        /// runs separated by " | ". A curve that leaves the altitude window **stops at
        /// the edge** and the next run starts where it comes back - clamping each
        /// sample to the edge instead used to draw a flat horizontal line along the
        /// bottom of the panel, which reads as an event that never happened.
        /// </summary>
        public static string TrimmedPolyline(
            NightPayload payload,
            System.Collections.Generic.IReadOnlyList<(DateTimeOffset Time, double Altitude)> series,
            System.Func<double, double> yOf) {
            var sb = new System.Text.StringBuilder();
            bool open = false;          // a run is currently being written
            double px = 0, py = 0;
            bool pInside = false, pAbove = false;

            for (int i = 0; i < series.Count; i++) {
                double x = XAt(payload, series[i].Time);
                double y = yOf(series[i].Altitude);
                bool inside = y >= 0 && y <= Height;
                bool above = y < 0;

                if (i == 0) {
                    if (inside) { AppendPoint(sb, x, y, ref open); open = true; }
                } else if (inside && pInside) {
                    AppendPoint(sb, x, y, ref open);
                    open = true;
                } else if (inside && !pInside) {
                    // Coming back in: start a fresh run at the edge, then the sample.
                    var (ex, ey) = EdgeCrossing(px, py, x, y, pAbove);
                    AppendPoint(sb, ex, ey, ref open);
                    AppendPoint(sb, x, y, ref open);
                    open = true;
                } else if (!inside && pInside) {
                    // Leaving: end the run exactly at the edge.
                    var (ex, ey) = EdgeCrossing(px, py, x, y, above);
                    AppendPoint(sb, ex, ey, ref open);
                    open = false;
                } else if (above != pAbove) {
                    // Outside at both ends but on opposite sides, so the segment
                    // crossed the whole window: show the part inside it.
                    var (ax, ay) = EdgeCrossing(px, py, x, y, pAbove);
                    var (bx, by) = EdgeCrossing(px, py, x, y, above);
                    AppendPoint(sb, ax, ay, ref open);
                    AppendPoint(sb, bx, by, ref open);
                    open = false;
                }

                px = x; py = y; pInside = inside; pAbove = above;
            }
            return sb.ToString();
        }

        /// <summary>Where the straight segment from an inside point to an outside point meets the window edge.</summary>
        private static (double X, double Y) EdgeCrossing(
            double px, double py, double x, double y, bool outsideIsAbove) {
            double edge = outsideIsAbove ? 0 : Height;
            double t = (edge - py) / (y - py);
            return (px + t * (x - px), edge);
        }

        private static void AppendPoint(System.Text.StringBuilder sb, double x, double y, ref bool open) {
            if (open) sb.Append(' ');
            else if (sb.Length > 0) sb.Append(" | ");
            sb.Append(x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
              .Append(',')
              .Append(y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Altitude of a series at an instant, interpolated between the two samples
        /// around it (NaN outside the frame), so a scrub readout is not tied to the
        /// sampling grid.
        /// </summary>
        public static double AltitudeAt(
            System.Collections.Generic.IReadOnlyList<(DateTimeOffset Time, double Altitude)> series,
            DateTimeOffset t) {
            if (series.Count == 0 || t < series[0].Time || t > series[series.Count - 1].Time) return double.NaN;
            for (int i = 0; i + 1 < series.Count; i++) {
                if (t < series[i + 1].Time) {
                    double span = (series[i + 1].Time - series[i].Time).TotalMinutes;
                    double f = span <= 0 ? 0 : (t - series[i].Time).TotalMinutes / span;
                    return series[i].Altitude + f * (series[i + 1].Altitude - series[i].Altitude);
                }
            }
            return series[series.Count - 1].Altitude;
        }

        public double SunAltitudeAt(DateTimeOffset t) => AltitudeAt(SunPoints, t);
        public double MoonAltitudeAt(DateTimeOffset t) => AltitudeAt(MoonPoints, t);

        /// <summary>
        /// The readout shown at a scrub position: clock time plus where the sun and
        /// the moon actually are at that instant.
        /// </summary>
        public string ScrubReadout(double minutes) {
            var t = TimeAtMinutes(minutes);
            return $"{t.LocalDateTime:HH:mm}  sun {Signed(SunAltitudeAt(t))}  moon {Signed(MoonAltitudeAt(t))}";
        }

        private static string Signed(double altitude) =>
            double.IsNaN(altitude) ? "n/a"
            : altitude >= 0 ? $"+{altitude:F0}\u00b0" : $"{altitude:F0}\u00b0";

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
        /// Chip top for a label hung **under** a horizontal line. Falls back to
        /// hanging it above when the panel is too short to fit it below, so a label
        /// never leaves the canvas.
        /// </summary>
        public static double ChipBelow(double lineY, double labelHeight, double canvasHeight) =>
            lineY + 4 + labelHeight <= canvasHeight ? lineY + 4 : lineY - 4 - labelHeight;

        /// <summary>Chip top for a label hung **above** a horizontal line, mirrored fallback.</summary>
        public static double ChipAbove(double lineY, double labelHeight, double canvasHeight) =>
            lineY - 4 - labelHeight >= 0 ? lineY - 4 - labelHeight : lineY + 4;

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
    }
}
