using Moq;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using Zakfo.Unphased.Core;
using Zakfo.Unphased.Features.planner;
using Zakfo.Unphased.Features.settings;

namespace Zakfo.Unphased.Tests.Features.planner {
    /// <summary>
    /// The highlighted row, the header caption and the chart must always describe
    /// the same night. Selection is owned by NightPlannerVM, so these tests cover
    /// it without touching WPF.
    /// </summary>
    [NonParallelizable]
    public class PlannerSelectionTests {
        private static DateTimeOffset Local(int year, int month, int day, int hour, int minute) {
            var dt = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
        }

        /// <summary>
        /// One night whose evening is 16:20 of (year, month, day) and whose
        /// sunrise is 07:30 the next morning - enough to fill the labels.
        /// </summary>
        private static NightRowVM Night(int year, int month, int day, int darkMinutes, double illum) {
            var evening = Local(year, month, day, 16, 20);
            var morning = evening.AddDays(1);
            var payload = new NightPayload {
                Location = new Site(50.08804, 14.42076),
                FrameNoon = evening.AddDays(-1).Date + new TimeSpan(12, 0, 0),
                NightStart = evening,
                NightEnd = morning,
                Sunset = evening,
                Sunrise = morning,
                Events = Array.Empty<TwilightEvent>(),
                SunSeries = Array.Empty<SunSample>(),
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
            return new NightRowVM(payload);
        }

        private static NightPlannerVM Planner() {
            var profiles = new Mock<IProfileService>();
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            profile.SetupGet(p => p.AstrometrySettings).Returns((IAstrometrySettings)null);
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            return new NightPlannerVM(settings);
        }

        private static NightPlannerVM WindowOfThreeNights() {
            var planner = Planner();
            planner.Nights.Add(Night(2026, 11, 24, 691, 0.99));
            planner.Nights.Add(Night(2026, 11, 25, 692, 0.97));
            planner.Nights.Add(Night(2026, 11, 26, 694, 0.92));
            // Start from a date outside the window so every test navigation is a
            // real change no matter when the suite runs.
            planner.SelectedDate = new DateTime(2026, 1, 1);
            return planner;
        }

        [Test]
        public void NavigatingToANightSelectsTheRowNamedByItsMorning() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);

            Assert.That(planner.SelectedNight, Is.Not.Null);
            Assert.That(planner.SelectedNight.MorningDay, Is.EqualTo(new DateTime(2026, 11, 26)));
            // The row the user sees highlighted reads 25 Nov (its evening); the
            // header quotes the same night explicitly, so there is no off-by-one.
            Assert.That(planner.SelectedDateLabel, Is.EqualTo(planner.SelectedNight.NightSpanLabel));
            Assert.That(planner.SelectedDateLabel, Does.Contain("Wed 25 Nov"));
            Assert.That(planner.SelectedDateLabel, Does.Contain("Thu 26 Nov 2026"));
        }

        [Test]
        public void ClickingARowMovesTheAnchorDateToThatNight() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);

            planner.SelectedNight = planner.Nights[0];

            Assert.That(planner.SelectedDate.Date, Is.EqualTo(new DateTime(2026, 11, 25)));
            Assert.That(planner.SelectedDateLabel, Is.EqualTo(planner.Nights[0].NightSpanLabel));
        }

        [Test]
        public void SelectionFallsBackToTheMiddleWhenTheNightIsOutsideTheWindow() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 12, 30, 0, 0);

            Assert.That(planner.SelectedNight, Is.SameAs(planner.Nights[1]));
        }

        [Test]
        public void SelectingANightRaisesSelectedNightNotification() {
            var planner = WindowOfThreeNights();
            var seen = new List<string>();
            planner.PropertyChanged += (_, args) => seen.Add(args.PropertyName);

            planner.SelectedDate = Local(2026, 11, 25, 0, 0);

            Assert.That(seen, Does.Contain(nameof(NightPlannerVM.SelectedNight)));
            Assert.That(seen, Does.Contain(nameof(NightPlannerVM.SelectedDateLabel)));
        }

        [Test]
        public void EmptyWindowClearsTheSelectionAndKeepsADateCaption() {
            var planner = Planner();
            planner.SelectedDate = Local(2026, 11, 25, 0, 0);

            Assert.That(planner.SelectedNight, Is.Null);
            Assert.That(planner.SelectedDateLabel, Does.Contain("25 Nov 2026"));
        }

        // ---- Scrub marker -----------------------------------------------------------------

        [Test]
        public void ScrubbingSnapsToWholeHoursAndStepsOneHourAtATime() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);

            planner.SetScrub(613);
            Assert.That(planner.ScrubMinutes, Is.EqualTo(600), "the marker must stop on a whole hour");

            planner.StepScrub(1);
            Assert.That(planner.ScrubMinutes, Is.EqualTo(660));
            planner.StepScrub(-3);
            Assert.That(planner.ScrubMinutes, Is.EqualTo(480));
        }

        [Test]
        public void FirstScrubStepStartsAfterSunsetRatherThanAtTheFrameStart() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);
            var p = planner.SelectedNight.Payload;
            double sunset = (p.Sunset - p.FrameNoon).TotalMinutes;

            planner.StepScrub(1);

            Assert.That(planner.ScrubMinutes, Is.EqualTo(Math.Round(sunset / 60, MidpointRounding.AwayFromZero) * 60 + 60));
        }

        [Test]
        public void SteppingCannotWalkTheMarkerOffTheVisibleSpan() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);
            planner.SetVisibleSpan(NightChartVM.OneNightMinutes);

            planner.SetScrub(47 * 60);
            Assert.That(planner.ScrubMinutes, Is.EqualTo(NightChartVM.OneNightMinutes), "clamped to the right edge");

            planner.SetScrub(-2 * 60);
            Assert.That(planner.ScrubMinutes, Is.EqualTo(0), "clamped to the frame start");

            planner.SetVisibleSpan(NightChartVM.FullFrameMinutes);
            Assert.That(planner.VisibleSpanMinutes, Is.EqualTo(NightChartVM.FullFrameMinutes));
            Assert.That(planner.ScrubMinutes, Is.EqualTo(0));
        }

        [Test]
        public void TurningTheScrubOffClearsItAndSaysSoInHeader() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);
            planner.SetScrub(20 * 60);
            Assert.That(planner.ScrubLabel, Does.StartWith("Scrub "));

            planner.ClearScrub();

            Assert.Multiple(() => {
                Assert.That(planner.ScrubMinutes, Is.Null);
                Assert.That(planner.ScrubLabel, Does.Contain("Scrub off"));
            });
        }

        [Test]
        public void ScrubPositionSurvivesANewNightAndItsReadoutFollows() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);
            planner.SetScrub(20 * 60);
            var seen = new List<string>();
            planner.PropertyChanged += (_, args) => seen.Add(args.PropertyName);

            planner.SelectedDate = Local(2026, 11, 25, 0, 0);

            // +20 h from noon is the same clock hour in every frame, which is what
            // makes two nights comparable - so the marker stays put and only its
            // quoted altitudes change.
            Assert.Multiple(() => {
                Assert.That(planner.ScrubMinutes, Is.EqualTo(20 * 60));
                Assert.That(seen, Does.Contain(nameof(NightPlannerVM.ScrubLabel)));
            });
        }
    }
}
