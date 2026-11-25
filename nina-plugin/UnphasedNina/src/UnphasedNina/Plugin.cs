using NINA.Plugin;
using NINA.Plugin.Interfaces;
using System.ComponentModel.Composition;

namespace Zakfo.Unphased {
    [Export(typeof(IPluginManifest))]
    public class Plugin : PluginBase {
        [Import]
        public Features.settings.PluginSettings Settings { get; set; }

        public override Task Initialize() {
            Settings.Initialize();
            return Task.CompletedTask;
        }

        public override Task Teardown() {
            Settings.Dispose();
            return Task.CompletedTask;
        }
    }
}
