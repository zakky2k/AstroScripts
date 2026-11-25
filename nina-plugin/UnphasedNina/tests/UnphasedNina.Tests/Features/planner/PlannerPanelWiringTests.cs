using Moq;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zakfo.Unphased.Features.planner;

namespace Zakfo.Unphased.Tests.Features.planner {
    /// <summary>
    /// The panel as N.I.N.A. will actually instantiate it: the real data template, loaded,
    /// given the real view model, and laid out in a window. The night list used to be the
    /// thing that handed the chart its row and the thing that kicked off the first
    /// computation; with a calendar in its place, those wires are now attached properties on
    /// a different element, and a broken ElementName binding fails silently in the live app -
    /// the chart simply paints an empty rectangle. So this loads the markup and looks.
    /// </summary>
    [NonParallelizable, Apartment(ApartmentState.STA)]
    public class PlannerPanelWiringTests {
        private Window host;

        [SetUp]
        public void SetUp() {
            WpfHost.Ensure();
            // An off-screen window, because template bindings, the visual tree and layout all
            // need the content to be hosted; 8x8 would squeeze the panel's star-sized rows.
            host = new Window {
                Left = -32000, Top = -32000, Width = 900, Height = 700,
                ShowInTaskbar = false, Opacity = 0, ShowActivated = false,
            };
            host.Show();
        }

        [TearDown]
        public void TearDown() {
            host.Close();
            host = null;
        }

        [Test]
        public void TheCalendarHandsTheChartTheSelectedNight() {
            var panel = new PlannerDockableVM(ProfilesWithNoSite(), new Mock<NINA.Equipment.Interfaces.Mediator.ITelescopeMediator>().Object);
            panel.Planner.ShowWindow(new[] {
                PlannerFixture.Night(2026, 11, 24, 691, 0.99),
                PlannerFixture.Night(2026, 11, 25, 692, 0.97),
                PlannerFixture.Night(2026, 11, 26, 694, 0.92),
            });
            panel.Planner.SelectedDate = PlannerFixture.Local(2026, 11, 26, 0, 0);

            var root = Panel(panel);
            var chart = Only<NightChartControl>(root);

            Assert.That(chart.Row, Is.SameAs(panel.Planner.SelectedNight),
                "the chart must be drawing the night the calendar has selected");

            // And it has to keep following, not just start in the right place.
            panel.Planner.NextNight();
            host.UpdateLayout();
            Assert.That(chart.Row, Is.SameAs(panel.Planner.SelectedNight),
                "changing the night from any direction has to redraw the chart");
        }

        [Test]
        public void ThePanelCarriesACalendarAndNoScrollingNightList() {
            var panel = new PlannerDockableVM(ProfilesWithNoSite(), new Mock<NINA.Equipment.Interfaces.Mediator.ITelescopeMediator>().Object);
            panel.Planner.ShowWindow(new[] { PlannerFixture.Night(2026, 11, 24, 691, 0.99) });
            panel.Planner.SelectedDate = PlannerFixture.Local(2026, 11, 26, 0, 0);

            var root = Panel(panel);
            var picker = Only<DatePicker>(root);

            Assert.Multiple(() => {
                Assert.That(picker.SelectedDate, Is.EqualTo(panel.Planner.SelectedDateForPicker),
                    "the calendar has to show the night that is highlighted, not the day it opened on");
                Assert.That(Visual<ListBox>(root), Is.Empty,
                    "the night list is gone: the calendar and the chips replaced it");
                Assert.That(PlannerViewGlue.GetChart(picker), Is.SameAs(Only<NightChartControl>(root)),
                    "the picker is what carries the chart wire now the list is gone");
            });
        }

        [Test]
        public void TheHeaderShowsTheSelectedNightsThreeFigures() {
            var panel = new PlannerDockableVM(ProfilesWithNoSite(), new Mock<NINA.Equipment.Interfaces.Mediator.ITelescopeMediator>().Object);
            panel.Planner.ShowWindow(new[] {
                PlannerFixture.Night(2026, 11, 24, 691, 0.99),
                PlannerFixture.Night(2026, 11, 25, 692, 0.97),
            });
            panel.Planner.SelectedDate = PlannerFixture.Local(2026, 11, 26, 0, 0);

            var root = Panel(panel);
            var row = panel.Planner.SelectedNight;
            var texts = Visual<TextBlock>(root).Select(t => t.Text).ToList();

            Assert.Multiple(() => {
                Assert.That(texts, Does.Contain(row.SunChip), "the sunset-to-sunrise chip is not on the panel");
                Assert.That(texts, Does.Contain(row.DarkChip), "the dark-hours chip is not on the panel");
                Assert.That(texts, Does.Contain(row.MoonChip), "the moon chip is not on the panel");
            });
        }

        /// <summary>Load the real dockable template, bind it, host it, and lay it out.</summary>
        private FrameworkElement Panel(PlannerDockableVM panel) {
            var dictionary = new Zakfo.Unphased.Features.planner.Resources();
            var key = typeof(PlannerDockableVM).FullName + "_Dockable";
            var root = (FrameworkElement)((DataTemplate)dictionary[key]).LoadContent();
            root.DataContext = panel;
            host.Content = root;
            host.UpdateLayout();
            return root;
        }

        private static T Only<T>(DependencyObject root) where T : DependencyObject {
            var found = Visual<T>(root).ToList();
            Assert.That(found, Has.Count.EqualTo(1), $"the panel must contain exactly one {typeof(T).Name}");
            return found[0];
        }

        private static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var deeper in Visual<T>(child)) yield return deeper;
            }
        }

        /// <summary>A profile with no astrometry settings, so the panel has no site to compute for.</summary>
        private static IProfileService ProfilesWithNoSite() {
            var profiles = new Mock<IProfileService>();
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            profile.SetupGet(p => p.AstrometrySettings).Returns((IAstrometrySettings)null);
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            return profiles.Object;
        }
    }
}
