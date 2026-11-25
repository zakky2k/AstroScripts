using System.Windows;
using System.Windows.Media;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// Chart palette. Default mode borrows the live N.I.N.A. theme brushes by
    /// key (BackgroundBrush, PrimaryBrush, ...), so the panel follows whatever
    /// palette the user picked in the N.I.N.A. settings. The DARK toggle swaps
    /// every role to a red night-vision hue at comparable lightness - same idea
    /// as the Unphased web app's BLUE/RED palette swap.
    /// </summary>
    public sealed class PlannerPalette {
        private static readonly SolidColorBrush RedChart = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x04, 0x04)));
        private static readonly SolidColorBrush RedCurve = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x6A)));
        private static readonly SolidColorBrush RedMoon = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0xD2)));
        private static readonly SolidColorBrush RedGrid = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0x5C, 0x1A, 0x1A)));
        private static readonly SolidColorBrush RedAccent = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x3B)));

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

        /// <summary>Text, axis labels and the sun curve.</summary>
        public Brush Ink(bool dark) =>
            dark ? RedCurve : Theme("PrimaryBrush", "ButtonForegroundBrush");

        /// <summary>The moon track - lighter than the ink but same family.</summary>
        public Brush MoonTrack(bool dark) =>
            dark ? RedMoon : Theme("SecondaryBrush", "ButtonForegroundBrush");

        /// <summary>Faint gridlines and band separators.</summary>
        public Brush Gridline(bool dark) =>
            dark ? RedGrid : Theme("BorderBrush", "AltBorderBrush");

        /// <summary>Selection markers (selected night, sunset/sunrise ticks).</summary>
        public Brush Accent(bool dark) =>
            dark ? RedAccent : Theme("ButtonBackgroundSelectedBrush", "ButtonSelectBrush");

        /// <summary>
        /// Background overlay for the band a sun altitude falls in: daylight,
        /// civil, nautical or astronomical twilight. Null means astronomical
        /// night, where the chart background is left as the darkest layer.
        /// </summary>
        public Brush Twilight(bool dark, double sunAltitude) {
            var table = dark ? TwilightOverlaysRed : TwilightOverlays;
            if (sunAltitude > 0) return table[0];
            if (sunAltitude > -6) return table[1];
            if (sunAltitude > -12) return table[2];
            if (sunAltitude > -18) return table[3];
            return null;
        }
    }
}
