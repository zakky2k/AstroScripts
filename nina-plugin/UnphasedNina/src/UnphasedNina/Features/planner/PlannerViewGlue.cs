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

        /// <summary>
        /// Scope the red night-vision palette to one element's subtree. N.I.N.A.'s styles
        /// resolve their colours through DynamicResource, and a DynamicResource lookup
        /// walks outwards from the element asking each ancestor in turn, so brushes held
        /// in this panel's own resource dictionary win for this panel alone. The imaging
        /// tab, every other tab and the rest of the application keep the colours the user
        /// picked in N.I.N.A. - the plugin never edits the application theme.
        /// </summary>
        public static readonly DependencyProperty NightPaletteProperty =
            DependencyProperty.RegisterAttached("NightPalette", typeof(bool), typeof(PlannerViewGlue),
                new PropertyMetadata(false, OnNightPaletteChanged));

        public static bool GetNightPalette(DependencyObject d) => (bool)d.GetValue(NightPaletteProperty);
        public static void SetNightPalette(DependencyObject d, bool value) => d.SetValue(NightPaletteProperty, value);

        private static void OnNightPaletteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not FrameworkElement element) return;
            // Resources is an inherited property, so reading it hands back the nearest
            // ancestor's dictionary - writing into that would recolour everything above
            // this panel. The panel therefore gets a dictionary of its own. WPF looks a
            // resource key up by walking the element tree, so every key this dictionary
            // does not define still resolves to the user's N.I.N.A. theme; off simply
            // installs an empty one. (The panel template defines no local resources of its
            // own, so replacing the dictionary here gives nothing away.)
            var palette = new ResourceDictionary();
            if ((bool)e.NewValue) {
                foreach (var entry in PlannerPalette.NightChrome) palette[entry.Key] = entry.Value;
            }
            element.Resources = palette;
        }
    }
}
