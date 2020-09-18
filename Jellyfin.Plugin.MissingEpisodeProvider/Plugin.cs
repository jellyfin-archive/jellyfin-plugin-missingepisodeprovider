using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MissingEpisodeProvider.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MissingEpisodeProvider
{
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public static string MetadataProviderName = "Missing Episode Fetcher";
        public override string Name => "MissingEpisodeProvider";

        public override Guid Id => Guid.Parse("BC785400-8541-47CB-B8E8-2E437848C87A");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }
    }
}
