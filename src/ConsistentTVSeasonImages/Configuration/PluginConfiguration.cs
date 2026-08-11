using MediaBrowser.Model.Plugins;

namespace ConsistentTVSeasonImages.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string TmdbApiKey { get; set; } = string.Empty;
        public string TvdbApiKey { get; set; } = string.Empty;
        public string TvdbPin { get; set; } = string.Empty;
    }
}
