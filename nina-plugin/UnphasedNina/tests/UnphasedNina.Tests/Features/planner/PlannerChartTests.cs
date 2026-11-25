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
