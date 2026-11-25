using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Zakfo.Unphased.Features.settings {
    /// <summary>
    /// Plugin settings. Site latitude/longitude default to the astrometry
    /// settings of the active N.I.N.A. profile but can be overridden per
    /// profile; an explicit override also keeps the chart stable when the
    /// profile has no astrometry site configured. Dark mode is global: it
    /// switches the chart from the N.I.N.A. theme palette to the red
    /// night-vision scheme.
    /// </summary>
    [Export(typeof(PluginSettings))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public sealed class PluginSettings : BaseINPC, IDisposable {
        private readonly IProfileService profiles;
        private readonly PluginOptionsAccessor profileOptions;
        private readonly string globalPath;
        private bool darkMode;
        private bool initialized;

        [ImportingConstructor]
        public PluginSettings(IProfileService profiles) {
            this.profiles = profiles;
            var id = new Plugin().Identifier;
            profileOptions = new PluginOptionsAccessor(profiles, Guid.Parse(id));
            globalPath = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "PluginSettings", id, "settings.json");
        }

        public void Initialize() {
            if (initialized) return;
            if (File.Exists(globalPath)) {
                try {
                    using var doc = JsonDocument.Parse(File.ReadAllText(globalPath));
                    if (doc.RootElement.TryGetProperty("darkMode", out var dark))
                        darkMode = dark.GetBoolean();
                } catch (JsonException) {
                    // Corrupt settings file: keep defaults rather than crash the plugin.
                }
            }
            profiles.ProfileChanged += OnProfileChanged;
            initialized = true;
            RaisePropertyChanged(nameof(DarkMode));
        }

        /// <summary>Red night-vision scheme instead of the N.I.N.A. theme palette.</summary>
        public bool DarkMode {
            get => darkMode;
            set {
                if (darkMode == value) return;
                darkMode = value;
                SaveGlobal();
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Latitude override. Empty means "take it from the profile's
        /// astrometry settings". Stored as an invariant-culture decimal string.
        /// </summary>
        public string SiteLatOverride {
            get => profileOptions.GetValueString(nameof(SiteLatOverride), string.Empty);
            set { profileOptions.SetValueString(nameof(SiteLatOverride), value ?? string.Empty); RaisePropertyChanged(); }
        }

        /// <summary>Longitude override; empty means "from the profile".</summary>
        public string SiteLonOverride {
            get => profileOptions.GetValueString(nameof(SiteLonOverride), string.Empty);
            set { profileOptions.SetValueString(nameof(SiteLonOverride), value ?? string.Empty); RaisePropertyChanged(); }
        }

        /// <summary>
        /// Effective site: override if both fields parse, otherwise the
        /// profile's astrometry lat/lon. Null when neither is available.
        /// </summary>
        public (double Lat, double Lon)? EffectiveSite() {
            var latText = SiteLatOverride;
            var lonText = SiteLonOverride;
            if (TryCoord(latText, out var lat) && TryCoord(lonText, out var lon))
                return (lat, lon);
            var astro = profiles.ActiveProfile?.AstrometrySettings;
            if (astro != null && double.IsFinite(astro.Latitude) && double.IsFinite(astro.Longitude))
                return (astro.Latitude, astro.Longitude);
            return null;
        }

        private static bool TryCoord(string text, out double value) {
            value = 0;
            return !string.IsNullOrWhiteSpace(text) &&
                double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                double.IsFinite(value);
        }

        private void SaveGlobal() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(globalPath));
                var payload = JsonSerializer.Serialize(new { darkMode });
                var temporaryPath = globalPath + ".tmp";
                File.WriteAllText(temporaryPath, payload);
                File.Move(temporaryPath, globalPath, true);
            } catch (IOException) {
                // Settings persistence is best-effort; in-memory state stays correct.
            }
        }

        private void OnProfileChanged(object sender, EventArgs args) {
            RaisePropertyChanged(nameof(SiteLatOverride));
            RaisePropertyChanged(nameof(SiteLonOverride));
        }

        public void Dispose() {
            if (!initialized) return;
            profiles.ProfileChanged -= OnProfileChanged;
            initialized = false;
        }
    }
}
