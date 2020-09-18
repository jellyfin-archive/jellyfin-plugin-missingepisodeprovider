using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.MissingEpisodeProvider
{
    /// <summary>
    /// The metadata provider class for allowing Library-based configuration.
    /// </summary>
    public class RemoteProviderStub : IMetadataProvider<Series>, IRemoteMetadataProvider, IHasOrder
    {
        public string Name => Plugin.MetadataProviderName;

        // Just put it last
        public int Order => 999;
    }
}
