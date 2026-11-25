using NINA.Core.Utility;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // Mirror of the planner's scrub position (minutes from frame noon, null = off).
        // The planner owns it, this is only what OnRender draws.
        private double? scrubMinutes;
        private double reportedSpan = -1;

        public NightChartControl() {
            // Focusable so the arrow keys can step the scrub hour by hour.
            Focusable = true;
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
                SetScrub(hookedPlanner.ScrubMinutes);
            } else SetScrub(null);
        }

        private static NightPlannerVM FindPlanner(object dc) => dc switch {
            NightPlannerVM p => p,
            Features.planner.PlannerDockableVM d => d.Planner,
            _ => null
        };

        private void PlannerChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(NightPlannerVM.DarkMode)) SetDarkMode(hookedPlanner.DarkMode);
            if (e.PropertyName == nameof(NightPlannerVM.ScrubMinutes)) SetScrub(hookedPlanner.ScrubMinutes);
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

        /// <summary>
        /// Move the scrub marker (null takes it off). The planner view model owns the
        /// value; setting it here lets the chart be rendered on its own in tests, and
        /// is how a planner change reaches the drawing.
        /// </summary>
        public void SetScrub(double? minutes) {
            if (Nullable.Equals(scrubMinutes, minutes)) return;
            scrubMinutes = minutes;
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

            // The planner clamps the scrub to what is on screen, so the step buttons
            // cannot walk the marker past the right-hand edge of a one-night panel.
            if (hookedPlanner != null && System.Math.Abs(reportedSpan - spanMinutes) > 0.5) {
                reportedSpan = spanMinutes;
                hookedPlanner.SetVisibleSpan(spanMinutes);
            }

            DrawTwilightShading(dc, h, sx);
            DrawGrid(dc, w, h, sx, sy, spanMinutes);
            DrawCurve(dc, row.Chart.SunPolyline, palette.Ink(darkMode), 1.6, sx, sy);
            DrawCurve(dc, row.Chart.MoonPolyline, palette.MoonTrack(darkMode), 1.2, sx, sy);
            DrawSunMarkers(dc, w, h, sx, sy);
            DrawMoonMarkers(dc, w, h, sx, sy);
            DrawScrub(dc, w, h, sx, sy, spanMinutes);
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

        /// <summary>
        /// Draw a curve. Its path data can carry several runs separated by " | ": a
        /// curve that leaves the altitude window stops dead at the edge and the next
        /// run starts where it comes back (see NightChartVM.TrimmedPolyline), so each
        /// run begins a fresh figure. Joined into one figure instead, the gap would be
        /// bridged by a line hugging the edge - the flat horizontal line that looked
        /// like a feature of the sky.
        /// </summary>
        private void DrawCurve(DrawingContext dc, string polyline, Brush brush, double thickness, double sx, double sy) {
            if (string.IsNullOrEmpty(polyline)) return;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open()) {
                bool first = true;
                foreach (var run in polyline.Split('|')) {
                    foreach (var pair in run.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                        var xy = pair.Split(',');
                        if (xy.Length != 2) continue;
                        var pt = new Point(
                            double.Parse(xy[0], CultureInfo.InvariantCulture) * sx,
                            double.Parse(xy[1], CultureInfo.InvariantCulture) * sy);
                        if (first) { ctx.BeginFigure(pt, false, false); first = false; }
                        else ctx.LineTo(pt, true, false);
                    }
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(brush, thickness), geo);
        }

        /// <summary>
        /// Label sunset and sunrise where they happen, at the horizon, with the chip
        /// hung **under** the line so it does not collide with the moonrise/moonset
        /// chips, which hang above it. No full-height marker lines: they competed with
        /// the scrub line and read as verticals that mean nothing.
        /// </summary>
        private void DrawSunMarkers(DrawingContext dc, double w, double h, double sx, double sy) {
            var accent = palette.Accent(darkMode);
            double horizonY = NightChartVM.YSun(0) * sy;
            foreach (var (x, name) in new[] {
                (row.SunsetX * sx, "sunset"), (row.SunriseX * sx, "sunrise") }) {
                if (x < 2 || x > w - 2) continue;
                var s = LabelSize(name, MarkerFs);
                DrawChip(dc, name, MarkerFs, BoxLeft(x, name, MarkerFs, w),
                    NightChartVM.ChipBelow(horizonY, s.Height, h), accent, w, h);
            }
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
            double horizonY = NightChartVM.YSun(0) * sy;

            foreach (var ev in p.Moon.Rise.Select(t => (time: t, name: "moonrise"))
                       .Concat(p.Moon.Set.Select(t => (time: t, name: "moonset")))) {
                double x = row.Chart.XAt(ev.time) * sx;
                if (x < 2 || x > w - 2) continue;
                var s = LabelSize(ev.name, MarkerFs);
                // The word sits where the moon crosses the horizon, not in the clock
                // row, and no vertical is drawn through it: the crossing is the label's
                // own position, and a full-height dash was the stray line the scrub
                // line has now replaced.
                DrawChip(dc, ev.name, MarkerFs, BoxLeft(x, ev.name, MarkerFs, w),
                    NightChartVM.ChipAbove(horizonY, s.Height, h), moon, w, h);
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
        /// The scrub marker: one vertical the user moves hour by hour - by dragging
        /// across the chart, with the wheel, with the arrow keys or with the buttons in
        /// the panel header. It snaps to whole hours, carries a handle on the horizon so
        /// it looks like the control it is, puts a dot on each curve at that instant and
        /// prints where the sun and the moon actually are. That is the question the old
        /// fixed marker lines never answered.
        /// </summary>
        private void DrawScrub(DrawingContext dc, double w, double h, double sx, double sy, double spanMinutes) {
            if (scrubMinutes == null) return;
            double minutes = scrubMinutes.Value;
            if (minutes < 0 || minutes > spanMinutes) return;
            double x = NightChartVM.XAtMinutes(minutes) * sx;
            var ink = palette.Scrub(darkMode);
            var pen = new Pen(ink, 1.2) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) };
            dc.DrawLine(pen, new Point(x, 0), new Point(x, h));

            double horizonY = NightChartVM.YSun(0) * sy;
            dc.DrawEllipse(ink, new Pen(palette.ChipFill(darkMode), 1.0), new Point(x, horizonY), 4.5, 4.5);

            var chart = row.Chart;
            var instant = chart.TimeAtMinutes(minutes);
            DrawScrubDot(dc, x, NightChartVM.YSun(chart.SunAltitudeAt(instant)) * sy, palette.Ink(darkMode), h);
            DrawScrubDot(dc, x, NightChartVM.YMoon(chart.MoonAltitudeAt(instant)) * sy, palette.MoonTrack(darkMode), h);

            string readout = chart.ScrubReadout(minutes);
            var s = LabelSize(readout, NoteFs);
            double left = x + 6;
            if (left + s.Width > w) left = x - 6 - s.Width;
            DrawChip(dc, readout, NoteFs, left, 3, ink, w, h);
        }

        /// <summary>Where the scrubbed instant meets a curve; skipped when that body is off the plotted window.</summary>
        private static void DrawScrubDot(DrawingContext dc, double x, double y, Brush brush, double h) {
            if (double.IsNaN(y) || y < 0 || y > h) return;
            dc.DrawEllipse(brush, null, new Point(x, y), 3, 3);
        }

        // ---- Scrub interaction -------------------------------------------------------------

        /// <summary>Sunrise-to-sunset span the chart is drawing, matching OnRender.</summary>
        private double SpanMinutes => NightChartVM.FrameSpanMinutes(ActualWidth);

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
            base.OnMouseLeftButtonDown(e);
            if (row == null) return;
            Focus();
            CaptureMouse();
            ScrubToPixel(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (IsMouseCaptured) ScrubToPixel(e.GetPosition(this).X);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
            base.OnMouseLeftButtonUp(e);
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        /// <summary>Wheel over the chart: one hour at a time, the fastest way to walk a night.</summary>
        protected override void OnMouseWheel(MouseWheelEventArgs e) {
            base.OnMouseWheel(e);
            if (row == null) return;
            StepScrub(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (row == null) return;
            switch (e.Key) {
                case Key.Left: StepScrub(-1); e.Handled = true; break;
                case Key.Right: StepScrub(1); e.Handled = true; break;
                case Key.Home: SetScrubDirect(0); e.Handled = true; break;
                case Key.End: SetScrubDirect(SpanMinutes); e.Handled = true; break;
                case Key.Escape:
                case Key.Delete: ClearScrub(); e.Handled = true; break;
            }
        }

        /// <summary>
        /// Scrub to the whole hour under the pointer. The pointer is deliberately not
        /// snapped to a sample of the curve: the hour is what a night plan is written in.
        /// </summary>
        private void ScrubToPixel(double pixelX) {
            double sx = ActualWidth / (NightChartVM.Width * SpanMinutes / NightChartVM.FullFrameMinutes);
            double minutes = NightChartVM.SnapToHour(NightChartVM.MinutesAtX(pixelX / sx));
            SetScrubDirect(minutes);
        }

        private void SetScrubDirect(double minutes) {
            if (hookedPlanner != null) hookedPlanner.SetScrub(minutes);
            else SetScrub(System.Math.Clamp(minutes, 0, SpanMinutes));
        }

        private void StepScrub(int hours) {
            if (hookedPlanner != null) hookedPlanner.StepScrub(hours);
            else SetScrubDirect((scrubMinutes ?? (row.Payload.Sunset - row.Payload.FrameNoon).TotalMinutes) + hours * 60);
        }

        private void ClearScrub() {
            if (hookedPlanner != null) hookedPlanner.ClearScrub();
            else SetScrub(null);
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
