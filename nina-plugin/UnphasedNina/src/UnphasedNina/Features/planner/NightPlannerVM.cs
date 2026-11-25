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

        /// <summary>The raw single-night payloads of the window, stitched into rows by RebuildRows.</summary>
        private List<NightPayload> window = new();

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
                // The scrub readout quotes the selected night, so it follows it too.
                RaisePropertyChanged(nameof(ScrubLabel));
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

        // ---- Zoom -------------------------------------------------------------------------
        // How many nights the chart spans, 1 (one noon to the next, like the desktop
        // planner) up to a week. Deliberately not decided by the panel width any more:
        // it was showing two nights on a wide panel without being asked, and the honest
        // answer to "I want to see the week" is a control, not a threshold. Zooming
        // rebuilds the rows from the already-computed window, stitching one day per
        // night payload (see NightFrame), so nothing is recomputed and nothing guessed.
        private int zoomNights = 1;

        public int ZoomNights => zoomNights;

        /// <summary>Header readout for the zoom, e.g. "1 night" or "5 nights".</summary>
        public string ZoomLabel => zoomNights == 1 ? "1 night" : $"{zoomNights} nights";

        public void ZoomIn() => SetZoom(zoomNights - 1);
        public void ZoomOut() => SetZoom(zoomNights + 1);

        public void SetZoom(int nights) {
            int clamped = (int)System.Math.Clamp(nights, 1, NightChartVM.MaxZoomNights);
            if (clamped == zoomNights) return;
            zoomNights = clamped;
            RaisePropertyChanged(nameof(ZoomNights));
            RaisePropertyChanged(nameof(ZoomLabel));
            RebuildRows();
            // A wider frame can only keep the marker inside it by re-clamping it.
            if (scrubMinutes != null) MoveScrub(scrubMinutes.Value);
        }

        /// <summary>
        /// Re-make the rows from the raw window at the current zoom. Selection is
        /// re-matched by morning date so zooming never changes which night is highlighted.
        /// </summary>
        private void RebuildRows() {
            var keep = selectedNight?.MorningDay ?? selectedDate.Date;
            SelectedNight = null;
            Nights.Clear();
            double frameMinutes = NightChartVM.FrameMinutesAtZoom(zoomNights);
            for (int i = 0; i < window.Count; i++) {
                var frame = NightFrame.For(window.GetRange(i, System.Math.Min(zoomNights, window.Count - i)), zoomNights);
                Nights.Add(new NightRowVM(frame, frameMinutes));
            }
            var match = Nights.FirstOrDefault(r => r.MorningDay == keep);
            SelectedNight = match ?? (Nights.Count > 0 ? Nights[Nights.Count / 2] : null);
        }

        // ---- The scrub marker -------------------------------------------------------------
        // The hour the chart is being read at, as a whole-hour offset from the frame
        // start (local noon). It lives here rather than on the chart control so the
        // header buttons, the keyboard, the mouse wheel and dragging across the chart
        // all move one value, and so it outlives a change of night: "+N hours from
        // noon" is the same clock hour in every frame, which is what makes comparing
        // two nights at one glance possible.
        private double? scrubMinutes;

        /// <summary>Current scrub position in minutes from frame noon, or null when the marker is off.</summary>
        public double? ScrubMinutes => scrubMinutes;

        /// <summary>How wide a span the chart is drawing: the zoomed frame, 1 night to a week.</summary>
        public double VisibleSpanMinutes => NightChartVM.FrameMinutesAtZoom(zoomNights);

        /// <summary>Header readout: what the scrub is pointing at, or how to start one.</summary>
        public string ScrubLabel =>
            scrubMinutes == null || selectedNight == null
                ? "Scrub off - click the chart or use the hour buttons"
                : "Scrub " + selectedNight.Chart.ScrubReadout(scrubMinutes.Value);

        /// <summary>Put the marker at a minute offset from local noon, snapped to the nearest whole hour.</summary>
        public void SetScrub(double minutes) => MoveScrub(NightChartVM.SnapToHour(minutes));

        /// <summary>
        /// Step the marker by whole hours. With no marker on screen yet the first
        /// press lands just after sunset of the selected night, which is where a
        /// night is actually interesting.
        /// </summary>
        public void StepScrub(int hours) {
            double from = scrubMinutes ?? (selectedNight == null
                ? 0
                : (selectedNight.Payload.Sunset - selectedNight.Payload.FrameNoon).TotalMinutes);
            MoveScrub(NightChartVM.SnapToHour(from) + hours * 60);
        }

        /// <summary>Take the marker off the chart.</summary>
        public void ClearScrub() {
            if (scrubMinutes == null) return;
            scrubMinutes = null;
            RaisePropertyChanged(nameof(ScrubMinutes));
            RaisePropertyChanged(nameof(ScrubLabel));
        }

        /// <summary>The one place the scrub value changes: snapped to the hour, clamped to the visible span.</summary>
        private void MoveScrub(double minutes) {
            double clamped = System.Math.Clamp(minutes, 0, VisibleSpanMinutes);
            if (scrubMinutes.HasValue && System.Math.Abs(scrubMinutes.Value - clamped) < 0.01) return;
            scrubMinutes = clamped;
            RaisePropertyChanged(nameof(ScrubMinutes));
            RaisePropertyChanged(nameof(ScrubLabel));
        }

        /// <summary>
        /// Show a window of already-computed single-night payloads at the current
        /// zoom. The compute path calls this on the UI thread; tests use it to drive
        /// zoom and stitching without a N.I.N.A. profile.
        /// </summary>
        public void ShowWindow(IReadOnlyList<NightPayload> payloads) {
            window = payloads.ToList();
            RebuildRows();
        }

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
                    ShowWindow(nights);
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
