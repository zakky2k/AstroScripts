using Moq;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using Zakfo.Unphased.Core;
using Zakfo.Unphased.Features.planner;
using Zakfo.Unphased.Features.settings;

namespace Zakfo.Unphased.Tests.Features.planner {
    /// <summary>
    /// Shared stand-ins for the planner view model. Kept out of the individual test
    /// classes because the keyboard tests need the same three-night window the
    /// selection tests already build, and a second copy of the fake sun series would
    /// be a second thing to keep honest.
    /// </summary>
    internal static class PlannerFixture {
        public static DateTimeOffset Local(int year, int month, int day, int hour, int minute) {
            var dt = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
        }

        /// <summary>
        /// One night whose evening is 16:20 of (year, month, day) and whose
        /// sunrise is 07:30 the next morning - enough to fill the labels. The
        /// frame starts at the noon of the evening's own day, exactly as the real
        /// calculator does, so a one-night frame really contains that night.
        /// </summary>
        public static NightPayload Night(int year, int month, int day, int darkMinutes, double illum) {
            var evening = Local(year, month, day, 16, 20);
            var morning = evening.Date.AddDays(1) + new TimeSpan(7, 30, 0);
            var noon = evening.Date + new TimeSpan(12, 0, 0);
            return new NightPayload {
                Location = new Site(50.08804, 14.42076),
                FrameNoon = noon,
                NightStart = evening,
                NightEnd = morning,
                Sunset = evening,
                Sunrise = morning,
                Events = Array.Empty<TwilightEvent>(),
                SunSeries = FakeSunSeries(noon, evening),
                TrueDarkness = true,
                DeepestSunAltitude = -18.5,
                MoonSamples = Array.Empty<MoonSample>(),
                Moon = new MoonEvents(Array.Empty<DateTimeOffset>(), Array.Empty<DateTimeOffset>()),
                Waxing = true,
                Limb = "right",
                MidnightTime = evening.AddHours(8),
                MidnightIllumFraction = illum,
                MidnightMoonAltitude = 35,
                MidnightMoonAzimuth = 180,
                DarkMinutes = darkMinutes,
            };
        }

        /// <summary>
        /// Two days of half-hourly sun samples on a sine that touches zero exactly at
        /// 16:20 and 07:30, so stitched frames cross the horizon where the fixture
        /// claims they do. Real payloads carry a 10-minute grid from the calculator.
        /// </summary>
        public static SunSample[] FakeSunSeries(DateTimeOffset noon, DateTimeOffset sunset) {
            const double nightMinutes = 16 * 60 + 20 - (7 * 60 + 30);   // 16:20 down to 07:30
            var samples = new List<SunSample>();
            for (int m = 0; m <= 2 * 1440; m += 30) {
                var t = noon.AddMinutes(m);
                double phase = ((t - sunset).TotalMinutes % 1440 + 1440) % 1440;
                double altitude = phase <= nightMinutes
                    ? -25 * Math.Sin(Math.PI * phase / nightMinutes)
                    : 25 * Math.Sin(Math.PI * (phase - nightMinutes) / (1440 - nightMinutes));
                samples.Add(new SunSample(t, Math.Round(altitude, 2)));
            }
            return samples.ToArray();
        }

        public static NightPlannerVM Planner() {
            var profiles = new Mock<IProfileService>();
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            profile.SetupGet(p => p.AstrometrySettings).Returns((IAstrometrySettings)null);
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            return new NightPlannerVM(settings);
        }

        /// <summary>
        /// A planner showing three consecutive nights. Rows are made by the view
        /// model (ShowWindow -> RebuildRows), so zooming later has the raw payloads
        /// to stitch from, exactly as after a real compute.
        /// </summary>
        public static NightPlannerVM WindowOfThreeNights() {
            var planner = Planner();
            planner.ShowWindow(new[] {
                Night(2026, 11, 24, 691, 0.99),
                Night(2026, 11, 25, 692, 0.97),
                Night(2026, 11, 26, 694, 0.92),
            });
            // Start from a date outside the window so every test navigation is a
            // real change no matter when the suite runs.
            planner.SelectedDate = new DateTime(2026, 1, 1);
            return planner;
        }
    }
}
