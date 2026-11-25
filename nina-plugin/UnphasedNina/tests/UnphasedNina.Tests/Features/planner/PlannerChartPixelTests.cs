using Moq;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zakfo.Unphased.Core;
using Zakfo.Unphased.Features.planner;
using Zakfo.Unphased.Features.settings;

namespace Zakfo.Unphased.Tests.Features.planner {
    /// <summary>
    /// What the renderer paints, checked on pixels rather than on the polyline string.
    /// The string cuts a track where it drops below the floor of the window, but the
    /// control used to join those runs into one figure, and the pen then drew a straight
    /// line along the bottom edge for every hour the sun was under it - which read as a
    /// feature of the sky rather than as a clipped curve.
    /// </summary>
    [NonParallelizable]
    public class PlannerChartPixelTests {
        // Chosen as a picked colour, which is how a real user gets a distinctive sun
        // track: magenta is not used by the horizon (green), the moon (cream) or the
        // accents (orange), and it needs no theme resources at all.
        private const string SunPick = "#FF00FF";

        [Test]
        public void ATrackThatDropsBelowTheFloorStopsThereInsteadOfRunningAlongIt() {
            int longest = 0;
            var worker = new Thread(() => longest = LongestSunRunOnTheBottomEdge());
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            worker.Join();

            // The sun sits below -24 degrees for roughly a quarter of this frame, so a
            // bridged gap is a couple of hundred pixels wide. Where the track genuinely
            // crosses the floor it covers a handful of pixels at most.
            Assert.That(longest, Is.LessThan(100),
                "a flat run of sun colour along the bottom edge is the cut being bridged");
        }

        /// <summary>Widest run of sun-coloured pixels found on the bottom rows of the rendered chart.</summary>
        private static int LongestSunRunOnTheBottomEdge() {
            var profiles = new Mock<IProfileService>();
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            profile.SetupGet(p => p.AstrometrySettings).Returns((IAstrometrySettings)null);
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            // A picked colour, so the sun track is a colour nothing else on the chart wears.
            var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            settings.SunColor = SunPick;
            try {
                return Measure(settings);
            } finally {
                settings.ResetColors();
                settings.Dispose();
            }
        }

        private static int Measure(PluginSettings settings) {
            const int width = 600, height = 320;
            var calc = new NightCalculator(new Site(50.08804, 14.42076));
            // Prague, 24 November: the sun sinks to -60 degrees, far below the -24 floor
            // of the chart, so its track leaves the window for hours and comes back.
            var noon = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 24, 12, 0, 0), TimeZoneInfo.Local);
            var payload = calc.Compute(new DateTimeOffset(noon, TimeSpan.Zero));
            var frame = NightFrame.For(new[] { payload }, 1);

            var planner = new NightPlannerVM(settings);
            var ctl = new NightChartControl();
            ctl.DataContext = planner;   // this is how the control takes its picked colours
            ctl.SetRow(new NightRowVM(frame, NightChartVM.FrameMinutesAtZoom(1)));
            ctl.Measure(new Size(width, height));
            ctl.Arrange(new Rect(0, 0, width, height));
            ctl.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(ctl);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            rtb.CopyPixels(pixels, stride, 0);

            int longest = 0;
            for (int y = height - 4; y < height; y++) {
                int run = 0;
                for (int x = 0; x < width; x++) {
                    int i = y * stride + x * 4;
                    // Pbgra32 straight out of the render target, so the order is B, G, R.
                    bool sun = pixels[i] > 0xB0 && pixels[i + 1] < 0x50 && pixels[i + 2] > 0xB0;
                    run = sun ? run + 1 : 0;
                    if (run > longest) longest = run;
                }
            }
            return longest;
        }
    }
}
