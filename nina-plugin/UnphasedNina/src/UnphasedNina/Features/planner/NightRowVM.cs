using System.Globalization;
using Zakfo.Unphased.Core;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// One row in the night strip: pre-formatted summary labels for the list
    /// above the chart, plus the chart geometry itself.
    /// </summary>
    public sealed class NightRowVM {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public NightRowVM(NightPayload payload) {
            Payload = payload;
            Chart = new NightChartVM(payload);
            DateLabel = payload.NightStart.ToLocalTime().ToString("ddd d MMM", CultureInfo.CurrentCulture);
            DarkLabel = payload.TrueDarkness ? $"{payload.DarkMinutes} min dark" : "no true darkness";
            MoonLabel = payload.MidnightIllumFraction >= 0.995
                ? "full moon"
                : $"{Math.Round(payload.MidnightIllumFraction * 100)}% moon";
            Summary = string.Join("  -  ", new[] { DateLabel, DarkLabel, MoonLabel });
        }

        public NightPayload Payload { get; }
        public NightChartVM Chart { get; }

        /// <summary>
        /// Local calendar day the night starts on (sunset side). This is what
        /// DateLabel shows, so it is also the day a row click is matched on.
        /// </summary>
        public DateTime EveningDay => Payload.NightStart.ToLocalTime().Date;

        /// <summary>
        /// Local calendar day the night ends on (sunrise side). The planner
        /// navigates by this date, so "25 Nov" means the night of 24/25 Nov.
        /// </summary>
        public DateTime MorningDay => EveningDay.AddDays(1);

        /// <summary>Header caption for the selected night, e.g. "Tue 24 Nov -> Wed 25 Nov 2026".</summary>
        public string NightSpanLabel =>
            $"{Payload.Sunset.ToLocalTime():ddd d MMM} \u2192 {Payload.Sunrise.ToLocalTime():ddd d MMM yyyy}";
        public string DateLabel { get; }
        public string DarkLabel { get; }
        public string MoonLabel { get; }
        public string Summary { get; }

        /// <summary>Minutes from frame start (local noon) to sunset, for the marker line.</summary>
        public double SunsetX => Chart.XAt(Payload.Sunset);
        public double SunriseX => Chart.XAt(Payload.Sunrise);
    }
}
