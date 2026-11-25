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
        public void RenderZoomsAndScrubPreviews() {
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
                // Nov 14..24: the same span the panel prefetches, so a 7-night zoom
                // out of the 18th really does have the following nights to stitch.
                var nights = new System.Collections.Generic.List<NightPayload>();
                for (int day = 14; day <= 24; day++) {
                    var noon = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, 12, 0, 0), tz);
                    nights.Add(calc.Compute(new DateTimeOffset(noon, TimeSpan.Zero)));
                }
                NightPayload Night(int day) => nights[day - 14];

                // (first night's evening day, nights in the frame, panel width,
                //  scrub hours after local noon, panel height). One night first, then
                // the zoom ladder, then a deliberately narrow panel at the widest zoom,
                // then scrubs: one at the moon's high hour, one inside a zoomed frame.
                foreach (var (day, zoom, width, scrub, height) in new[] {
                        (24, 1, 1200, -1, 360),
                        (24, 1, 720, -1, 360),
                        (22, 2, 1200, -1, 360),
                        (22, 3, 1200, -1, 360),
                        (18, 7, 1200, -1, 360),
                        (18, 7, 620, -1, 260),
                        (24, 1, 1200, 13, 360),
                        // 13:00 on the 14th: the moon is up, a quarter lit, and well to
                        // the left of its peak, so the phase riding on the scrub marker
                        // is unmistakable rather than hidden behind the peak glyph.
                        (14, 1, 1200, 1, 360),
                        (22, 3, 1200, 36, 360),
                    }) {
                    var slice = new System.Collections.Generic.List<NightPayload>();
                    for (int d = day; d < day + zoom && d <= 24; d++) slice.Add(Night(d));
                    paths.Add(Render(slice, width, scrub, height));
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            worker.Join();

            Assert.That(paths, Has.Count.EqualTo(9));
            foreach (var path in paths) Assert.That(new System.IO.FileInfo(path).Length, Is.GreaterThan(2000));
            TestContext.Progress.WriteLine(string.Join(System.Environment.NewLine, paths));
        }

        private static string Render(System.Collections.Generic.IReadOnlyList<NightPayload> slice,
            int width, int scrubHoursAfterNoon, int height) {
            var frame = NightFrame.For(slice, slice.Count);
            var first = slice[0];

            string suffix = scrubHoursAfterNoon < 0 ? "" : $"-scrub{(12 + scrubHoursAfterNoon) % 24:00}h";
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"planner-preview-{first.FrameNoon.LocalDateTime:dd}-z{slice.Count}-at-{width}{suffix}.png");
            var ctl = new NightChartControl();
            ctl.SetRow(new NightRowVM(frame, NightChartVM.FrameMinutesAtZoom(slice.Count)));
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
