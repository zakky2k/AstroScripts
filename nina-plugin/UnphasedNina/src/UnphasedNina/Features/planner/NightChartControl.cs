using NINA.Core.Utility;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zakfo.Unphased.Features.settings;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// Draws the night chart (sun curve, twilight bands, moon track) by code
    /// in a plain FrameworkElement. This keeps the plugin low-impact: no
    /// custom controls, no styles that could leak into other N.I.N.A. views.
    /// Colors come from PlannerPalette: live N.I.N.A. theme brushes by default,
    /// red night-vision scheme when the DARK toggle is on.
    /// </summary>
    public class NightChartControl : FrameworkElement {
        private const double ChartHeight = 260;

        private readonly PlannerPalette palette = new();
        private NightRowVM row;
        private bool darkMode;

        public NightChartControl() {
            Loaded += (_, _) => RequestRerender();
            DataContextChanged += (_, _) => HookDarkMode();
        }

        private NightPlannerVM hookedPlanner;

        private void HookDarkMode() {
            if (hookedPlanner != null) hookedPlanner.PropertyChanged -= PlannerChanged;
            hookedPlanner = FindPlanner(DataContext);
            if (hookedPlanner != null) {
                hookedPlanner.PropertyChanged += PlannerChanged;
                SetDarkMode(hookedPlanner.DarkMode);
            }
        }

        private static NightPlannerVM FindPlanner(object dc) => dc switch {
            NightPlannerVM p => p,
            Features.planner.PlannerDockableVM d => d.Planner,
            _ => null
        };

        private void PlannerChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(NightPlannerVM.DarkMode)) SetDarkMode(hookedPlanner.DarkMode);
        }

        /// <summary>Attach the row whose payload should be drawn.</summary>
        public void SetRow(NightRowVM row) {
            this.row = row;
            RequestRerender();
        }

        public void SetDarkMode(bool dark) {
            if (darkMode == dark) return;
            darkMode = dark;
            RequestRerender();
        }

        protected override Size MeasureOverride(Size availableSize) =>
            new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, ChartHeight);

        protected override void OnRender(DrawingContext dc) {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 10 || h <= 10) return;
            dc.DrawRectangle(palette.ChartBackground(darkMode), null, new Rect(0, 0, w, h));
            if (row == null) {
                var tip = DrawText("no night selected", 9, palette.Gridline(darkMode));
                dc.DrawText(tip, new Point((w - tip.Width) / 2, h / 2 - tip.Height / 2));
                return;
            }

            // Two nights side by side while they stay legible; below that width
            // show one full noon-to-noon night stretched over the same pixels.
            double spanMinutes = NightChartVM.FrameSpanMinutes(w);
            double sx = w / (NightChartVM.Width * spanMinutes / NightChartVM.FullFrameMinutes);
            double sy = h / NightChartVM.Height;

            DrawTwilightShading(dc, h, sx);
            DrawGrid(dc, w, h, sx, sy, spanMinutes);
            DrawCurve(dc, row.Chart.SunPolyline, palette.Ink(darkMode), 1.6, sx, sy);
            DrawCurve(dc, row.Chart.MoonPolyline, palette.MoonTrack(darkMode), 1.2, sx, sy);
            DrawSunMarkers(dc, w, h, sx, sy);
            DrawMoonMarkers(dc, w, h, sx, sy);
            DrawLegend(dc);
        }

        private void DrawGrid(DrawingContext dc, double w, double h, double sx, double sy, double spanMinutes) {
            var pen = new Pen(palette.Gridline(darkMode), 0.5);
            var label = palette.Gridline(darkMode);
            // horizon (alt 0) and the -6/-12/-18 twilight lines, labelled on the right
            foreach (double alt in new[] { 0.0, -6.0, -12.0, -18.0 }) {
                double y = NightChartVM.YSun(alt) * sy;
                dc.DrawLine(pen, new Point(0, y), new Point(w, y));
                var tag = DrawText($"{alt:F0}\u00b0", 7, label);
                dc.DrawText(tag, new Point(w - tag.Width - 3, y + 1));
            }
            // hour ticks every 2 hours across the visible window, labelled every 4
            for (int min = 0; min <= spanMinutes; min += 120) {
                double x = min / NightChartVM.FullFrameMinutes * NightChartVM.Width * sx;
                dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
                if (min % 240 == 0 && min < spanMinutes) {
                    var tag = DrawText($"{(12 + min / 60) % 24:00}:00", 7, label);
                    dc.DrawText(tag, new Point(Math.Min(x + 2, w - tag.Width - 2), h - tag.Height - 2));
                }
            }
        }

        /// <summary>
        /// Paint the day / civil / nautical / astronomical twilight transition as
        /// background colour over time, so sunset to sunrise reads at a glance
        /// instead of relying on the horizontal altitude gridlines alone.
        /// </summary>
        private void DrawTwilightShading(DrawingContext dc, double h, double sx) {
            var series = row.Payload.SunSeries;
            for (int i = 0; i + 1 < series.Count; i++) {
                var brush = palette.Twilight(darkMode, (series[i].Altitude + series[i + 1].Altitude) / 2);
                if (brush == null) continue;
                double x0 = row.Chart.XAt(series[i].Time) * sx;
                double x1 = row.Chart.XAt(series[i + 1].Time) * sx;
                if (x1 <= x0) continue;
                dc.DrawRectangle(brush, null, new Rect(x0, 0, x1 - x0 + 0.5, h));
            }
        }

        private void DrawCurve(DrawingContext dc, string polyline, Brush brush, double thickness, double sx, double sy) {
            if (string.IsNullOrEmpty(polyline)) return;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open()) {
                bool first = true;
                foreach (var pair in polyline.Split(' ')) {
                    var xy = pair.Split(',');
                    if (xy.Length != 2) continue;
                    var pt = new Point(
                        double.Parse(xy[0], CultureInfo.InvariantCulture) * sx,
                        double.Parse(xy[1], CultureInfo.InvariantCulture) * sy);
                    if (first) { ctx.BeginFigure(pt, false, false); first = false; }
                    else ctx.LineTo(pt, true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(brush, thickness), geo);
        }

        private void DrawSunMarkers(DrawingContext dc, double w, double h, double sx, double sy) {
            var accent = palette.Accent(darkMode);
            var pen = new Pen(accent, 1.0);
            double xSet = row.SunsetX * sx, xRise = row.SunriseX * sx;
            dc.DrawLine(pen, new Point(xSet, 0), new Point(xSet, h));
            dc.DrawLine(pen, new Point(xRise, 0), new Point(xRise, h));
            var lbl = DrawText("sunset", 7, accent);
            dc.DrawText(lbl, new Point(xSet + 3, 2));
            var lbl2 = DrawText("sunrise", 7, accent);
            dc.DrawText(lbl2, new Point(Math.Min(xRise + 3, w - lbl2.Width - 2), 2));
        }

        /// <summary>
        /// Make the moon readable: moonrise/moonset crossings within the visible
        /// window, plus the phase glyph at the moon's highest point of that night
        /// labelled with its illumination. Without these the moon track is just an
        /// unlabelled arc and reads as missing.
        /// </summary>
        private void DrawMoonMarkers(DrawingContext dc, double w, double h, double sx, double sy) {
            var p = row.Payload;
            var moon = palette.MoonTrack(darkMode);

            foreach (var ev in p.Moon.Rise.Select(t => (time: t, name: "moonrise"))
                       .Concat(p.Moon.Set.Select(t => (time: t, name: "moonset")))) {
                double x = row.Chart.XAt(ev.time) * sx;
                if (x < 2 || x > w - 2) continue;
                dc.DrawLine(new Pen(moon, 0.8) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) },
                    new Point(x, 0), new Point(x, h));
                var lbl = DrawText(ev.name, 7, moon);
                dc.DrawText(lbl, new Point(Math.Clamp(x + 3, 0, w - lbl.Width - 2), h - lbl.Height - 16));
            }

            var peak = row.Chart.NightMoonPeak();
            double px = row.Chart.XAt(peak.Time) * sx;
            double py = NightChartVM.YMoon(peak.Altitude) * sy;
            // Limb is which side is lit as THIS observer sees it, so southern
            // hemispheres get a mirrored glyph; absent data falls back to waxing.
            bool litOnRight = !string.Equals(p.Limb, "left", StringComparison.OrdinalIgnoreCase);
            DrawMoonGlyph(dc, px, py, 7, peak.IllumFraction, litOnRight);
            var tag = DrawText($"{Math.Round(peak.IllumFraction * 100)}% moon at {peak.Altitude:F0}\u00b0", 8, moon);
            dc.DrawText(tag, new Point(
                Math.Clamp(px + 10, 0, w - tag.Width - 2),
                Math.Clamp(py - tag.Height - 6, 0, h - tag.Height)));
        }

        /// <summary>
        /// Draw the moon as a phase glyph, not a dot: the lit part is bounded by
        /// the outer limb semicircle on the lit side and by the terminator, which
        /// is the half ellipse of half-width r*(1-2f) implied by illuminated
        /// fraction f - bulging toward the dark side when gibbous, toward the lit
        /// side when crescent.
        /// </summary>
        private void DrawMoonGlyph(DrawingContext dc, double cx, double cy, double r, double illum, bool litOnRight) {
            var brush = palette.MoonTrack(darkMode);
            var outline = new Pen(brush, 0.8);
            if (illum <= 0.01) {
                // New moon: nothing is lit, so show the unlit outline only.
                dc.DrawEllipse(null, outline, new Point(cx, cy), r, r);
                return;
            }
            dc.DrawGeometry(brush, outline, MoonPhaseGeometry(cx, cy, r, illum, litOnRight));
        }

        /// <summary>
        /// The lit part of a phase glyph as a geometry. Public so the shape
        /// logic is unit-testable without a rendering pass.
        /// </summary>
        public static Geometry MoonPhaseGeometry(double cx, double cy, double r, double illum, bool litOnRight) {
            double f = Math.Clamp(illum, 0.0, 1.0);
            if (f >= 0.999) return new EllipseGeometry(new Point(cx, cy), r, r);
            double halfWidth = r * Math.Abs(1 - 2 * f);
            var top = new Point(cx, cy - r);
            var bottom = new Point(cx, cy + r);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open()) {
                ctx.BeginFigure(top, true, true);
                ctx.ArcTo(bottom, new Size(r, r), 0, false, Sweep(litOnRight), true, false);
                bool gibbous = f > 0.5;
                ctx.ArcTo(top, new Size(halfWidth, r), 0, false, Sweep(gibbous ? litOnRight : !litOnRight), true, false);
            }
            geo.Freeze();
            return geo;
        }

        /// <summary>Clockwise in WPF's y-down space, which is what the screen shows.</summary>
        private static SweepDirection Sweep(bool clockwise) =>
            clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;

        /// <summary>Name the two curves; a bare arc is otherwise ambiguous.</summary>
        private void DrawLegend(DrawingContext dc) {
            DrawKey(dc, 7, palette.Ink(darkMode), "sun altitude");
            DrawKey(dc, 21, palette.MoonTrack(darkMode), "moon altitude");
        }

        private void DrawKey(DrawingContext dc, double y, Brush brush, string text) {
            dc.DrawLine(new Pen(brush, 1.6), new Point(6, y), new Point(24, y));
            var t = DrawText(text, 8, brush);
            dc.DrawText(t, new Point(28, y - t.Height / 2));
        }

        private FormattedText DrawText(string text, double size, Brush brush) =>
            new(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

        public void RequestRerender() => InvalidateVisual();
    }
}
