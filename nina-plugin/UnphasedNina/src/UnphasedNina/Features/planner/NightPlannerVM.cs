using NINA.Core.Utility;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Zakfo.Unphased.Core;
using Zakfo.Unphased.Features.settings;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// View model behind the planner panel: computes a window of nights
    /// (today plus/minus ten) around the selected date for the effective
    /// site, mirroring how the Unphased web app prefetches its night strip.
    /// Computation runs on a background thread so the UI never blocks.
    /// </summary>
    public sealed class NightPlannerVM : BaseINPC {
        private const int WindowNights = 10;

        private readonly PluginSettings settings;

        private DateTimeOffset selectedDate;
        private NightRowVM selectedNight;
        private string statusText = "Waiting for a site - set latitude/longitude in the plugin options or configure astrometry in the profile.";
        private bool busy;
        private Site? computedSite;
        private int computeToken;

        /// <summary>The UI thread dispatcher (a plain dispatcher outside WPF, so the view model stays unit-testable).</summary>
        private static Dispatcher Ui => Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        public NightPlannerVM(PluginSettings settings) {
            this.settings = settings;
            selectedDate = DateTimeOffset.Now.Date;
            Nights = new ObservableCollection<NightRowVM>();
            settings.PropertyChanged += (_, e) => {
                if (e.PropertyName is nameof(PluginSettings.SiteLatOverride) or nameof(PluginSettings.SiteLonOverride))
                    RequestRecompute();
                if (e.PropertyName == nameof(PluginSettings.DarkMode))
                    Ui.Invoke(() => RaisePropertyChanged(nameof(DarkMode)));
            };
        }

        public ObservableCollection<NightRowVM> Nights { get; }

        /// <summary>Red night-vision scheme (global DARK toggle from plugin options).</summary>
        public bool DarkMode => settings.DarkMode;

        /// <summary>
        /// The night the user navigated to (prev/next/today). Changing it also
        /// re-picks the matching row, so the highlighted row, the header and the
        /// chart can never drift apart.
        /// </summary>
        public DateTimeOffset SelectedDate {
            get => selectedDate;
            set {
                var day = value.Date;
                if (selectedDate == day) return;
                selectedDate = day;
                RaisePropertyChanged();
                ApplySelection();
            }
        }

        /// <summary>
        /// The highlighted night row. The chart draws this row and the header
        /// captions it, so selection is owned here instead of in view glue code.
        /// A row click (two-way binding) moves the anchor date to that night.
        /// </summary>
        public NightRowVM SelectedNight {
            get => selectedNight;
            set {
                if (ReferenceEquals(selectedNight, value)) return;
                selectedNight = value;
                RaisePropertyChanged();
                if (value != null && selectedDate.Date != value.MorningDay) {
                    selectedDate = value.MorningDay;
                    RaisePropertyChanged(nameof(SelectedDate));
                }
                RaisePropertyChanged(nameof(SelectedDateLabel));
            }
        }

        /// <summary>Header caption; follows the selected row, not just the anchor date.</summary>
        public string SelectedDateLabel =>
            selectedNight?.NightSpanLabel ?? selectedDate.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);

        /// <summary>
        /// Highlight the night whose morning is the anchor date; if the window
        /// does not contain it (or nothing is computed yet) fall back to the
        /// middle of the window so something is always drawn.
        /// </summary>
        private void ApplySelection() {
            var match = Nights.FirstOrDefault(r => r.MorningDay == selectedDate.Date);
            SelectedNight = match ?? (Nights.Count > 0 ? Nights[Nights.Count / 2] : null);
        }

        public string StatusText {
            get => statusText;
            private set { statusText = value; RaisePropertyChanged(); }
        }

        public bool Busy {
            get => busy;
            private set { busy = value; RaisePropertyChanged(); }
        }

        public void PrevNight() => SelectedDate = selectedDate.AddDays(-1);
        public void NextNight() => SelectedDate = selectedDate.AddDays(1);
        public void Tonight() => SelectedDate = DateTimeOffset.Now.Date;

        /// <summary>
        /// Recompute the whole window for the effective site. Cheap enough to
        /// redo in full (21 nights run in a few tens of milliseconds).
        /// </summary>
        public void RequestRecompute() {
            var siteOpt = settings.EffectiveSite();
            if (siteOpt == null) {
                StatusText = "No site available: configure astrometry in the profile or set an override in the plugin options.";
                return;
            }
            var site = siteOpt.Value;
            var siteValue = new Site(site.Lat, site.Lon);
            int token = ++computeToken;
            var ui = Ui;
            Busy = true;
            Task.Run(() => {
                List<NightPayload> nights;
                string error = null;
                try {
                    nights = ComputeWindow(siteValue, selectedDate);
                } catch (NightException ex) {
                    error = ex.Message;
                    nights = null;
                } catch (Exception ex) {
                    Logger.Error("Unphased planner calculation failed", ex);
                    error = "Planner calculation failed: " + ex.Message;
                    nights = null;
                }
                if (token != computeToken) return; // a newer request superseded this one
                ui.Invoke(() => {
                    Busy = false;
                    if (nights == null) {
                        StatusText = error;
                        return;
                    }
                    computedSite = siteValue;
                    SelectedNight = null;
                    Nights.Clear();
                    foreach (var p in nights) Nights.Add(new NightRowVM(p));
                    // Select once, after the window is complete: selecting while
                    // the list is still growing left the highlight on the wrong row.
                    ApplySelection();
                    StatusText = $"Site {site.Lat:F4}, {site.Lon:F4} - window {Nights.Count} nights";
                });
            });
        }

        /// <summary>
        /// The night frame pivot follows the Unphased convention: local noon
        /// of (selectedDate - 1 day), so the frame covers the night that
        /// starts on selectedDate's evening.
        /// </summary>
        private static List<NightPayload> ComputeWindow(Site site, DateTimeOffset anchor) {
            var tz = TimeZoneInfo.Local;
            var calc = new NightCalculator(site);
            var result = new List<NightPayload>();
            for (int d = -WindowNights; d <= WindowNights; d++) {
                var day = anchor.Date.AddDays(d);
                var noonUtc = TimeZoneInfo.ConvertTimeToUtc(
                    new DateTime(day.Year, day.Month, day.Day, 12, 0, 0) - TimeSpan.FromDays(1), tz);
                try {
                    result.Add(calc.Compute(new DateTimeOffset(noonUtc, TimeSpan.Zero)));
                } catch (NightException) {
                    // Polar/no-night frames simply drop out of the window.
                }
            }
            return result;
        }

        /// <summary>Site the currently shown nights were computed for (null before first compute).</summary>
        public Site? ComputedSite => computedSite;
    }
}
