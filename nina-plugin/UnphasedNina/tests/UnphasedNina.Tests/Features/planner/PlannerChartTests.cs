using NUnit.Framework;
using System.Windows.Media;
using Zakfo.Unphased.Core;
using Zakfo.Unphased.Features.planner;

namespace Zakfo.Unphased.Tests.Features.planner {
    /// <summary>
    /// Chart geometry sanity for a real Prague night: the sun curve, the moon
    /// track and the twilight bands must all have drawable content inside the
    /// noon-to-noon+2 frame.
    /// </summary>
    [NonParallelizable]
    public class PlannerChartTests {
        private static NightPayload PragueNight(int year, int month, int day) {
            var calc = new NightCalculator(new Site(50.08804, 14.42076));
            var tz = TimeZoneInfo.Local;
            // Night convention: the frame is anchored at local noon of the day
            // the night starts on, so this is the night of (day) evening -> (day+1) morning.
            var noonUtc = TimeZoneInfo.ConvertTimeToUtc(
                new DateTime(year, month, day, 12, 0, 0), tz);
            return calc.Compute(new DateTimeOffset(noonUtc, TimeSpan.Zero));
        }

        /// <summary>
        /// The points a path would draw, in order, run separators (" | ") ignored. A
        /// curve that leaves the altitude window is several runs, so counting tokens is
        /// not enough to describe what the chart shows.
        /// </summary>
        private static (double X, double Y)[] Points(string path) {
            if (string.IsNullOrEmpty(path)) return Array.Empty<(double, double)>();
            return path.Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Split(','))
                .Select(xy => (
                    X: double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture),
                    Y: double.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture)))
                .ToArray();
        }

        [Test]
        public void PragueNightHasSunAndMoonContentInsideTheFrame() {
            var p = PragueNight(2026, 11, 24);
            var row = new NightRowVM(p);

            var inWindow = p.MoonSamples.Count(m => m.Time >= p.Sunset && m.Time <= p.Sunrise);
            Console.WriteLine($"frame noon {p.FrameNoon} | sunset {p.Sunset} | sunrise {p.Sunrise}");
            Console.WriteLine($"sun samples {p.SunSeries.Count} | moon samples {p.MoonSamples.Count} | in night window {inWindow}");
            Console.WriteLine($"sun polyline pts {Points(row.Chart.SunPolyline).Length} | moon polyline pts {Points(row.Chart.MoonPolyline).Length}");
            Console.WriteLine($"moon peak alt {p.MoonSamples.Where(m => m.Time >= p.Sunset && m.Time <= p.Sunrise).Select(m => m.Altitude).DefaultIfEmpty(double.NaN).Max():F2} illum {p.MidnightIllumFraction:F3}");
            Console.WriteLine($"sun alt range {p.SunSeries.Min(s => s.Altitude):F2} .. {p.SunSeries.Max(s => s.Altitude):F2}");
            Console.WriteLine($"moonrise {string.Join(", ", p.Moon.Rise)} moonset {string.Join(", ", p.Moon.Set)}");

            Assert.That(row.Chart.SunPolyline, Is.Not.Empty);
            Assert.That(inWindow, Is.GreaterThan(1), "moon track has no samples inside the night window");
            Assert.That(row.Chart.MoonPolyline, Is.Not.Empty, "moon polyline is empty: the chart cannot draw a moon track");
        }

        [Test]
        public void SunCurveStaysInsideTheFrameHorizontally() {
            var p = PragueNight(2026, 11, 24);
            var xs = p.SunSeries.Select(p2 => NightChartVM.XAt(p, p2.Time)).ToArray();
            Assert.That(xs.Min(), Is.GreaterThanOrEqualTo(0).And.LessThan(NightChartVM.Width * 0.05));
            Assert.That(xs.Max(), Is.LessThanOrEqualTo(NightChartVM.Width).And.GreaterThan(NightChartVM.Width * 0.95));
        }

        [Test]
        public void MoonTrackSpansTheWholeFrameLikeTheSun() {
            var p = PragueNight(2026, 11, 24);
            var row = new NightRowVM(p);

            // The track is no longer clipped to sunset..sunrise: it runs the full
            // frame, so the drawn points must reach both edges of the canvas.
            var drawn = Points(row.Chart.MoonPolyline).Select(q => q.X).ToArray();

            Assert.Multiple(() => {
                Assert.That(drawn, Has.Length.EqualTo(p.MoonSamples.Count), "moon samples are being dropped");
                Assert.That(drawn.Min(), Is.LessThan(NightChartVM.Width * 0.05), "moon track does not start at the frame start");
                Assert.That(drawn.Max(), Is.GreaterThan(NightChartVM.Width * 0.95), "moon track stops before the frame end");
            });
        }

        [Test]
        public void TwilightShadingIsOneRunPerBandStretchNotOnePerSample() {
            var p = PragueNight(2026, 11, 24);
            var runs = NightChartVM.ShadingRuns(p);

            // One hairline-width rectangle per 10-minute sample reads as a comb of
            // vertical lines, so consecutive samples of the same band must merge.
            Assert.Multiple(() => {
                Assert.That(runs.Count, Is.LessThan(p.SunSeries.Count / 4), "shading is still sliced per sample");
                Assert.That(runs[0].X0, Is.EqualTo(0).Within(0.01));
                Assert.That(runs[runs.Count - 1].X1, Is.EqualTo(NightChartVM.Width).Within(0.6));
                Assert.That(runs.Select(r => r.Band).Distinct(),
                    Is.EquivalentTo(new[] { -1, 0, 1, 2, 3 }), "a Prague night must pass through every band");
            });
            for (int i = 1; i < runs.Count; i++) {
                Assert.That(runs[i].X0, Is.EqualTo(runs[i - 1].X1).Within(0.01), $"run {i} leaves a gap");
                Assert.That(runs[i].Band, Is.Not.EqualTo(runs[i - 1].Band), $"run {i} should have merged with {i - 1}");
                Assert.That(runs[i].X1, Is.GreaterThan(runs[i].X0), $"run {i} is empty");
            }
        }

        [Test]
        public void TwilightBandBoundariesMatchTheStandardDepths() {
            Assert.Multiple(() => {
                Assert.That(PlannerPalette.TwilightBand(5), Is.EqualTo(0), "sun up");
                Assert.That(PlannerPalette.TwilightBand(0), Is.EqualTo(1), "sun on the horizon is civil twilight");
                Assert.That(PlannerPalette.TwilightBand(-6), Is.EqualTo(2), "nautical");
                Assert.That(PlannerPalette.TwilightBand(-12), Is.EqualTo(3), "astronomical");
                Assert.That(PlannerPalette.TwilightBand(-18), Is.EqualTo(-1), "at -18 the sky is fully dark");
                Assert.That(PlannerPalette.TwilightBand(-40), Is.EqualTo(-1));
            });
        }

        [Test]
        public void AltitudeWindowShowsTheWholeSky() {
            double gap = NightChartVM.YSun(-6) - NightChartVM.YSun(0);
            Assert.Multiple(() => {
                Assert.That(NightChartVM.YSun(NightChartVM.TopAltitude), Is.EqualTo(0), "top edge of the window");
                Assert.That(NightChartVM.YSun(NightChartVM.BottomAltitude), Is.EqualTo(NightChartVM.Height), "bottom edge");
                // The full +-90 domain is a user requirement, not a sizing choice: a
                // narrower window cut high winter moons off and pressed the deep half
                // of the night against the bottom edge. Legibility is handled by the
                // degree chips and their de-stacking walk instead.
                Assert.That(NightChartVM.TopAltitude, Is.EqualTo(90), "the zenith must be on the chart");
                Assert.That(NightChartVM.BottomAltitude, Is.EqualTo(-90), "the nadir must be on the chart");
                Assert.That(NightChartVM.YSun(0), Is.EqualTo(NightChartVM.Height / 2), "horizon at mid-height");
                Assert.That(gap, Is.GreaterThan(0), "twilight lines must be in order top to bottom");
            });
        }

        [Test]
        public void CurvesKeepEverySampleWhileNothingLeavesTheWindow() {
            var p = PragueNight(2026, 11, 24);
            var chart = new NightChartVM(p);
            Assert.Multiple(() => {
                Assert.That(Points(chart.SunPolyline), Has.Length.EqualTo(p.SunSeries.Count), "sun samples dropped");
                Assert.That(Points(chart.MoonPolyline), Has.Length.EqualTo(p.MoonSamples.Count), "moon samples dropped");
                Assert.That(chart.SunPolyline, Does.Not.Contain("|"), "nothing leaves the +-90 window, so it is one run");
                Assert.That(chart.MoonPolyline, Does.Not.Contain("|"));
            });
        }

        [Test]
        public void CurvesStopAtTheWindowEdgeInsteadOfRunningAlongIt() {
            var p = PragueNight(2026, 11, 24);
            // A deliberately tight window (only +-20 degrees visible) so the real moon
            // track certainly leaves it, to prove the curve is cut rather than clamped:
            // clamping every sample to the edge is what drew a flat horizontal line
            // along the bottom of the panel.
            var path = NightChartVM.TrimmedPolyline(
                p,
                p.MoonSamples.Select(m => (m.Time, m.Altitude)).ToArray(),
                alt => (20 - alt) / 40 * NightChartVM.Height);
            Assert.Multiple(() => {
                Assert.That(path, Does.Contain("|"), "the track leaves this window, so it must break into runs");
                Assert.That(Points(path).Select(q => q.Y), Is.All.InRange(0.0, NightChartVM.Height), "a point lies off the canvas");
            });
            foreach (var run in path.Split('|')) {
                var q = Points(run);
                // Only a run's endpoints may touch an edge: an interior point pinned to
                // the edge is the flattened line this replaced.
                for (int i = 1; i < q.Length - 1; i++)
                    Assert.That(q[i].Y, Is.GreaterThan(0.01).And.LessThan(NightChartVM.Height - 0.01),
                        $"point {i} of a run sits on the window edge: the curve is flattened along it");
                for (int i = 1; i < q.Length; i++)
                    Assert.That(q[i].X, Is.GreaterThan(q[i - 1].X), "a run doubles back in time");
            }
        }

        [Test]
        public void MarkerChipsFlipToTheOtherSideOfTheLineRatherThanLeavingTheChart() {
            const double horizon = 160, label = 18, canvas = 260;
            Assert.Multiple(() => {
                // Sunset and sunrise hang under the horizon when the panel has room.
                Assert.That(NightChartVM.ChipBelow(horizon, label, canvas), Is.EqualTo(horizon + 4));
                Assert.That(NightChartVM.ChipBelow(horizon, label, canvas) + label, Is.LessThanOrEqualTo(canvas));
                // Too short a panel flips them above the line instead of off the chart.
                Assert.That(NightChartVM.ChipBelow(horizon, label, 180), Is.EqualTo(horizon - 4 - label));
                // Moonrise/moonset chips are the mirror image, above the line.
                Assert.That(NightChartVM.ChipAbove(horizon, label, canvas), Is.EqualTo(horizon - 4 - label));
                Assert.That(NightChartVM.ChipAbove(20, label, canvas), Is.EqualTo(24));
            });
        }

        [Test]
        public void ScrubbingLandsOnWholeHoursAndReportsWhereTheBodiesAre() {
            var p = PragueNight(2026, 11, 24);
            var chart = new NightChartVM(p);
            double asked = (p.Sunset - p.FrameNoon).TotalMinutes + 37;
            double snapped = NightChartVM.SnapToHour(asked);
            var instant = chart.TimeAtMinutes(snapped);
            var text = chart.ScrubReadout(snapped);

            Assert.Multiple(() => {
                Assert.That(snapped % 60, Is.EqualTo(0), "the marker only stops on whole hours");
                Assert.That(NightChartVM.SnapToHour(150), Is.EqualTo(180), "half hours round away from zero");
                Assert.That(NightChartVM.MinutesAtX(NightChartVM.XAtMinutes(500)), Is.EqualTo(500).Within(1e-9), "pointer mapping is not an inverse");
                Assert.That(text, Does.StartWith(instant.LocalDateTime.ToString("HH:mm")), "readout does not name the hour it points at");
                Assert.That(text, Does.Contain("sun"));
                Assert.That(text, Does.Contain("moon"));
                // Altitudes are interpolated at that instant, not snapped to a sample.
                Assert.That(chart.SunAltitudeAt(instant), Is.Not.NaN);
                Assert.That(chart.SunAltitudeAt(instant), Is.EqualTo(NightChartVM.AltitudeAt(
                    p.SunSeries.Select(s => (s.Time, s.Altitude)).ToArray(), instant)).Within(1e-9));
                Assert.That(chart.MoonAltitudeAt(p.FrameNoon.AddHours(-1)), Is.NaN, "outside the frame has no altitude");
            });
        }

        [Test]
        public void HorizonAndChipBackingAreDistinctFromTheFaintGrid() {
            var palette = new PlannerPalette();
            var horizon = ((SolidColorBrush)palette.Horizon(true)).Color;
            var grid = ((SolidColorBrush)palette.Gridline(true)).Color;
            var chip = ((SolidColorBrush)palette.ChipFill(false)).Color;

            Assert.Multiple(() => {
                Assert.That(horizon, Is.Not.EqualTo(grid), "the horizon must not read as just another gridline");
                Assert.That(chip.A, Is.GreaterThanOrEqualTo((byte)0xC0), "chip backing has to block what is behind it");
                Assert.That(chip.R + chip.G + chip.B, Is.LessThan(0x60), "chip backing has to read as black");
            });
        }

        [Test]
        public void ChartShowsTwoNightsOnlyWhenThereIsRoomForBoth() {
            Assert.Multiple(() => {
                Assert.That(NightChartVM.FrameSpanMinutes(2100), Is.EqualTo(NightChartVM.FullFrameMinutes), "wide panel keeps both nights");
                Assert.That(NightChartVM.FrameSpanMinutes(NightChartVM.TwoNightMinimumWidth), Is.EqualTo(NightChartVM.FullFrameMinutes));
                Assert.That(NightChartVM.FrameSpanMinutes(NightChartVM.TwoNightMinimumWidth - 1), Is.EqualTo(NightChartVM.OneNightMinutes), "narrow panel falls back to one night");
                Assert.That(NightChartVM.FrameSpanMinutes(600), Is.EqualTo(NightChartVM.OneNightMinutes));
            });
        }

        [Test]
        public void MoonGlyphSitsOnTheHighestMoonPointOfThatNight() {
            var p = PragueNight(2026, 11, 24);
            var peak = new NightChartVM(p).NightMoonPeak();
            var bestTonight = p.MoonSamples
                .Where(m => m.Time >= p.Sunset && m.Time <= p.Sunrise)
                .Max(m => m.Altitude);

            Assert.Multiple(() => {
                Assert.That(peak.Time, Is.InRange(p.Sunset, p.Sunrise), "glyph placed outside the night");
                Assert.That(peak.Altitude, Is.EqualTo(bestTonight).Within(1e-9));
            });
        }

        [Test]
        public void MoonGlyphShapeMatchesTheIlluminatedFraction() {
            // r = 10 disc centred on the origin, lit on the right (waxing, northern view).
            Geometry Waxing(double f) => NightChartControl.MoonPhaseGeometry(0, 0, 10, f, true);
            Geometry Waning(double f) => NightChartControl.MoonPhaseGeometry(0, 0, 10, f, false);

            Assert.Multiple(() => {
                Assert.That(Waxing(0.999).FillContains(new System.Windows.Point(9, 0)), Is.True, "full moon covers the disc");
                Assert.That(Waxing(0.999).FillContains(new System.Windows.Point(-9, 0)), Is.True);

                // Crescent: only a sliver by the right limb is lit.
                Assert.That(Waxing(0.25).FillContains(new System.Windows.Point(8, 0)), Is.True);
                Assert.That(Waxing(0.25).FillContains(new System.Windows.Point(2, 0)), Is.False, "terminator cuts the crescent at x=+5");
                Assert.That(Waxing(0.25).FillContains(new System.Windows.Point(-8, 0)), Is.False);

                // Gibbous: the terminator bulges into the dark side.
                Assert.That(Waxing(0.75).FillContains(new System.Windows.Point(-3, 0)), Is.True);
                Assert.That(Waxing(0.75).FillContains(new System.Windows.Point(-8, 0)), Is.False);

                // Quarter: straight terminator through the centre.
                Assert.That(Waxing(0.5).FillContains(new System.Windows.Point(1, 0)), Is.True);
                Assert.That(Waxing(0.5).FillContains(new System.Windows.Point(-1, 0)), Is.False);

                // Waning mirrors the lit side.
                Assert.That(Waning(0.25).FillContains(new System.Windows.Point(-8, 0)), Is.True);
                Assert.That(Waning(0.25).FillContains(new System.Windows.Point(8, 0)), Is.False);
            });
        }

        [Test]
        public void TwilightPaletteFollowsTheStandardAltitudeBands() {
            var palette = new PlannerPalette();
            foreach (var dark in new[] { false, true }) {
                Assert.Multiple(() => {
                    Assert.That(palette.Twilight(dark, 12), Is.Not.Null, "daylight");
                    Assert.That(palette.Twilight(dark, 0), Is.Not.Null, "civil just below the horizon");
                    Assert.That(palette.Twilight(dark, -6), Is.Not.Null, "nautical");
                    Assert.That(palette.Twilight(dark, -12), Is.Not.Null, "astronomical");
                    Assert.That(palette.Twilight(dark, -18), Is.Null, "below -18 is full night, no overlay");
                    Assert.That(palette.Twilight(dark, -60), Is.Null);
                });
                // Each band must be a distinct colour, otherwise the transition is invisible.
                var brushes = new[] { 12.0, -3.0, -9.0, -15.0 }
                    .Select(a => ((System.Windows.Media.SolidColorBrush)palette.Twilight(dark, a)).Color)
                    .ToArray();
                Assert.That(brushes.Distinct().Count(), Is.EqualTo(4), "twilight bands are not distinguishable");
            }
        }
    }
}
