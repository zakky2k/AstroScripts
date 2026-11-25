using NUnit.Framework;
using Zakfo.Unphased.Features.settings;
using Moq;
using NINA.Profile.Interfaces;

namespace Zakfo.Unphased.Tests.Features.settings {
    [NonParallelizable, Apartment(ApartmentState.STA)]
    public class FeatureTests {
        private static Mock<IProfileService> ProfilesWithAstrometry(double lat, double lon) {
            var profiles = new Mock<IProfileService>();
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            var astro = new Mock<IAstrometrySettings>();
            astro.SetupGet(a => a.Latitude).Returns(lat);
            astro.SetupGet(a => a.Longitude).Returns(lon);
            profile.SetupGet(p => p.AstrometrySettings).Returns(astro.Object);
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            return profiles;
        }

        [Test]
        public void EffectiveSiteFallsBackToProfileAstrometry() {
            var profiles = ProfilesWithAstrometry(50.0875, 14.4213);
            using var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            Assert.That(settings.EffectiveSite(), Is.EqualTo((50.0875, 14.4213)));
        }

        [Test]
        public void OverrideWinsOverProfileAndPartialOverrideIsIgnored() {
            var profiles = ProfilesWithAstrometry(50.0875, 14.4213);
            using var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            settings.SiteLatOverride = "-33.8688";
            // Only latitude set: incomplete override must not half-apply.
            Assert.That(settings.EffectiveSite(), Is.EqualTo((50.0875, 14.4213)));
            settings.SiteLonOverride = "151.2093";
            Assert.That(settings.EffectiveSite(), Is.EqualTo((-33.8688, 151.2093)));
            // Garbage input falls back to the profile instead of breaking.
            settings.SiteLonOverride = "not a number";
            Assert.That(settings.EffectiveSite(), Is.EqualTo((50.0875, 14.4213)));
        }

        [Test]
        public void NoProfileSiteAndNoOverrideYieldsNull() {
            var profiles = new Mock<IProfileService>();
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            profile.SetupGet(p => p.AstrometrySettings).Returns((IAstrometrySettings)null);
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            using var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            Assert.That(settings.EffectiveSite(), Is.Null);
        }

        [Test]
        public void ProfileSwitchRaisesOverridePropertyNotifications() {
            var profiles = ProfilesWithAstrometry(50.0875, 14.4213);
            using var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            var notifications = new List<string>();
            settings.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
            profiles.Raise(p => p.ProfileChanged += null, EventArgs.Empty);
            Assert.That(notifications, Does.Contain(nameof(PluginSettings.SiteLatOverride)));
            Assert.That(notifications, Does.Contain(nameof(PluginSettings.SiteLonOverride)));
            // After dispose the handler is detached: no more notifications.
            settings.Dispose();
            notifications.Clear();
            profiles.Raise(p => p.ProfileChanged += null, EventArgs.Empty);
            Assert.That(notifications, Is.Empty);
        }

        [Test]
        public void DarkModePersistsAcrossInstances() {
            var profiles = ProfilesWithAstrometry(50.0875, 14.4213);
            using (var settings = new PluginSettings(profiles.Object)) {
                settings.Initialize();
                settings.DarkMode = true;
            }
            using (var restored = new PluginSettings(profiles.Object)) {
                restored.Initialize();
                Assert.That(restored.DarkMode, Is.True);
                restored.DarkMode = false;
            }
            using var clean = new PluginSettings(profiles.Object);
            clean.Initialize();
            Assert.That(clean.DarkMode, Is.False);
        }

        [Test]
        public void PickedChartColoursPersistAndResetToTheScheme() {
            // Same caveat as the night mode test above: these settings live in the real
            // plugin options file, so the test leaves every role unpicked again.
            var profiles = ProfilesWithAstrometry(50.0875, 14.4213);
            using (var settings = new PluginSettings(profiles.Object)) {
                settings.Initialize();
                settings.SunColor = "FFD700";
                Assert.Multiple(() => {
                    Assert.That(settings.SunColor, Is.EqualTo("#FFD700"), "a typed value gains its # and keeps it");
                    Assert.That(((System.Windows.Media.SolidColorBrush)settings.SunColorBrush).Color,
                        Is.EqualTo(System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)));
                });
            }
            using (var restored = new PluginSettings(profiles.Object)) {
                restored.Initialize();
                Assert.Multiple(() => {
                    Assert.That(restored.SunColor, Is.EqualTo("#FFD700"), "the pick survives a restart");
                    Assert.That(restored.MoonColor, Is.Empty, "and only the role that was picked changes");
                });
                restored.ResetColors();
                Assert.That(restored.SunColor, Is.Empty);
            }
            using var clean = new PluginSettings(profiles.Object);
            clean.Initialize();
            Assert.Multiple(() => {
                Assert.That(clean.SunColor, Is.Empty, "the reset is persisted too");
                // An unpicked swatch is not blank: it shows what the chart paints.
                Assert.That(clean.SunColorBrush, Is.Not.Null);
                Assert.That(clean.HorizonColorBrush, Is.Not.Null);
            });
        }

        [Test]
        public void OptionsLookupUsesTheManifestName() {
            var profiles = ProfilesWithAstrometry(50.0875, 14.4213);
            using var settings = new PluginSettings(profiles.Object);
            settings.Initialize();
            var manifest = new Plugin { Settings = settings };
            var resources = new Resources();
            Assert.That(resources.Contains("OptionsTemplate"), Is.False);
            Assert.That(resources.Contains(manifest.Name + "_Options"), Is.True);
        }
    }
}
