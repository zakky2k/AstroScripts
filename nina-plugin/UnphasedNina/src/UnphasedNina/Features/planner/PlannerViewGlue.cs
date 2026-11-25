using NINA.Core.Utility;
using System.Windows;
using System.Windows.Controls;
using Zakfo.Unphased.Features.settings;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// Small code-behind glue for the panel template: feeds the selected night
    /// row into the chart control and triggers the first computation when the
    /// panel becomes visible. No custom controls, no global styles.
    /// </summary>
    public static class PlannerViewGlue {
        public static readonly DependencyProperty ChartProperty =
            DependencyProperty.RegisterAttached("Chart", typeof(NightChartControl), typeof(PlannerViewGlue),
                new PropertyMetadata(null, OnChartChanged));

        public static NightChartControl GetChart(DependencyObject d) => (NightChartControl)d.GetValue(ChartProperty);
        public static void SetChart(DependencyObject d, NightChartControl v) => d.SetValue(ChartProperty, v);

        private static void OnChartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not ListBox list || e.NewValue is not NightChartControl chart) return;
            var planner = FindPlanner(list.DataContext);
            if (planner == null) return;

            // The view model owns the selection, so the highlighted row (two-way
            // SelectedItem binding), the header caption and the chart all follow
            // one value. Row clicks travel back through the same binding.
            planner.PropertyChanged += (_, args) => {
                if (args.PropertyName == nameof(NightPlannerVM.SelectedNight))
                    chart.SetRow(planner.SelectedNight);
            };
            chart.SetRow(planner.SelectedNight);

            // Kick off the first computation the first time the panel loads.
            list.Loaded += (_, _) => {
                if (planner.ComputedSite == null && planner.Nights.Count == 0) planner.RequestRecompute();
            };
        }

        private static NightPlannerVM FindPlanner(object dc) => dc switch {
            NightPlannerVM p => p,
            PlannerDockableVM d => d.Planner,
            _ => null
        };
    }
}
