using IPA;
using IPA.Config;
using IPA.Config.Stores;
using SiraUtil.Zenject;
using TTVStreamersKit.Configuration;
using TTVStreamersKit.Installers;
using TTVStreamersKit.Utilities;
using IPALogger = IPA.Logging.Logger;

namespace TTVStreamersKit
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static IPALogger Log { get; private set; }
        internal static PluginConfig Config { get; private set; }
        internal static string Version { get; private set; } = "";

        [Init]
        public void Init(IPALogger logger, Config config, Zenjector zenjector)
        {
            Log = logger;
            Version = typeof(Plugin).Assembly.GetName().Version.ToString(3);
            Config = config.Generated<PluginConfig>();
            Localization.Apply(Config.Language);
            zenjector.Install(Location.App, container => container.BindInstance(Config).AsSingle());
            zenjector.Install<AppInstaller>(Location.App);
            zenjector.Install<MenuInstaller>(Location.Menu);
        }
    }
}
