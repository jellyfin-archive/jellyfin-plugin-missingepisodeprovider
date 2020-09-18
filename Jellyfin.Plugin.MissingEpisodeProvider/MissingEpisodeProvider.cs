using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;
using TvDbSharper;
using TvDbSharper.Dto;
using Series = MediaBrowser.Controller.Entities.TV.Series;

namespace Jellyfin.Plugin.MissingEpisodeProvider
{
    public class MissingEpisodeProvider : IServerEntryPoint
    {
        // Jellyfin's API key
        private const string _tvdbApiKey = "OG4V3YJ3FAP7FP2K";

        private readonly IProviderManager _providerManager;
        private readonly ILocalizationManager _localization;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<MissingEpisodeProvider> _logger;
        private TvDbClient _tvDbClient;
        // TODO
        private DateTime _tokenCreatedAt;

        public MissingEpisodeProvider(
            IProviderManager providerManager,
            ILocalizationManager localization,
            ILibraryManager libraryManager,
            ILogger<MissingEpisodeProvider> logger)
        {
            _providerManager = providerManager;
            _localization = localization;
            _libraryManager = libraryManager;
            _logger = logger;

            _tvDbClient = new TvDbClient();
        }

        public Task RunAsync()
        {
            _providerManager.RefreshCompleted += OnProviderManagerRefreshComplete;
            _libraryManager.ItemUpdated += OnLibraryManagerItemUpdated;
            _libraryManager.ItemRemoved += OnLibraryManagerItemRemoved;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _providerManager.RefreshCompleted -= OnProviderManagerRefreshComplete;
            _libraryManager.ItemUpdated -= OnLibraryManagerItemUpdated;
            _libraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;
        }

        private bool IsEnabledForLibrary(BaseItem item)
        {
            Series series = item switch
            {
                Episode episode => episode.Series,
                Season season => season.Series,
                _ => item as Series
            };

            if (series == null)
            {
                return false;
            }

            var libraryOptions = _libraryManager.GetLibraryOptions(series);
            return series.IsMetadataFetcherEnabled(libraryOptions, Plugin.MetadataProviderName);
        }

        // TODO use the new async events when provider manager is updated
        private void OnProviderManagerRefreshComplete(object sender, GenericEventArgs<BaseItem> genericEventArgs)
        {
            if (!IsEnabledForLibrary(genericEventArgs.Argument))
            {
                return;
            }

            if (genericEventArgs.Argument is Series series)
            {
                HandleSeries(series).GetAwaiter().GetResult();
            }

            if (genericEventArgs.Argument is Season season)
            {
                HandleSeason(season).GetAwaiter().GetResult();
            }
        }

        private async Task HandleSeries(Series series)
        {
            var tvdbId = Convert.ToInt32(series.GetProviderId(MetadataProvider.Tvdb));
            if (tvdbId == 0)
            {
                return;
            }

            var children = series.GetRecursiveChildren();
            var existingSeasons = new List<Season>();
            var existingEpisodes = new Dictionary<int, List<Episode>>();
            for (var i = 0; i < children.Count; i++)
            {
                switch (children[i])
                {
                    case Season season:
                        if (season.IndexNumber.HasValue)
                        {
                            existingSeasons.Add(season);
                        }

                        break;
                    case Episode episode:
                        var seasonNumber = episode.ParentIndexNumber ?? 1;
                        if (!existingEpisodes.ContainsKey(seasonNumber))
                        {
                            existingEpisodes[seasonNumber] = new List<Episode>();
                        }

                        existingEpisodes[seasonNumber].Add(episode);
                        break;
                }
            }

            var allEpisodes = await GetAllEpisodes(tvdbId, series.GetPreferredMetadataLanguage()).ConfigureAwait(false);
            var allSeasons = allEpisodes
                .Where(ep => ep.AiredSeason.HasValue)
                .Select(ep => ep.AiredSeason.Value)
                .Distinct()
                .ToList();

            // Add missing seasons
            var newSeasons = AddMissingSeasons(series, existingSeasons, allSeasons);
            AddMissingEpisodes(existingEpisodes, allEpisodes, existingSeasons.Concat(newSeasons).ToList());
        }

        private async Task HandleSeason(Season season)
        {
            var tvdbId = Convert.ToInt32(season.Series?.GetProviderId(MetadataProvider.Tvdb));
            if (tvdbId == 0)
            {
                return;
            }

            var query = new EpisodeQuery
            {
                AiredSeason = season.IndexNumber
            };
            var allEpisodes = await GetAllEpisodes(tvdbId, season.GetPreferredMetadataLanguage(), query).ConfigureAwait(false);

            var existingEpisodes = season.Children.OfType<Episode>().ToList();

            for (var i = 0; i < allEpisodes.Count; i++)
            {
                var episode = allEpisodes[i];
                if (EpisodeExists(episode, existingEpisodes))
                {
                    continue;
                }

                AddVirtualEpisode(episode, season);
            }
        }

        private void OnLibraryManagerItemUpdated(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            // Only interested in real Season and Episode items
            if (itemChangeEventArgs.Item.IsVirtualItem
                || !(itemChangeEventArgs.Item is Season || itemChangeEventArgs.Item is Episode))
            {
                return;
            }

            if (!IsEnabledForLibrary(itemChangeEventArgs.Item))
            {
                return;
            }

            var indexNumber = itemChangeEventArgs.Item.IndexNumber;

            // If the item is an Episode, filter on ParentIndexNumber as well (season number)
            int? parentIndexNumber = null;
            if (itemChangeEventArgs.Item is Episode)
            {
                parentIndexNumber = itemChangeEventArgs.Item.ParentIndexNumber;
            }

            var query = new InternalItemsQuery
            {
                IsVirtualItem = true,
                IndexNumber = indexNumber,
                ParentIndexNumber = parentIndexNumber,
                IncludeItemTypes = new []{ itemChangeEventArgs.Item.GetType().Name },
                Parent = itemChangeEventArgs.Parent,
                GroupByPresentationUniqueKey = false,
                DtoOptions = new DtoOptions(true)
            };

            var existingVirtualItems = _libraryManager.GetItemList(query);

            var deleteOptions = new DeleteOptions
            {
                DeleteFileLocation = true
            };

            // Remove the virtual season/episode that matches the newly updated item
            for (var i = 0; i < existingVirtualItems.Count; i++)
            {
                _libraryManager.DeleteItem(existingVirtualItems[i], deleteOptions);
            }
        }

        // TODO use async events
        private void OnLibraryManagerItemRemoved(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            // No action needed if the item is virtual
            if (itemChangeEventArgs.Item.IsVirtualItem || !IsEnabledForLibrary(itemChangeEventArgs.Item))
            {
                return;
            }

            // Create a new virtual season if the real one was deleted.
            // Similarly, create a new virtual episode if the real one was deleted.
            if (itemChangeEventArgs.Item is Season season)
            {
                var newSeason = AddVirtualSeason(season.IndexNumber!.Value, season.Series);
                HandleSeason(newSeason).GetAwaiter().GetResult();
            }
            else if (itemChangeEventArgs.Item is Episode episode)
            {
                var tvdbId = Convert.ToInt32(episode.Series?.GetProviderId(MetadataProvider.Tvdb));
                if (tvdbId == 0)
                {
                    return;
                }

                var query = new EpisodeQuery
                {
                    AiredSeason = episode.ParentIndexNumber,
                    AiredEpisode = episode.IndexNumber
                };
                var episodeRecords = GetAllEpisodes(tvdbId, episode.GetPreferredMetadataLanguage(), query).GetAwaiter().GetResult();

                AddVirtualEpisode(episodeRecords.FirstOrDefault(), episode.Season);
            }
        }

        private async Task<IReadOnlyList<EpisodeRecord>> GetAllEpisodes(int tvdbId, string acceptedLanguage, EpisodeQuery episodeQuery = null)
        {
            if (string.IsNullOrEmpty(_tvDbClient.Authentication.Token)
                || DateTime.Now.AddHours(-24) >= _tokenCreatedAt)
            {
                await _tvDbClient.Authentication.AuthenticateAsync(_tvdbApiKey).ConfigureAwait(false);
                _tokenCreatedAt = DateTime.Now;
            }

            _tvDbClient.AcceptedLanguage = acceptedLanguage;

            // Fetch all episodes for the series
            var allEpisodes = new List<EpisodeRecord>();
            var page = 1;
            while (true)
            {
                episodeQuery ??= new EpisodeQuery();
                var episodes = await _tvDbClient.Series.GetEpisodesAsync(tvdbId, page, episodeQuery).ConfigureAwait(false);
                allEpisodes.AddRange(episodes.Data);
                if (!episodes.Links.Next.HasValue)
                {
                    break;
                }

                page = episodes.Links.Next.Value;
            }

            return allEpisodes;
        }

        private IEnumerable<Season> AddMissingSeasons(Series series, List<Season> existingSeasons, IReadOnlyList<int> allSeasons)
        {
            var missingSeasons = allSeasons.Except(existingSeasons.Select(s => s.IndexNumber!.Value)).ToList();
            for (var i = 0; i < missingSeasons.Count; i++)
            {
                var season = missingSeasons[i];
                yield return AddVirtualSeason(season, series);
            }
        }

        private void AddMissingEpisodes(
            Dictionary<int, List<Episode>> existingEpisodes,
            IReadOnlyList<EpisodeRecord> allEpisodeRecords,
            IReadOnlyList<Season> existingSeasons)
        {
            for (var i = 0; i < allEpisodeRecords.Count; i++)
            {
                var episodeRecord = allEpisodeRecords[i];
                // tvdb has a lot of bad data?
                if (!IsValidEpisode(episodeRecord))
                {
                    continue;
                }

                // skip if it exists already
                if (existingEpisodes.TryGetValue(episodeRecord.AiredSeason!.Value, out var episodes)
                    && EpisodeExists(episodeRecord, episodes))
                {
                    continue;
                }

                var existingSeason = existingSeasons.First(season => season.IndexNumber.HasValue && season.IndexNumber.Value == episodeRecord.AiredSeason);

                AddVirtualEpisode(episodeRecord, existingSeason);
            }
        }

        private Season AddVirtualSeason(int season, Series series)
        {
            string seasonName;
            if (season == 0)
            {
                seasonName = _libraryManager.GetLibraryOptions(series).SeasonZeroDisplayName;
            }
            else
            {
                seasonName = string.Format(
                    _localization.GetLocalizedString("NameSeasonNumber"),
                    season.ToString(CultureInfo.InvariantCulture));
            }

            _logger.LogInformation("Creating Season {SeasonName} entry for {SeriesName}", seasonName, series.Name);

            var newSeason = new Season
            {
                Name = seasonName,
                IndexNumber = season,
                Id = _libraryManager.GetNewItemId(
                    series.Id + season.ToString(CultureInfo.InvariantCulture) + seasonName,
                    typeof(Season)),
                IsVirtualItem = true,
                SeriesId = series.Id,
                SeriesName = series.Name,
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey()
            };

            series.AddChild(newSeason, CancellationToken.None);

            return newSeason;
        }

        private void AddVirtualEpisode(EpisodeRecord episode, Season season)
        {
            // tvdb has a lot of bad data?
            if (!IsValidEpisode(episode) || season == null)
            {
                return;
            }

            // Put as much metadata into it as possible
            DateTime.TryParse(episode.FirstAired, out var premiereDate);
            var newEpisode = new Episode
            {
                Name = episode.EpisodeName,
                IndexNumber = episode.AiredEpisodeNumber!.Value,
                ParentIndexNumber = episode.AiredSeason!.Value,
                Id = _libraryManager.GetNewItemId(
                    season.Series.Id + episode.AiredSeason.Value.ToString(CultureInfo.InvariantCulture) + "Episode " + episode.AiredEpisodeNumber,
                    typeof(Episode)),
                IsVirtualItem = true,
                SeasonId = season.Id,
                SeriesId = season.Series.Id,
                AirsBeforeEpisodeNumber = episode.AirsBeforeEpisode,
                AirsAfterSeasonNumber = episode.AirsAfterSeason,
                AirsBeforeSeasonNumber = episode.AirsBeforeSeason,
                Overview = episode.Overview,
                CommunityRating = (float?)episode.SiteRating,
                OfficialRating = episode.ContentRating,
                PremiereDate = premiereDate,
                SeriesName = season.Series.Name,
                SeriesPresentationUniqueKey = season.SeriesPresentationUniqueKey,
                SeasonName = season.Name,
                DateLastSaved = DateTime.UtcNow
            };
            newEpisode.PresentationUniqueKey = newEpisode.GetPresentationUniqueKey();
            newEpisode.SetProviderId(MetadataProvider.Tvdb, episode.Id.ToString(CultureInfo.InvariantCulture));

            _logger.LogInformation(
                "Creating virtual episode {0} {1}x{2}",
                season.Series.Name,
                episode.AiredSeason,
                episode.AiredEpisodeNumber);

            season.AddChild(newEpisode, CancellationToken.None);
        }

        private static bool IsValidEpisode(EpisodeRecord episodeRecord)
        {
            return episodeRecord?.AiredSeason != null && episodeRecord.AiredEpisodeNumber != null;
        }

        private static bool EpisodeExists(EpisodeRecord episodeRecord, IReadOnlyList<Episode> existingEpisodes)
        {
            return existingEpisodes.Any(ep => ep.ContainsEpisodeNumber(episodeRecord.AiredEpisodeNumber!.Value) && ep.ParentIndexNumber == episodeRecord.AiredSeason);
        }
    }
}
