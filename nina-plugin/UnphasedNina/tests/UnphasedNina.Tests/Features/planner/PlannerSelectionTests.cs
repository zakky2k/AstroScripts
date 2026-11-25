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
        /// sunrise is 07:30 the next morning - enough to fill the labels. The
        /// frame starts at the noon of the evening's own day, exactly as the real
        /// calculator does, so a one-night frame really contains that night.
        /// </summary>
        private static NightPayload Night(int year, int month, int day, int darkMinutes, double illum) {
            var evening = Local(year, month, day, 16, 20);
            var morning = evening.Date.AddDays(1) + new TimeSpan(7, 30, 0);
            var noon = evening.Date + new TimeSpan(12, 0, 0);
            var payload = new NightPayload {
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
            return payload;
        }

        /// <summary>
        /// Two days of half-hourly sun samples on a sine that touches zero exactly at
        /// 16:20 and 07:30, so stitched frames cross the horizon where the fixture
        /// claims they do. Real payloads carry a 10-minute grid from the calculator.
        /// </summary>
        private static SunSample[] FakeSunSeries(DateTimeOffset noon, DateTimeOffset sunset) {
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

        /// <summary>
        /// A planner showing three consecutive nights. Rows are made by the view
        /// model (ShowWindow -> RebuildRows), so zooming later has the raw payloads
        /// to stitch from, exactly as after a real compute.
        /// </summary>
        private static NightPlannerVM WindowOfThreeNights() {
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
            planner.SetZoom(1);

            planner.SetScrub(47 * 60);
            Assert.That(planner.ScrubMinutes, Is.EqualTo(NightChartVM.OneNightMinutes), "clamped to the right edge");

            planner.SetScrub(-2 * 60);
            Assert.That(planner.ScrubMinutes, Is.EqualTo(0), "clamped to the frame start");

            planner.SetZoom(3);
            Assert.That(planner.VisibleSpanMinutes, Is.EqualTo(3 * NightChartVM.OneNightMinutes));
            Assert.That(planner.ScrubMinutes, Is.EqualTo(0));
        }

        [Test]
        public void ZoomWidensTheFrameWithoutMovingTheSelectionOrTheScrub() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);
            var selected = planner.SelectedNight;
            planner.SetScrub(20 * 60);

            planner.ZoomOut();
            planner.ZoomOut();

            Assert.Multiple(() => {
                Assert.That(planner.ZoomNights, Is.EqualTo(3));
                Assert.That(planner.ZoomLabel, Is.EqualTo("3 nights"));
                Assert.That(planner.VisibleSpanMinutes, Is.EqualTo(3 * NightChartVM.OneNightMinutes));
                Assert.That(planner.SelectedNight.MorningDay, Is.EqualTo(selected.MorningDay), "zoom changed which night is selected");
                Assert.That(planner.SelectedNight.Chart.FrameMinutes, Is.EqualTo(3 * NightChartVM.OneNightMinutes));
                Assert.That(planner.SelectedNight.Payload.SunSeries.Count, Is.GreaterThan(selected.Payload.SunSeries.Count),
                    "the chart frame was not stitched from the following nights");
                Assert.That(planner.SelectedNight.Payload.Sunset, Is.EqualTo(selected.Payload.Sunset),
                    "the row must still describe the night that was picked");
                Assert.That(planner.ScrubMinutes, Is.EqualTo(20 * 60), "zooming must not move the scrub marker");
            });

            planner.ZoomIn();
            Assert.Multiple(() => {
                Assert.That(planner.ZoomNights, Is.EqualTo(2));
                Assert.That(planner.ZoomLabel, Is.EqualTo("2 nights"));
            });

            planner.SetZoom(0);
            Assert.That(planner.ZoomNights, Is.EqualTo(1), "never below one night");
            planner.SetZoom(99);
            Assert.That(planner.ZoomNights, Is.EqualTo(NightChartVM.MaxZoomNights), "never past a week");
        }

        [Test]
        public void ZoomButtonsAreToldWhenTheyHaveReachedTheirEnd() {
            var planner = WindowOfThreeNights();
            planner.SelectedDate = Local(2026, 11, 26, 0, 0);
            planner.SetZoom(1);
            var announced = new List<string>();
            planner.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

            planner.ZoomOut();

            Assert.Multiple(() => {
                Assert.That(planner.CanZoomIn, Is.True);
                Assert.That(planner.CanZoomOut, Is.True);
                // The buttons bind their enabled state to these two properties. Left to the
                // commands' own CanExecute the minus button stayed greyed out after the
                // plus had widened the frame - in a dockable pane nothing re-queried it - so
                // the chart could only ever grow.
                Assert.That(announced, Does.Contain(nameof(NightPlannerVM.CanZoomIn)),
                    "leaving the tightest setting has to wake the minus button up");
                Assert.That(announced, Does.Contain(nameof(NightPlannerVM.CanZoomOut)));
            });

            planner.SetZoom(NightChartVM.MaxZoomNights);
            Assert.Multiple(() => {
                Assert.That(planner.CanZoomIn, Is.True);
                Assert.That(planner.CanZoomOut, Is.False, "a week is as wide as the chart goes");
            });

            planner.SetZoom(1);
            Assert.Multiple(() => {
                Assert.That(planner.CanZoomIn, Is.False, "one night is as tight as it gets");
                Assert.That(planner.CanZoomOut, Is.True);
            });
        }

        /// <summary>
        /// The minus button in the panel stayed greyed after the plus had widened the
        /// frame: WPF ANDs a command's CanExecute into the button's enabled state, and
        /// inside a dockable pane nothing reliably re-queries it. So the commands never
        /// refuse and only the IsEnabled binding - which the panel does see change -
        /// decides what the buttons look like.
        /// </summary>
        [Test]
        public void TheZoomCommandsNeverRefuseSoOnlyTheBindingGreysAButton() {
            var profiles = new Mock<IProfileService>();
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            profile.SetupGet(p => p.AstrometrySettings).Returns((IAstrometrySettings)null);
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            var panel = new PlannerDockableVM(
                profiles.Object, new Mock<NINA.Equipment.Interfaces.Mediator.ITelescopeMediator>().Object);
            panel.Planner.SetZoom(1);

            Assert.Multiple(() => {
                Assert.That(panel.Planner.CanZoomIn, Is.False, "the planner still knows one night is the end");
                Assert.That(panel.ZoomInCommand.CanExecute(null), Is.True,
                    "the command must not be the thing that greys the button out");
                Assert.That(panel.ZoomOutCommand.CanExecute(null), Is.True);
            });

            panel.ZoomInCommand.Execute(null);
            Assert.That(panel.Planner.ZoomNights, Is.EqualTo(1), "zooming past an end is a no-op, not a crash");

            panel.ZoomOutCommand.Execute(null);
            Assert.Multiple(() => {
                Assert.That(panel.Planner.ZoomNights, Is.EqualTo(2));
                Assert.That(panel.Planner.CanZoomIn, Is.True, "the binding is what unmutes the minus button");
            });
        }

        /// <summary>
        /// A colour picked on the plugin options page has to arrive on the settings object
        /// the panel reads and be announced by the planner, because that announcement is
        /// what makes the chart repaint.
        /// </summary>
        [Test]
        public void AChartColourSetAnywhereReachesThePlannerAndAnnouncesItself() {
            var profiles = new Mock<IProfileService>();
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            profile.SetupGet(p => p.AstrometrySettings).Returns((IAstrometrySettings)null);
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            var planner = new NightPlannerVM(settings);
            var announced = new List<string>();
            planner.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

            settings.SunColor = "#FFD700";
            settings.MoonColor = "#123456";
            settings.HorizonColor = "ABCDEF";

            Assert.Multiple(() => {
                Assert.That(planner.SunColor, Is.EqualTo("#FFD700"));
                Assert.That(planner.MoonColor, Is.EqualTo("#123456"));
                Assert.That(planner.HorizonColor, Is.EqualTo("#ABCDEF"), "a typed value gains its missing #");
                Assert.That(announced, Has.Member(nameof(NightPlannerVM.SunColor)),
                    "no announcement means no repaint, which is what made picks look ignored");
                Assert.That(announced, Has.Member(nameof(NightPlannerVM.MoonColor)));
                Assert.That(announced, Has.Member(nameof(NightPlannerVM.HorizonColor)));
            });

            settings.ResetColors();
            Assert.That(planner.SunColor, Is.Empty, "clearing a pick follows the scheme again");
        }

        [Test]
        public void ThePrecomputedWindowLooksFurtherAheadThanBehind() {
            Assert.Multiple(() => {
                // History only needs enough to see how the last fortnight went, while
                // planning wants the month ahead. The forward side also decides how far a
                // zoomed frame near the end of the window can still stitch, which is what
                // the blank space at the right edge used to be.
                Assert.That(NightPlannerVM.PastNights, Is.EqualTo(10));
                Assert.That(NightPlannerVM.FutureNights, Is.GreaterThanOrEqualTo(20));
                Assert.That(NightPlannerVM.FutureNights, Is.GreaterThan(NightPlannerVM.PastNights));
            });
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
