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

        // Label sizes in device-independent pixels, sized for how wide the panel
        // actually gets on a modern screen: the previous 7-8 px was unreadable.
        private const double LabelFs = 10;    // altitude degrees, hour clock
        private const double MarkerFs = 11;   // sunset / sunrise / moonrise / moonset
        private const double NoteFs = 12;     // the "NN% moon at NN deg" readout
        private const double LegendFs = 11;
        private const double TipFs = 13;

        // Phase glyph radius. Deliberately not scaled by the vertical stretch, or
        // the moon turns into an ellipse.
        private const double MoonGlyphRadius = 10.5;

        private const double ChipPadX = 3, ChipPadY = 2;

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
                var s = LabelSize("no night selected", TipFs);
                DrawChip(dc, "no night selected", TipFs, (w - s.Width) / 2, h / 2 - s.Height / 2,
                    palette.Ink(darkMode), w, h);
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
            var faint = new Pen(palette.Gridline(darkMode), 0.6);
            var horizon = new Pen(palette.Horizon(darkMode), 1.6);
            var ink = palette.Ink(darkMode);

            // The horizon is the reference line every altitude is read against, so it
            // gets a heavier pen and is drawn last, over the faint twilight lines.
            foreach (double alt in new[] { -18.0, -12.0, -6.0, 0.0 }) {
                double y = NightChartVM.YSun(alt) * sy;
                dc.DrawLine(alt == 0.0 ? horizon : faint, new Point(0, y), new Point(w, y));
            }
            // Hour lines every 4 hours across the visible window: at 2 hours they read
            // as clutter rather than as a scale.
            for (int min = 0; min <= spanMinutes; min += 240) {
                double x = min / NightChartVM.FullFrameMinutes * NightChartVM.Width * sx;
                dc.DrawLine(faint, new Point(x, 0), new Point(x, h));
                if (min >= spanMinutes) continue;
                string clock = $"{(12 + min / 60) % 24:00}:00";
                var s = LabelSize(clock, LabelFs);
                DrawChip(dc, clock, LabelFs, Math.Min(x + 3, w - s.Width), h - s.Height - 3, ink, w, h);
            }
            // Every altitude line carries its degrees on the right, boxed so the band
            // behind it cannot swallow the number. Walked top to bottom, pushing a chip
            // down when the panel is too short for the 6 degree gap, so labels can
            // never stack into an unreadable pile.
            double floor = double.MinValue;
            foreach (double alt in new[] { 0.0, -6.0, -12.0, -18.0 }) {
                double y = NightChartVM.YSun(alt) * sy;
                string degrees = $"{alt:F0}\u00b0";
                var s = LabelSize(degrees, LabelFs);
                double top = y - s.Height / 2;
                if (top < floor + 2) top = floor + 2;
                DrawChip(dc, degrees, LabelFs, w - s.Width - 3, top,
                    alt == 0.0 ? palette.Horizon(darkMode) : ink, w, h);
                floor = top + s.Height;
            }
        }

        /// <summary>
        /// Paint the day / civil / nautical / astronomical twilight transition as
        /// background colour over time, so sunset to sunrise reads at a glance
        /// instead of relying on the horizontal altitude gridlines alone. One
        /// rectangle per stretch of band (see NightChartVM.ShadingRuns), which is
        /// what keeps the band edges from looking like stray vertical lines.
        /// </summary>
        private void DrawTwilightShading(DrawingContext dc, double h, double sx) {
            foreach (var run in NightChartVM.ShadingRuns(row.Payload)) {
                var brush = palette.TwilightBandBrush(darkMode, run.Band);
                if (brush == null) continue;
                double x0 = run.X0 * sx, x1 = run.X1 * sx;
                if (x1 <= x0) continue;
                dc.DrawRectangle(brush, null, new Rect(x0, 0, x1 - x0, h));
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
            var pen = new Pen(accent, 1.2);
            double xSet = row.SunsetX * sx, xRise = row.SunriseX * sx;
            dc.DrawLine(pen, new Point(xSet, 0), new Point(xSet, h));
            dc.DrawLine(pen, new Point(xRise, 0), new Point(xRise, h));
            DrawChip(dc, "sunset", MarkerFs, BoxLeft(xSet, "sunset", MarkerFs, w), 3, accent, w, h);
            DrawChip(dc, "sunrise", MarkerFs, BoxLeft(xRise, "sunrise", MarkerFs, w), 3, accent, w, h);
        }

        /// <summary>
        /// Left edge of a label box hung just to the right of a marker, flipped to the
        /// left of it when the box would otherwise run off the canvas.
        /// </summary>
        private double BoxLeft(double x, string text, double size, double w) {
            double left = x + 4;
            if (left + LabelSize(text, size).Width > w) left = x - 4 - LabelSize(text, size).Width;
            return Math.Clamp(left, 0, Math.Max(0, w - LabelSize(text, size).Width));
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
            var dash = new Pen(moon, 1.0) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            double horizonY = NightChartVM.YSun(0) * sy;

            foreach (var ev in p.Moon.Rise.Select(t => (time: t, name: "moonrise"))
                       .Concat(p.Moon.Set.Select(t => (time: t, name: "moonset")))) {
                double x = row.Chart.XAt(ev.time) * sx;
                if (x < 2 || x > w - 2) continue;
                dc.DrawLine(dash, new Point(x, 0), new Point(x, h));
                var s = LabelSize(ev.name, MarkerFs);
                // The word sits where the moon crosses the horizon, not in the clock row.
                DrawChip(dc, ev.name, MarkerFs, BoxLeft(x, ev.name, MarkerFs, w),
                    Math.Clamp(horizonY - s.Height - 4, 0, h - s.Height), moon, w, h);
            }

            var peak = row.Chart.NightMoonPeak();
            double px = row.Chart.XAt(peak.Time) * sx;
            double py = NightChartVM.YMoon(peak.Altitude) * sy;
            // Limb is which side is lit as THIS observer sees it, so southern
            // hemispheres get a mirrored glyph; absent data falls back to waxing.
            bool litOnRight = !string.Equals(p.Limb, "left", StringComparison.OrdinalIgnoreCase);
            DrawMoonGlyph(dc, px, py, MoonGlyphRadius, peak.IllumFraction, litOnRight);
            string note = $"{Math.Round(peak.IllumFraction * 100)}% moon at {peak.Altitude:F0}\u00b0";
            var ns = LabelSize(note, NoteFs);
            DrawChip(dc, note, NoteFs, BoxLeft(px + MoonGlyphRadius, note, NoteFs, w),
                Math.Clamp(py - ns.Height / 2, 0, h - ns.Height), moon, w, h);
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
            var outline = new Pen(brush, 1.0);
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
            DrawKey(dc, 10, palette.Ink(darkMode), "sun altitude");
            DrawKey(dc, 29, palette.MoonTrack(darkMode), "moon altitude");
        }

        private void DrawKey(DrawingContext dc, double y, Brush brush, string text) {
            dc.DrawLine(new Pen(brush, 1.8), new Point(8, y), new Point(28, y));
            DrawChip(dc, text, LegendFs, 32, y - LabelSize(text, LegendFs).Height / 2,
                brush, ActualWidth, ActualHeight);
        }

        /// <summary>Box size a label will occupy, for centring and right-aligning it.</summary>
        private Size LabelSize(string text, double size) {
            var t = DrawText(text, size, Brushes.Black);
            return new Size(t.Width + 2 * ChipPadX, t.Height + 2 * ChipPadY);
        }

        /// <summary>
        /// Draw a label as a chip: the text on a tight, near-black box edged with a
        /// hairline in the label's own colour. Without the backing, labels vanish over
        /// the bright daylight band and over the twilight overlays. x,y is the box
        /// top-left; it is clamped so a chip can never hang off the chart.
        /// </summary>
        private void DrawChip(DrawingContext dc, string text, double size, double x, double y, Brush ink, double w, double h) {
            var t = DrawText(text, size, ink);
            x = Math.Clamp(x, 0, Math.Max(0, w - t.Width - 2 * ChipPadX));
            y = Math.Clamp(y, 0, Math.Max(0, h - t.Height - 2 * ChipPadY));
            var box = new Rect(x, y, t.Width + 2 * ChipPadX, t.Height + 2 * ChipPadY);
            dc.DrawRoundedRectangle(palette.ChipFill(darkMode), new Pen(ink, 0.6), box, 2.5, 2.5);
            dc.DrawText(t, new Point(x + ChipPadX, y + ChipPadY));
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
