using ConsistentTVSeasonImages.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;

namespace ConsistentTVSeasonImages
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasThumbImage, IHasWebPages
    {
        public Plugin(IServerApplicationHost host, ILogManager logs)
            : base(host.Resolve<IApplicationPaths>(), host.Resolve<IXmlSerializer>())
        {
            Instance = this;
            Logger = logs.GetLogger(Name);
            Logger.Debug("Plugin {0} version {1} initialized. Assembly: {2}", Name, GetType().Assembly.GetName().Version, GetType().Assembly.FullName);
        }

        public static Plugin Instance { get; private set; }
        public static ILogger Logger { get; private set; }
        public override string Description => "Discovers, compares and applies consistent poster and banner artwork across TV seasons.";
        public override Guid Id => new Guid("19272eb3-a556-4ae3-80b2-ce78f8ce7958");
        public override string Name => "Consistent TV Season Images";
        public ImageFormat ThumbImageFormat => ImageFormat.Png;
        public Stream GetThumbImage() => GetType().Assembly.GetManifestResourceStream(GetType().Namespace + ".thumb.png");

        public IEnumerable<PluginPageInfo> GetPages()
        {
            Logger.Debug("Registering web resources ProviderImageHelper and ProviderImageHelperJs.");
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "ProviderImageHelper",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.ProviderImageHelper.html",
                    EnableInMainMenu = true,
                    MenuIcon = "compare"
                },
                new PluginPageInfo
                {
                    Name = "ProviderImageHelperJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Web.ProviderImageHelper.js"
                }
            };
        }
    }
}
