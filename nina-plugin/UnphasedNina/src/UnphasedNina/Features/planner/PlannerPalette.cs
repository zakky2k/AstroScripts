using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// Chart palette. Default mode borrows the live N.I.N.A. theme brushes by
    /// key (BackgroundBrush, PrimaryBrush, ...), so the panel follows whatever
    /// palette the user picked in the N.I.N.A. settings. The NIGHT toggle swaps
    /// every role to a red night-vision hue at comparable lightness - same idea
    /// as the Unphased web app's BLUE/RED palette swap.
    ///
    /// Three roles - the sun track, the moon track and the horizon - can be
    /// overridden by colour pickers in the plugin options (hex strings; empty
    /// means "use the scheme default"). Overrides live on the palette instance,
    /// so the scheme itself stays untouched and "reset" is simply clearing them.
    /// </summary>
    public sealed class PlannerPalette {
        private static readonly SolidColorBrush RedChart = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x04, 0x04)));
        private static readonly SolidColorBrush RedCurve = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x6A)));
        private static readonly SolidColorBrush RedMoon = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0xD2)));
        private static readonly SolidColorBrush RedGrid = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0x5C, 0x1A, 0x1A)));
        private static readonly SolidColorBrush RedAccent = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x3B)));

        // The horizon needs to read as the reference line of the whole chart, so it
        // gets its own, stronger ink instead of the faint gridline colour.
        private static readonly SolidColorBrush RedHorizon = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x64, 0x64)));

        // The horizon is the one line the chart is measured against, and it used to be
        // the theme's SecondaryBrush - red on N.I.N.A.'s default palette, where the moon
        // track is red too and the two read as the same object. A mid green sits well on
        // both the dark and the light N.I.N.A. themes and is not used by any other role.
        // Night mode keeps its red horizon: a green line in an all-red chart defeats the
        // point of the red chart.
        private static readonly SolidColorBrush GreenHorizon = Freeze(new SolidColorBrush(Color.FromRgb(0x4F, 0xC8, 0x7C)));

        // Label backing: near-black and almost opaque, so a word reads the same over
        // the bright daylight band and over full night, in either colour scheme.
        private static readonly SolidColorBrush Chip = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x00, 0x00, 0x00)));

        // Day -> civil -> nautical -> astronomical twilight overlays, in drawing order.
        // Alpha overlays so they read on both a light and a dark N.I.N.A. theme; below
        // -18 deg the base background is left alone, which is the darkest layer.
        private static readonly Brush[] TwilightOverlays = {
            Overlay(0x3D, 0xFF, 0xD9, 0x8A),   // sun up
            Overlay(0x2E, 0x86, 0xB8, 0xE8),   // civil  0 .. -6
            Overlay(0x24, 0x4A, 0x76, 0xB0),   // nautical -6 .. -12
            Overlay(0x1A, 0x24, 0x3C, 0x64),   // astronomical -12 .. -18
        };

        // Same four bands in the red night-vision scheme.
        private static readonly Brush[] TwilightOverlaysRed = {
            Overlay(0x3D, 0xFF, 0xB4, 0x5A),
            Overlay(0x2E, 0xFF, 0x8A, 0x6A),
            Overlay(0x24, 0xE0, 0x50, 0x40),
            Overlay(0x1A, 0xA8, 0x2E, 0x26),
        };

        private static SolidColorBrush Overlay(byte a, byte r, byte g, byte b) =>
            Freeze(new SolidColorBrush(Color.FromArgb(a, r, g, b)));

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
        private static SolidColorBrush Freeze(Color c) => Freeze(new SolidColorBrush(c));

        private static Brush Theme(string key, string fallbackKey) {
            if (Application.Current != null && Application.Current.Resources.Contains(key))
                return Application.Current.Resources[key] as Brush
                    ?? TryTheme(fallbackKey);
            return TryTheme(fallbackKey);
        }

        private static Brush TryTheme(string key) =>
            Application.Current?.Resources.Contains(key) == true
                ? Application.Current.Resources[key] as Brush ?? Brushes.Gray
                : Brushes.Gray;

        /// <summary>Chart canvas background (deepest panel layer).</summary>
        public Brush ChartBackground(bool dark) =>
            dark ? RedChart : Theme("TertiaryBackgroundBrush", "BackgroundBrush");

        /// <summary>Text, axis labels and degree numbers.</summary>
        public Brush Ink(bool dark) => InkDefault(dark);

        /// <summary>
        /// The sun curve. Its own role rather than Ink, so picking a sun colour in the
        /// options repaints the curve and leaves the text alone.
        /// </summary>
        public Brush SunTrack(bool dark) => sunOverride ?? InkDefault(dark);

        /// <summary>The moon track - lighter than the ink but same family by default.</summary>
        public Brush MoonTrack(bool dark) => moonOverride ?? MoonDefault(dark);

        private static Brush InkDefault(bool dark) =>
            dark ? RedCurve : Theme("PrimaryBrush", "ButtonForegroundBrush");

        private static Brush MoonDefault(bool dark) =>
            dark ? RedMoon : Theme("SecondaryBrush", "ButtonForegroundBrush");

        // The three pickable colours, from the plugin options, parsed once per change.
        // Null means "no override": the role follows the scheme.
        private SolidColorBrush sunOverride, moonOverride, horizonOverride;

        /// <summary>
        /// Take the pickable colours from the plugin options (hex strings; empty means
        /// "follow the scheme"). The chart control calls this whenever the options
        /// change, and the settings page uses the same defaults for its swatches.
        /// </summary>
        public void SetOverrides(string sun, string moon, string horizon) {
            sunOverride = ParseHex(sun);
            moonOverride = ParseHex(moon);
            horizonOverride = ParseHex(horizon);
        }

        /// <summary>
        /// A colour typed into the options as a frozen brush: accepts #rgb, #rrggbb and
        /// #aarrggbb, with or without the leading #. Null for empty or malformed input,
        /// which is how "no override" and a typo are both handled - a bad colour must
        /// never take the chart down.
        /// </summary>
        public static SolidColorBrush ParseHex(string hex) {
            var text = (hex ?? string.Empty).Trim();
            if (text.StartsWith("#")) text = text.Substring(1);
            byte a = 0xFF, r, g, b;
            switch (text.Length) {
                case 3:
                    if (!byte.TryParse(text.Substring(0, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return null;
                    if (!byte.TryParse(text.Substring(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)) return null;
                    if (!byte.TryParse(text.Substring(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b)) return null;
                    r = (byte)(r * 0x11); g = (byte)(g * 0x11); b = (byte)(b * 0x11);
                    break;
                case 6:
                case 8:
                    int offset = text.Length == 8 ? 2 : 0;
                    if (text.Length == 8 && !byte.TryParse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)) return null;
                    if (!byte.TryParse(text.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return null;
                    if (!byte.TryParse(text.Substring(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)) return null;
                    if (!byte.TryParse(text.Substring(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b)) return null;
                    break;
                default:
                    return null;
            }
            return Freeze(new SolidColorBrush(Color.FromArgb(a, r, g, b)));
        }

        /// <summary>
        /// The colour a swatch in the options should show for one role: the override if
        /// there is one, otherwise what the current scheme resolves to - which for the
        /// sun and the moon is a live N.I.N.A. theme brush.
        /// </summary>
        public static Brush Effective(string hex, bool dark, Func<bool, Brush> fallback) =>
            ParseHex(hex) ?? fallback(dark);

        public static Brush EffectiveSun(string hex, bool dark) => Effective(hex, dark, InkDefault);
        public static Brush EffectiveMoon(string hex, bool dark) => Effective(hex, dark, MoonDefault);
        public static Brush EffectiveHorizon(string hex, bool dark) => Effective(hex, dark, HorizonDefault);

        /// <summary>Faint gridlines and band separators.</summary>
        public Brush Gridline(bool dark) =>
            dark ? RedGrid : Theme("BorderBrush", "AltBorderBrush");

        /// <summary>Selection markers (selected night, sunset/sunrise ticks).</summary>
        public Brush Accent(bool dark) =>
            dark ? RedAccent : Theme("ButtonBackgroundSelectedBrush", "ButtonSelectBrush");

        /// <summary>
        /// The scrub line, its handle and its readout. Its own role rather than
        /// Accent, because it is the one vertical on the chart and it is a control, not
        /// a piece of the sky - it has to read as "the thing I moved".
        /// </summary>
        public Brush Scrub(bool dark) =>
            dark ? RedMoon : Theme("ButtonBackgroundSelectedBrush", "ButtonSelectBrush");

        /// <summary>
        /// Which twilight band a sun altitude sits in: 0 daylight, 1 civil (0..-6),
        /// 2 nautical (-6..-12), 3 astronomical (-12..-18), -1 full night. Exposed as
        /// a number so the chart can merge consecutive samples of one band into a
        /// single background run instead of one rectangle per sample.
        /// </summary>
        public static int TwilightBand(double sunAltitude) {
            if (sunAltitude > 0) return 0;
            if (sunAltitude > -6) return 1;
            if (sunAltitude > -12) return 2;
            if (sunAltitude > -18) return 3;
            return -1;
        }

        /// <summary>Overlay for one band; null is full night, left as the base background.</summary>
        public Brush TwilightBandBrush(bool dark, int band) {
            if (band < 0) return null;
            var table = dark ? TwilightOverlaysRed : TwilightOverlays;
            return band < table.Length ? table[band] : null;
        }

        /// <summary>
        /// Background overlay for the band a sun altitude falls in: daylight,
        /// civil, nautical or astronomical twilight. Null means astronomical
        /// night, where the chart background is left as the darkest layer.
        /// </summary>
        public Brush Twilight(bool dark, double sunAltitude) => TwilightBandBrush(dark, TwilightBand(sunAltitude));

        /// <summary>
        /// The horizon (alt 0) line: stronger than the twilight gridlines, and the line
        /// every degree on the chart is measured from.
        /// </summary>
        public Brush Horizon(bool dark) => horizonOverride ?? HorizonDefault(dark);

        /// <summary>The horizon colour when nothing has been picked for it.</summary>
        public static Brush HorizonDefault(bool dark) => dark ? RedHorizon : GreenHorizon;

        /// <summary>
        /// The red night-vision palette for the panel's **chrome**, keyed by the brush
        /// names N.I.N.A. itself defines. The hexes are copied from the built-in scheme
        /// N.I.N.A. calls "Dark", which is the one its own night-vision eye button
        /// switches the whole application to - so the panel matches the desktop app
        /// rather than an approximation of it. PlannerViewGlue.NightPalette installs them
        /// in the panel's own resource dictionary, and a DynamicResource lookup walks out
        /// from the element, so they apply inside this panel and nowhere else.
        ///
        /// Two things worth knowing about the source scheme, both left as they are:
        /// PrimaryColor/BorderColor are a dark red (#550C18) rather than a bright one,
        /// and SecondaryColor is navy - the desktop night palette is not purely red.
        /// Only what the plugin draws follows these: N.I.N.A.'s own control styles bound
        /// their colours to the application's palette when the application loaded, so a
        /// plugin cannot recolour the button style itself without editing that palette,
        /// which would change every other tab too.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Brush> NightChrome =
            new Dictionary<string, Brush> {
                ["BackgroundBrush"] = Freeze(Color.FromArgb(0xFF, 0x02, 0x01, 0x0A)),
                ["SecondaryBackgroundBrush"] = Freeze(Color.FromArgb(0xFF, 0x23, 0x04, 0x09)),
                ["TertiaryBackgroundBrush"] = Freeze(Color.FromArgb(0xFF, 0x2D, 0x06, 0x0D)),
                ["PrimaryBrush"] = Freeze(Color.FromArgb(0xFF, 0x55, 0x0C, 0x18)),
                ["SecondaryBrush"] = Freeze(Color.FromArgb(0xFF, 0x1B, 0x2A, 0x41)),
                ["BorderBrush"] = Freeze(Color.FromArgb(0xFF, 0x55, 0x0C, 0x18)),
                ["ButtonForegroundBrush"] = Freeze(Color.FromArgb(0xFF, 0x02, 0x01, 0x0A)),
                ["ButtonForegroundDisabledBrush"] = Freeze(Color.FromArgb(0xFF, 0x44, 0x37, 0x30)),
            };

        /// <summary>Near-black backing behind chart labels.</summary>
        public Brush ChipFill(bool dark) => Chip;
    }
}
