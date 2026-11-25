using CommunityToolkit.Mvvm.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System.ComponentModel.Composition;
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
        private IRelayCommand prev, next, tonight, refresh;

        [ImportingConstructor]
        public PlannerDockableVM(IProfileService profileService, PluginSettings settings, ITelescopeMediator telescope)
            : base(profileService) {
            this.settings = settings;
            this.telescope = telescope;
            Title = "Unphased Night Planner";
            CanClose = true;
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
    }
}

