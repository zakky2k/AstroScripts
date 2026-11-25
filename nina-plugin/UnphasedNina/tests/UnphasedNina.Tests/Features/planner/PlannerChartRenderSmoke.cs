using NUnit.Framework;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zakfo.Unphased.Core;
using Zakfo.Unphased.Features.planner;

namespace Zakfo.Unphased.Tests.Features.planner {
    /// <summary>
    /// Visual smoke test, not part of the automated run: renders the chart to PNG
    /// files in %TEMP% so a layout change can be eyeballed without starting
    /// N.I.N.A. Run it explicitly after touching NightChartControl.
    /// </summary>
    [Explicit("Writes PNGs to %TEMP%; run by hand to eyeball chart layout.")]
    [NonParallelizable]
    public class PlannerChartRenderSmoke {
        [Test]
        public void RenderWideAndNarrowPreviews() {
            var paths = new System.Collections.Generic.List<string>();
            var worker = new System.Threading.Thread(() => {
                // Stand-in theme: inside N.I.N.A. these brushes come from its own
                // theme dictionaries, and without them the palette falls back to
                // flat grey, which hides the very thing being checked.
                var app = new Application();
                app.Resources["TertiaryBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x0E, 0x16, 0x22));
                app.Resources["PrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x6F, 0xC8, 0xE8));
                app.Resources["SecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xE8, 0xC8));
                app.Resources["BorderBrush"] = new SolidColorBrush(Color.FromArgb(0x80, 0x5A, 0x6E, 0x8C));
                app.Resources["ButtonBackgroundSelectedBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3C));

                var calc = new NightCalculator(new Site(50.08804, 14.42076));
                var tz = TimeZoneInfo.Local;
                // One of the four is rendered scrubbed (here at 20:00 local), so the
                // scrub line, its dots and its readout can be eyeballed too.
                foreach (var (day, width, scrub) in new[] { (24, 1200, -1), (24, 720, -1), (17, 1200, -1), (24, 1200, 8) }) {
                    var noon = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, 12, 0, 0), tz);
                    paths.Add(Render(calc.Compute(new DateTimeOffset(noon, TimeSpan.Zero)), width, scrub));
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            worker.Join();

            Assert.That(paths, Has.Count.EqualTo(4));
            foreach (var path in paths) Assert.That(new System.IO.FileInfo(path).Length, Is.GreaterThan(2000));
        }

        private static string Render(NightPayload p, int width, int scrubHoursAfterNoon) {
            int height = 360;
            string suffix = scrubHoursAfterNoon < 0 ? "" : $"-scrub{(12 + scrubHoursAfterNoon) % 24:00}h";
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"planner-preview-{p.FrameNoon.LocalDateTime:dd}-at-{width}{suffix}.png");
            var ctl = new NightChartControl();
            ctl.SetRow(new NightRowVM(p));
            if (scrubHoursAfterNoon >= 0) ctl.SetScrub(scrubHoursAfterNoon * 60.0);
            ctl.Measure(new Size(width, height));
            ctl.Arrange(new Rect(0, 0, width, height));
            ctl.UpdateLayout();
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(ctl);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
            return path;
        }
    }
}
