
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using ConsistentTVSeasonImages.UI.Config;
using ConsistentTVSeasonImages.UIBaseClasses.Store;

namespace ConsistentTVSeasonImages.Storage
{
    public class BasicsOptionsStore : SimpleFileStore<ConfigUI>
    {
        public BasicsOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        : base(applicationHost, logger, pluginFullName)
        {
        }
    }
}
