using Moq;
using NUnit.Framework;
using NINA.Profile.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Plugin.Interfaces;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;
using System.Windows;

namespace Zakfo.Unphased.Tests {
    [NonParallelizable, Apartment(ApartmentState.STA)]
    public class CompositionTests {
        private static CompositionContainer Create(Mock<ITelescopeMediator> telescope) {
            var container = new CompositionContainer(new AssemblyCatalog(typeof(Plugin).Assembly));
            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.PluginSettings).Returns(new NINA.Profile.PluginSettings());
            var profiles = new Mock<IProfileService>();
            profiles.SetupGet(p => p.ActiveProfile).Returns(profile.Object);
            container.ComposeExportedValue(profiles.Object);
            container.ComposeExportedValue(telescope.Object);
            container.ComposeExportedValue(Mock.Of<IImageSaveMediator>());
            container.ComposeExportedValue(Mock.Of<IOptionsVM>());
            container.ComposeExportedValue(Mock.Of<IMessageBroker>());
            return container;
        }
        [Test]
        public async Task ComposesManifestAndPartsInSeparateHostCatalogs() {
            var telescope = new Mock<ITelescopeMediator>();
            using var manifestCatalog = Create(telescope);
            using var partsCatalog = Create(telescope);
            var manifest = manifestCatalog.GetExportedValue<IPluginManifest>();
            var panels = partsCatalog.GetExportedValues<IDockableVM>().ToList();
            Assert.That(panels, Is.Not.Empty);
            await manifest.Initialize();
            await manifest.Teardown();
        }
        [Test]
        public void ExportedResourceDictionariesAndTemplatesConstruct() {
            // NINA drag/drop behaviors require an Application and a main window.
            if (Application.Current == null) new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var grid = new System.Windows.Controls.Grid();
            Application.Current.MainWindow = new Window { Content = grid };
            NameScope.SetNameScope(Application.Current.MainWindow, new NameScope());
            Application.Current.MainWindow.RegisterName("RootGrid", grid);
            foreach (var name in new[] { "ProfileService", "SVGDictionary", "Brushes", "Converters" }) {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary {
                    Source = new Uri($"/NINA.WPF.Base;component/Resources/StaticResources/{name}.xaml", UriKind.Relative)
                });
            }
            foreach (var type in typeof(Plugin).Assembly.GetTypes().Where(t => typeof(ResourceDictionary).IsAssignableFrom(t))) {
                var dictionary = (ResourceDictionary)Activator.CreateInstance(type);
                foreach (var key in dictionary.Keys) {
                    if (dictionary[key] is not DataTemplate template) continue;
                    var content = template.LoadContent();
                    Assert.That(content, Is.Not.Null, type.FullName);
                    // The panel root itself has to carry the keyboard scope: the arrow keys
                    // are handled there, so a template that forgets it puts Up/Down back in
                    // the hands of WPF's directional navigation (round 7, item 4).
                    if (key.ToString().EndsWith("_Dockable", StringComparison.Ordinal))
                        Assert.That(
                            Zakfo.Unphased.Features.planner.PlannerViewGlue.GetKeyboardScope((DependencyObject)content),
                            "the panel root must carry the keyboard scope, or Up/Down go back to " +
                            "directional navigation instead of the scrub: " + key);
                }
            }
        }
    }
}
