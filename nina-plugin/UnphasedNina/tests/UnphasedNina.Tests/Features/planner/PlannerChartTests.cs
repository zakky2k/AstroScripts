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

        [Test]
        public void PragueNightHasSunAndMoonContentInsideTheFrame() {
            var p = PragueNight(2026, 11, 24);
            var row = new NightRowVM(p);

            var inWindow = p.MoonSamples.Count(m => m.Time >= p.Sunset && m.Time <= p.Sunrise);
            Console.WriteLine($"frame noon {p.FrameNoon} | sunset {p.Sunset} | sunrise {p.Sunrise}");
            Console.WriteLine($"sun samples {p.SunSeries.Count} | moon samples {p.MoonSamples.Count} | in night window {inWindow}");
            Console.WriteLine($"sun polyline pts {row.Chart.SunPolyline.Split(' ').Length} | moon polyline pts {(row.Chart.MoonPolyline.Length == 0 ? 0 : row.Chart.MoonPolyline.Split(' ').Length)}");
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
            var drawn = row.Chart.MoonPolyline
                .Split(' ')
                .Select(s => double.Parse(s.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

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
        public void AltitudeWindowLeavesRoomToLabelEveryTwilightLine() {
            double gap = NightChartVM.YSun(-6) - NightChartVM.YSun(0);
            Assert.Multiple(() => {
                Assert.That(NightChartVM.YSun(NightChartVM.TopAltitude), Is.EqualTo(0), "top edge of the window");
                Assert.That(NightChartVM.YSun(NightChartVM.BottomAltitude), Is.EqualTo(NightChartVM.Height), "bottom edge");
                Assert.That(NightChartVM.TopAltitude, Is.GreaterThanOrEqualTo(70), "a high winter moon must stay visible");
                Assert.That(NightChartVM.BottomAltitude, Is.LessThanOrEqualTo(-24), "full night needs its own band");
                Assert.That(gap, Is.GreaterThanOrEqualTo(16), "each line needs its own degree label");
                Assert.That(NightChartVM.YSun(0), Is.LessThan(NightChartVM.Height * 0.8), "horizon not pushed off the chart");
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
