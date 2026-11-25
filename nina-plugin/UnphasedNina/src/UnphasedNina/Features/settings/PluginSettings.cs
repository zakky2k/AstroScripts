using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Zakfo.Unphased.Features.planner;
// NINA.Core.Utility has a RelayCommand of its own; the commands here are the
// CommunityToolkit ones, whose WPF integration N.I.N.A. also uses.
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace Zakfo.Unphased.Features.settings {
    /// <summary>
    /// Plugin settings. Site latitude/longitude default to the astrometry
    /// settings of the active N.I.N.A. profile but can be overridden per
    /// profile; an explicit override also keeps the chart stable when the
    /// profile has no astrometry site configured. Night mode and the chart
    /// colours are global: night mode switches the chart from the N.I.N.A.
    /// theme palette to the red night-vision scheme, and the colours are
    /// per-role overrides on top of whichever scheme is active.
    ///
    /// One object for the whole plugin, reached through <see cref="For"/> rather than a MEF
    /// import. N.I.N.A. composes a plugin's manifest and its parts in two **separate**
    /// composition containers (PluginLoader.LoadPlugin calls GetContainer for the manifest,
    /// then PluginLoader.Compose calls GetContainer again for the dockables and templates),
    /// and CreationPolicy.Shared only shares within one container. Exported as a part it
    /// therefore existed twice: one copy behind the options page, another behind the panel.
    /// A colour picked on the options page updated the copy the panel never looked at - and
    /// whichever copy wrote last overwrote the other's values in settings.json.
    /// </summary>
    public sealed class PluginSettings : BaseINPC, IDisposable {
        private readonly IProfileService profiles;
        private readonly PluginOptionsAccessor profileOptions;
        private readonly string globalPath;
        private bool darkMode;
        private bool initialized;

        private static readonly object SharedGate = new();

        /// <summary>The object handed out by <see cref="For"/>, or null before anything asks for it.</summary>
        public static PluginSettings Shared { get; private set; }

        /// <summary>
        /// The plugin's settings object, built once per application run. The options page
        /// reaches it through the plugin manifest and the dockable panel through its own
        /// import; N.I.N.A. hands both the same IProfileService, so both get the same
        /// instance and share one set of change notifications.
        /// </summary>
        public static PluginSettings For(IProfileService profiles) {
            lock (SharedGate) {
                Shared ??= new PluginSettings(profiles);
                // Idempotent and cheap: whoever asks first gets the file read.
                Shared.Initialize();
                return Shared;
            }
        }

        // Chart colours, global like night mode - they are a viewing preference, not a
        // property of the site. Stored as hex so the file stays hand-editable; empty
        // means "follow the scheme", which for the sun and the moon is the live N.I.N.A.
        // theme brush, and for the horizon is the plugin's own green.
        private string sunColor = string.Empty, moonColor = string.Empty, horizonColor = string.Empty;

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
                    // The file is written from the fields below, so its keys are camelCase
                    // like "darkMode"; nameof(SunColor) would look for a key never written.
                    sunColor = ReadHex(doc, "sunColor");
                    moonColor = ReadHex(doc, "moonColor");
                    horizonColor = ReadHex(doc, "horizonColor");
                } catch (JsonException) {
                    // Corrupt settings file: keep defaults rather than crash the plugin.
                }
            }
            profiles.ProfileChanged += OnProfileChanged;
            initialized = true;
            RaisePropertyChanged(nameof(DarkMode));
            RaiseColours();
        }

        /// <summary>Red night-vision scheme instead of the N.I.N.A. theme palette.</summary>
        public bool DarkMode {
            get => darkMode;
            set {
                if (darkMode == value) return;
                darkMode = value;
                SaveGlobal();
                RaisePropertyChanged();
                // Night mode changes what an unpicked colour resolves to, so the
                // options page has to repaint its swatches.
                RaiseColours();
            }
        }

        // ---- Chart colours ---------------------------------------------------------------
        // Three roles the user can pin a colour to. Empty means "follow the scheme", and
        // the palette resolves that to the live N.I.N.A. theme brush (sun, moon) or the
        // plugin's own green (horizon). A value that cannot be parsed is stored as typed -
        // so it can be corrected - but is ignored when drawing.

        /// <summary>The sun curve colour, as #rrggbb; empty follows the scheme.</summary>
        public string SunColor {
            get => sunColor;
            set {
                var normalized = NormalizeHex(value);
                if (sunColor == normalized) return;
                sunColor = normalized;
                SaveGlobal();
                RaiseColours();
            }
        }

        /// <summary>The moon track colour, as #rrggbb; empty follows the scheme.</summary>
        public string MoonColor {
            get => moonColor;
            set {
                var normalized = NormalizeHex(value);
                if (moonColor == normalized) return;
                moonColor = normalized;
                SaveGlobal();
                RaiseColours();
            }
        }

        /// <summary>The horizon line colour, as #rrggbb; empty follows the scheme.</summary>
        public string HorizonColor {
            get => horizonColor;
            set {
                var normalized = NormalizeHex(value);
                if (horizonColor == normalized) return;
                horizonColor = normalized;
                SaveGlobal();
                RaiseColours();
            }
        }

        // What the swatches on the options page show: the picked colour if there is one,
        // otherwise what the current scheme resolves to - for the sun and the moon that is
        // a live N.I.N.A. theme brush, which is what "take from the N.I.N.A. palette" means.
        public Brush SunColorBrush => PlannerPalette.EffectiveSun(sunColor, darkMode);
        public Brush MoonColorBrush => PlannerPalette.EffectiveMoon(moonColor, darkMode);
        public Brush HorizonColorBrush => PlannerPalette.EffectiveHorizon(horizonColor, darkMode);

        private IRelayCommand pickSun, pickMoon, pickHorizon, resetColours;

        /// <summary>Pick the sun curve colour.</summary>
        public IRelayCommand PickSunColorCommand =>
            pickSun ??= new RelayCommand(() => PickColour(sunColor, PlannerPalette.EffectiveSun(sunColor, darkMode), c => SunColor = c));

        /// <summary>Pick the moon track colour.</summary>
        public IRelayCommand PickMoonColorCommand =>
            pickMoon ??= new RelayCommand(() => PickColour(moonColor, PlannerPalette.EffectiveMoon(moonColor, darkMode), c => MoonColor = c));

        /// <summary>Pick the horizon line colour.</summary>
        public IRelayCommand PickHorizonColorCommand =>
            pickHorizon ??= new RelayCommand(() => PickColour(horizonColor, PlannerPalette.EffectiveHorizon(horizonColor, darkMode), c => HorizonColor = c));

        /// <summary>
        /// Drop every picked colour. The sun and the moon go back to the live N.I.N.A.
        /// theme palette; the horizon goes back to the plugin's green, because red was
        /// the thing that made it read as one of the tracks.
        /// </summary>
        public void ResetColors() {
            sunColor = moonColor = horizonColor = string.Empty;
            SaveGlobal();
            RaiseColours();
        }

        /// <summary>Reset the three chart colours to their scheme defaults.</summary>
        public IRelayCommand ResetColorsCommand => resetColours ??= new RelayCommand(ResetColors, () => true);

        /// <summary>
        /// Open the colour dialog seeded with what the chart currently shows for that
        /// role, and store the choice as hex. The dialog comes from WinForms because WPF
        /// ships no colour picker; that is the only place in the plugin that uses it.
        /// </summary>
        private void PickColour(string hex, Brush fallback, Action<string> apply) {
            var seed = PlannerPalette.ParseHex(hex)?.Color
                ?? (fallback as SolidColorBrush)?.Color
                ?? Color.FromRgb(0xC0, 0xC0, 0xC0);
            using var dialog = new System.Windows.Forms.ColorDialog {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(seed.R, seed.G, seed.B),
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            apply($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        }

        private void RaiseColours() {
            RaisePropertyChanged(nameof(SunColor));
            RaisePropertyChanged(nameof(MoonColor));
            RaisePropertyChanged(nameof(HorizonColor));
            RaisePropertyChanged(nameof(SunColorBrush));
            RaisePropertyChanged(nameof(MoonColorBrush));
            RaisePropertyChanged(nameof(HorizonColorBrush));
        }

        private static string ReadHex(JsonDocument doc, string name) =>
            doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? NormalizeHex(value.GetString())
                : string.Empty;

        /// <summary>
        /// Trim and add the missing # so a typed value, a picked value and the file all
        /// look the same. Deliberately does not reject nonsense: it gets ignored when
        /// drawn and stays editable.
        /// </summary>
        private static string NormalizeHex(string text) {
            var trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0) return string.Empty;
            return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed : "#" + trimmed;
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
                var payload = JsonSerializer.Serialize(new { darkMode, sunColor, moonColor, horizonColor });
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
            // Stop handing out a stopped object: if the plugin is reloaded within one run of
            // N.I.N.A., For() builds a fresh one that reads the file again.
            lock (SharedGate) {
                if (ReferenceEquals(Shared, this)) Shared = null;
            }
        }
    }
}
