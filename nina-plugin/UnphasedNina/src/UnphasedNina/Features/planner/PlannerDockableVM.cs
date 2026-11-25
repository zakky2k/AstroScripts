using CommunityToolkit.Mvvm.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System.ComponentModel.Composition;
using System.Windows.Input;
using Zakfo.Unphased.Features.settings;

namespace Zakfo.Unphased.Features.planner {
    /// <summary>
    /// The plugin's main page: a dockable panel showing the night planner
    /// chart. Everything lives inside this panel, so the plugin never touches
    /// other N.I.N.A. UI elements.
    /// </summary>
    [Export(typeof(NINA.Equipment.Interfaces.ViewModel.IDockableVM))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class PlannerDockableVM : DockableVM {
        public const string ContentIdKey = "Zakfo.Unphased.Planner";

        private readonly PluginSettings settings;
        private readonly ITelescopeMediator telescope;

        private NightPlannerVM planner;
        private IRelayCommand prev, next, tonight, refresh, scrubBack, scrubForward, scrubClear, zoomOut, zoomIn, toggleNight;

        [ImportingConstructor]
        public PlannerDockableVM(IProfileService profileService, PluginSettings settings, ITelescopeMediator telescope)
            : base(profileService) {
            this.settings = settings;
            this.telescope = telescope;
            Title = "Unphased Night Planner";
            CanClose = true;
            // The night mode switch also exists in the plugin options, so the button's
            // caption has to follow the setting however it was changed.
            settings.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(PluginSettings.DarkMode))
                    RaisePropertyChanged(nameof(NightLabel));
            };
        }

        /// <summary>
        /// Lazily built because the effective site depends on the active
        /// profile, which is not ready at MEF construction time.
        /// </summary>
        public NightPlannerVM Planner => planner ??= new NightPlannerVM(settings);

        public IRelayCommand PrevNightCommand => prev ??= new RelayCommand(() => Planner.PrevNight(), () => true);
        public IRelayCommand NextNightCommand => next ??= new RelayCommand(() => Planner.NextNight(), () => true);
        public IRelayCommand TonightCommand => tonight ??= new RelayCommand(() => Planner.Tonight(), () => true);
        public IRelayCommand RefreshCommand => refresh ??= new RelayCommand(() => Planner.RequestRecompute(), () => true);

        /// <summary>
        /// Hour-by-hour scrubbing of the chart. Dragging, the wheel and the arrow keys
        /// work on the chart itself; these buttons are the same control for people who
        /// would rather click, and the only way in when the panel is too small to
        /// pointer at.
        /// </summary>
        public IRelayCommand ScrubBackCommand => scrubBack ??= new RelayCommand(() => Planner.StepScrub(-1), () => true);
        public IRelayCommand ScrubForwardCommand => scrubForward ??= new RelayCommand(() => Planner.StepScrub(1), () => true);
        public IRelayCommand ScrubClearCommand => scrubClear ??= new RelayCommand(() => Planner.ClearScrub(), () => true);

        /// <summary>
        /// Chart frame length, one night (noon to noon, like the desktop planner) up to
        /// a week. The panel width no longer decides it: on a wide monitor the chart
        /// showed two nights without being asked, and one night is the comparison people
        /// actually want first. The buttons also bind IsEnabled to the planner directly,
        /// because a click here did not reliably re-run CanExecute in a dockable pane -
        /// the InvalidateRequerySuggested call is the belt, the binding is the braces.
        /// </summary>
        public IRelayCommand ZoomOutCommand => zoomOut ??= new RelayCommand(
            () => { Planner.ZoomOut(); CommandManager.InvalidateRequerySuggested(); },
            () => Planner.CanZoomOut);

        public IRelayCommand ZoomInCommand => zoomIn ??= new RelayCommand(
            () => { Planner.ZoomIn(); CommandManager.InvalidateRequerySuggested(); },
            () => Planner.CanZoomIn);

        /// <summary>
        /// The panel's night button. It flips the same setting as the checkbox in the
        /// plugin options, and it changes only what the plugin paints: the chart through
        /// PlannerPalette, the panel chrome through brushes this panel puts in its own
        /// resources. N.I.N.A.'s theme is never touched, so the rest of the application -
        /// including the other tabs - keeps the user's colours.
        /// </summary>
        public string NightLabel => settings.DarkMode ? "night: on" : "night: off";

        public IRelayCommand ToggleNightModeCommand => toggleNight ??= new RelayCommand(
            () => settings.DarkMode = !settings.DarkMode, () => true);
    }
}

