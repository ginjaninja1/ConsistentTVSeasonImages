using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Serialization;

namespace ConsistentTVSeasonImages.Services
{
    [Route("/ProviderImageHelper/Discover", "GET")]
    [Authenticated(Roles = "admin")]
    public sealed class DiscoverRequest : IReturn<DiscoverResult> { public string Filter { get; set; } public string Search { get; set; } public string AvailableType { get; set; } public int? StartIndex { get; set; } public int? Limit { get; set; } }
    [Route("/ProviderImageHelper/Show", "GET")]
    [Authenticated(Roles = "admin")]
    public sealed class ShowRequest : IReturn<ShowResult> { public string SeriesId { get; set; } }
    [Route("/ProviderImageHelper/Fetch", "GET")]
    [Authenticated(Roles = "admin")]
    public sealed class FetchRequest : IReturn<FetchResult> { public string SeriesId { get; set; } public string SeasonId { get; set; } public bool IncludeAllLanguages { get; set; } }
    [Route("/ProviderImageHelper/Apply", "POST")]
    [Authenticated(Roles = "admin")]
    public sealed class ApplyRequest : IReturn<ApplyResult> { public string SeasonId { get; set; } public string ImageType { get; set; } public string ImageUrl { get; set; } public string SourceItemId { get; set; } }
    [Route("/ProviderImageHelper/Image", "DELETE")]
    [Authenticated(Roles = "admin")]
    public sealed class RemoveImageRequest : IReturn<ApplyResult> { public string SeasonId { get; set; } public string ImageType { get; set; } }
    [Route("/ProviderImageHelper/Cache", "DELETE")]
    [Authenticated(Roles = "admin")]
    public sealed class ClearCacheRequest : IReturn<ClearCacheResult> { }
    [Route("/ProviderImageHelper/Ignore", "POST")]
    [Authenticated(Roles = "admin")]
    public sealed class IgnoreRequest : IReturn<IgnoreResult> { public string SeriesId { get; set; } public bool Ignored { get; set; } }
    [Route("/ProviderImageHelper/Availability", "GET")]
    [Authenticated(Roles = "admin")]
    public sealed class AvailabilityRequest : IReturn<AvailabilityStatus> { }
    [Route("/ProviderImageHelper/Settings", "GET")]
    [Route("/ProviderImageHelper/Settings", "POST")]
    [Authenticated(Roles = "admin")]
    public sealed class SettingsRequest : IReturn<SettingsResult> { public string NotifyWhenAvailable { get; set; } }

    public sealed class ShowResult { public string Id { get; set; } public string Name { get; set; } public bool Ignored { get; set; } public bool Available { get; set; } public bool AvailablePoster { get; set; } public bool AvailableBanner { get; set; } public bool MissingPoster { get; set; } public bool MissingPosterIncludingSpecials { get; set; } public bool MissingBanner { get; set; } public bool MissingBannerIncludingSpecials { get; set; } public List<SeasonSummary> Seasons { get; set; } }
    public sealed class DiscoverResult { public List<ShowResult> Items { get; set; } public int TotalRecordCount { get; set; } public int StartIndex { get; set; } public int Limit { get; set; } }
    public sealed class SeasonSummary { public string Id { get; set; } public string Name { get; set; } public int? Number { get; set; } public bool CurrentPoster { get; set; } public bool CurrentBanner { get; set; } public string CurrentPosterTag { get; set; } public string CurrentBannerTag { get; set; } }
    public sealed class ImageResult { public string Url { get; set; } public string ThumbnailUrl { get; set; } public string Type { get; set; } public string Provider { get; set; } public int? Width { get; set; } public int? Height { get; set; } }
    public sealed class ProviderDiagnostic { public string Provider { get; set; } public string Status { get; set; } public string Message { get; set; } public bool SupportsPosters { get; set; } public bool SupportsBanners { get; set; } public int PosterCount { get; set; } public int BannerCount { get; set; } public int Attempts { get; set; } public bool UsedStaleCache { get; set; } public double? StaleCacheAgeHours { get; set; } public int? RetryAfterSeconds { get; set; } }
    public sealed class SeasonResult { public string Id { get; set; } public string Name { get; set; } public int? Number { get; set; } public string CurrentPoster { get; set; } public string CurrentBanner { get; set; } public string CurrentPosterItemId { get; set; } public string CurrentBannerItemId { get; set; } public string CurrentPosterTag { get; set; } public string CurrentBannerTag { get; set; } public List<ImageResult> Posters { get; set; } public List<ImageResult> Banners { get; set; } public List<ProviderDiagnostic> ProviderDiagnostics { get; set; } }
    public sealed class FetchResult { public string SeriesId { get; set; } public string Name { get; set; } public string CorrelationId { get; set; } public List<SeasonResult> Seasons { get; set; } }
    public sealed class ApplyResult { public bool Success { get; set; } public string SeasonId { get; set; } public string ImageType { get; set; } }
    public sealed class ClearCacheResult { public bool Success { get; set; } public int FilesRemoved { get; set; } }
    public sealed class IgnoreResult { public bool Success { get; set; } public bool Ignored { get; set; } }
    public sealed class AvailabilityStatus { public bool Ready { get; set; } public bool Building { get; set; } public double Progress { get; set; } public int AvailableShows { get; set; } public int AvailablePosterShows { get; set; } public int AvailableBannerShows { get; set; } public DateTime? CreatedUtc { get; set; } }
    public sealed class SettingsResult { public string NotifyWhenAvailable { get; set; } }
    public sealed class AvailabilityCache { public int Version { get; set; } public DateTime CreatedUtc { get; set; } public List<string> SeriesIds { get; set; } = new List<string>(); public List<AvailabilityOpportunity> Entries { get; set; } = new List<AvailabilityOpportunity>(); }
    public sealed class AvailabilitySeason { public string SeasonId { get; set; } public bool Poster { get; set; } public bool Banner { get; set; } }
    public sealed class AvailabilityOpportunity { public string SeriesId { get; set; } public string SeriesName { get; set; } public bool Poster { get; set; } public bool Banner { get; set; } public DateTime NextRefreshUtc { get; set; } public List<AvailabilitySeason> Seasons { get; set; } = new List<AvailabilitySeason>(); }
    public sealed class AvailabilityWork { public int CacheVersion { get; set; } public DateTime StartedUtc { get; set; } public bool FirstBuild { get; set; } public List<string> CompletedSeriesIds { get; set; } = new List<string>(); public List<AvailabilityOpportunity> Opportunities { get; set; } = new List<AvailabilityOpportunity>(); }
    public sealed class ProviderImageCacheEntry { public DateTime CreatedUtc { get; set; } public List<CachedRemoteImage> Images { get; set; } }
    public sealed class CachedRemoteImage { public string ProviderName { get; set; } public string Url { get; set; } public string ThumbnailUrl { get; set; } public int? Height { get; set; } public int? Width { get; set; } public string Language { get; set; } public string DisplayLanguage { get; set; } public ImageType Type { get; set; } }

    public sealed class ProviderImageHelperService : IService
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
        private static readonly TimeSpan AvailabilityLifetime = TimeSpan.FromDays(14);
        private static readonly object AvailabilitySync = new object();
        private static bool availabilityBuilding;
        private static double availabilityProgress;
        private static readonly SemaphoreSlim CacheLock = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentDictionary<string, ProviderGate> ProviderGates = new ConcurrentDictionary<string, ProviderGate>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ProviderMinimumInterval = TimeSpan.FromMilliseconds(350);
        private const int MaximumProviderAttempts = 3;
        private const int AvailabilityCacheVersion = 2;
        private readonly ILibraryManager library; private readonly IProviderManager providers; private readonly IFileSystem fileSystem; private readonly IImageProcessor imageProcessor; private readonly IServerConfigurationManager configuration; private readonly IJsonSerializer json; private readonly ILogger logger; private readonly IUserManager users; private readonly ISessionManager sessions; private readonly IActivityManager activities; private readonly string cachePath; private readonly string availabilityPath; private readonly string availabilityWorkPath;
        public ProviderImageHelperService(ILibraryManager library, IProviderManager providers, IFileSystem fileSystem, IImageProcessor imageProcessor, IServerConfigurationManager configuration, IApplicationPaths applicationPaths, IJsonSerializer json, ILogManager logs, IUserManager users, ISessionManager sessions, IActivityManager activities)
        { this.library = library; this.providers = providers; this.fileSystem = fileSystem; this.imageProcessor = imageProcessor; this.configuration = configuration; this.json = json; this.users = users; this.sessions = sessions; this.activities = activities; cachePath = Path.Combine(applicationPaths.CachePath, "consistent-tv-season-images", "provider-images"); availabilityPath = Path.Combine(applicationPaths.CachePath, "consistent-tv-season-images", "availability.json"); availabilityWorkPath = Path.Combine(applicationPaths.CachePath, "consistent-tv-season-images", "availability-work.json"); logger = Plugin.Logger ?? logs.GetLogger("Consistent TV Season Images"); }

        public object Get(DiscoverRequest request)
        {
            var filter = (request.Filter ?? "all").ToLowerInvariant(); var search = request.Search ?? string.Empty;
            var startIndex = Math.Max(0, request.StartIndex ?? 0); var limit = Math.Max(1, Math.Min(100, request.Limit ?? 50));
            logger.Debug("Discover started. Filter={0}, AvailableType={1}, Search={2}, StartIndex={3}, Limit={4}", filter, request.AvailableType ?? "both", search, startIndex, limit);
            try
            {
                var queried = library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { typeof(Series).Name }, Recursive = true, IsVirtualItem = false, HasPath = true }).OfType<Series>().ToList();
                var excluded = queried.Where(s => string.IsNullOrEmpty(s.Path) || !string.IsNullOrEmpty(s.ExternalId)).ToList();
                foreach (var item in excluded) logger.Debug("Discover excluded non-library series. Name={0}, Id={1}, Path={2}, ExternalId={3}", item.Name, item.GetClientId(), item.Path ?? "(null)", item.ExternalId ?? "(null)");
                var candidates = queried.Where(s => !string.IsNullOrEmpty(s.Path) && string.IsNullOrEmpty(s.ExternalId) && s.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                ILookup<string, Season> seasonsBySeries = null;
                if (filter != "all")
                {
                    seasonsBySeries = library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { typeof(Season).Name }, Recursive = true }).OfType<Season>()
                        .ToLookup(x => x.SeriesId.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
                }
                var ignored = new HashSet<string>((Plugin.Instance.Configuration.IgnoredSeriesIds ?? new string[0]), StringComparer.OrdinalIgnoreCase);
                var available = LoadAvailability();
                var availableType = (request.AvailableType ?? "both").ToLowerInvariant();
                var availableEntries = available != null && available.Version == AvailabilityCacheVersion ? available.Entries.ToDictionary(x => x.SeriesId, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, AvailabilityOpportunity>(StringComparer.OrdinalIgnoreCase);
                var matching = candidates.Select(s => { var showSeasons = seasonsBySeries == null ? null : seasonsBySeries[s.InternalId.ToString(CultureInfo.InvariantCulture)].ToArray(); var x = CreateShowResult(s, showSeasons, false); x.Ignored = ignored.Contains(x.Id); AvailabilityOpportunity entry; availableEntries.TryGetValue(x.Id, out entry); var live = GetLiveAvailability(entry, showSeasons); x.AvailablePoster = live.Poster; x.AvailableBanner = live.Banner; x.Available = availableType == "poster" ? x.AvailablePoster : availableType == "banner" ? x.AvailableBanner : x.AvailablePoster || x.AvailableBanner; return x; })
                    .Where(s => filter == "ignored" ? s.Ignored : !s.Ignored && (filter == "all"
                        || filter == "available" && s.Available
                        || filter == "missingposters" && s.MissingPoster
                        || filter == "missingpostersspecial" && s.MissingPosterIncludingSpecials
                        || filter == "missingbanners" && s.MissingBanner
                        || filter == "missingbannersspecial" && s.MissingBannerIncludingSpecials))
                    .OrderBy(s => s.Name).ToList();
                var items = matching.Skip(startIndex).Take(limit).ToList();
                if (filter == "available")
                {
                    logger.Info("Available discovery evaluated. RequestedType={0}, MatchedShows={1}, ReturnedShows={2}", availableType, matching.Count, items.Count);
                }
                logger.Debug("Discover completed. Queried={0}, Excluded={1}, Matched={2}, Returned={3}, StartIndex={4}", queried.Count, excluded.Count, matching.Count, items.Count, startIndex);
                return new DiscoverResult { Items = items, TotalRecordCount = matching.Count, StartIndex = startIndex, Limit = limit };
            }
            catch (Exception ex) { logger.ErrorException("Discover failed. Filter={0}, Search={1}", ex, filter, search); throw; }
        }

        public object Get(AvailabilityRequest request)
        {
            var cache = LoadAvailability(); var entries = cache?.Entries ?? new List<AvailabilityOpportunity>();
            var seasons = library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { typeof(Season).Name }, Recursive = true }).OfType<Season>().ToLookup(x => x.SeriesId.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
            var live = cache != null && cache.Version == AvailabilityCacheVersion ? entries.Select(x => GetLiveAvailability(x, seasons[x.SeriesId].ToArray())).ToArray() : new AvailabilityFlags[0];
            var posterOnly = live.Count(x => x.Poster && !x.Banner); var bannerOnly = live.Count(x => x.Banner && !x.Poster); var both = live.Count(x => x.Poster && x.Banner);
            var posterTotal = posterOnly + both; var bannerTotal = bannerOnly + both; var either = posterOnly + bannerOnly + both;
            logger.Info("Availability cache counts. PosterOnly={0}, BannerOnly={1}, Both={2}, PosterTotal={3}, BannerTotal={4}, Either={5}, CachedShows={6}", posterOnly, bannerOnly, both, posterTotal, bannerTotal, either, entries.Count);
            lock (AvailabilitySync) return new AvailabilityStatus { Ready = cache != null && cache.Version == AvailabilityCacheVersion, Building = availabilityBuilding, Progress = availabilityProgress, AvailableShows = either, AvailablePosterShows = posterTotal, AvailableBannerShows = bannerTotal, CreatedUtc = cache?.CreatedUtc };
        }
        public object Get(SettingsRequest request) { return new SettingsResult { NotifyWhenAvailable = NormalizeNotificationPreference(Plugin.Instance.Configuration.NotifyWhenAvailable) }; }

        public object Post(SettingsRequest request)
        {
            Plugin.Instance.Configuration.NotifyWhenAvailable = NormalizeNotificationPreference(request.NotifyWhenAvailable);
            Plugin.Instance.SaveConfiguration(); logger.Info("Availability notification preference changed. Value={0}", Plugin.Instance.Configuration.NotifyWhenAvailable);
            return new SettingsResult { NotifyWhenAvailable = Plugin.Instance.Configuration.NotifyWhenAvailable };
        }

        public object Post(IgnoreRequest request)
        {
            if (library.GetItemById(request.SeriesId) as Series == null) throw new ArgumentException("Series not found.");
            var ids = new HashSet<string>(Plugin.Instance.Configuration.IgnoredSeriesIds ?? new string[0], StringComparer.OrdinalIgnoreCase);
            if (request.Ignored) ids.Add(request.SeriesId); else ids.Remove(request.SeriesId);
            Plugin.Instance.Configuration.IgnoredSeriesIds = ids.OrderBy(x => x).ToArray();
            Plugin.Instance.SaveConfiguration();
            logger.Info("Series ignored state changed. SeriesId={0}, Ignored={1}", request.SeriesId, request.Ignored);
            return new IgnoreResult { Success = true, Ignored = request.Ignored };
        }

        private AvailabilityCache LoadAvailability()
        {
            try { if (!fileSystem.FileExists(availabilityPath)) return null; using (var stream = fileSystem.OpenRead(availabilityPath)) { var value = json.DeserializeFromStream<AvailabilityCache>(stream); if (value == null) return null; value.SeriesIds = value.SeriesIds ?? new List<string>(); value.Entries = value.Entries ?? new List<AvailabilityOpportunity>(); return value; } }
            catch (Exception ex) { logger.ErrorException("Could not read availability cache.", ex); return null; }
        }

        private T LoadJson<T>(string path) where T : class
        {
            try { if (!fileSystem.FileExists(path)) return null; using (var stream = fileSystem.OpenRead(path)) return json.DeserializeFromStream<T>(stream); }
            catch (Exception ex) { logger.ErrorException("Could not read resumable cache file. Path={0}", ex, path); return null; }
        }

        private void WriteJsonAtomically<T>(string path, T value)
        {
            var temporary = path + ".tmp";
            fileSystem.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = fileSystem.GetFileStream(temporary, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.None)) json.SerializeToStream(value, stream);
            fileSystem.MoveFile(temporary, path, true);
        }

        private sealed class AvailabilityFlags { public bool Poster { get; set; } public bool Banner { get; set; } }
        private static AvailabilityFlags GetLiveAvailability(AvailabilityOpportunity entry, IEnumerable<Season> seasons)
        {
            var result = new AvailabilityFlags(); if (entry?.Seasons == null || seasons == null) return result;
            var cached = entry.Seasons.ToDictionary(x => x.SeasonId, StringComparer.OrdinalIgnoreCase);
            foreach (var season in seasons)
            {
                AvailabilitySeason inventory; if (!cached.TryGetValue(season.GetClientId(), out inventory)) continue;
                result.Poster |= inventory.Poster && !season.HasImage(ImageType.Primary, 0);
                result.Banner |= inventory.Banner && !season.HasImage(ImageType.Banner, 0);
            }
            return result;
        }

        private static bool HasNewLiveProviderType(AvailabilityOpportunity current, AvailabilityOpportunity previous, IEnumerable<Season> seasons, bool poster)
        {
            var old = (previous?.Seasons ?? new List<AvailabilitySeason>()).ToDictionary(x => x.SeasonId, StringComparer.OrdinalIgnoreCase);
            var now = (current?.Seasons ?? new List<AvailabilitySeason>()).ToDictionary(x => x.SeasonId, StringComparer.OrdinalIgnoreCase);
            foreach (var season in seasons)
            {
                AvailabilitySeason currentSeason; AvailabilitySeason previousSeason; if (!now.TryGetValue(season.GetClientId(), out currentSeason)) continue; old.TryGetValue(season.GetClientId(), out previousSeason);
                if (poster && currentSeason.Poster && previousSeason?.Poster != true && !season.HasImage(ImageType.Primary, 0)) return true;
                if (!poster && currentSeason.Banner && previousSeason?.Banner != true && !season.HasImage(ImageType.Banner, 0)) return true;
            }
            return false;
        }

        public async Task BuildAvailabilityCache(CancellationToken cancellationToken, IProgress<double> progress)
        {
            lock (AvailabilitySync) { if (availabilityBuilding) return; availabilityBuilding = true; availabilityProgress = 0; }
            try
            {
                var priorCache = LoadAvailability();
                var series = library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { typeof(Series).Name }, Recursive = true, IsVirtualItem = false, HasPath = true }).OfType<Series>().Where(x => string.IsNullOrEmpty(x.ExternalId)).ToArray();
                var work = LoadJson<AvailabilityWork>(availabilityWorkPath);
                if (work != null && work.CacheVersion != AvailabilityCacheVersion) work = null;
                if (work == null)
                {
                    var firstBuild = priorCache == null || priorCache.Version != AvailabilityCacheVersion;
                    work = new AvailabilityWork { CacheVersion = AvailabilityCacheVersion, StartedUtc = DateTime.UtcNow, FirstBuild = firstBuild };
                    if (!firstBuild)
                    {
                        foreach (var entry in priorCache.Entries.Where(x => x.NextRefreshUtc > DateTime.UtcNow))
                        {
                            work.CompletedSeriesIds.Add(entry.SeriesId); work.Opportunities.Add(entry);
                        }
                    }
                }
                work.CompletedSeriesIds = work.CompletedSeriesIds ?? new List<string>(); work.Opportunities = work.Opportunities ?? new List<AvailabilityOpportunity>();
                var currentIds = new HashSet<string>(series.Select(x => x.GetClientId()), StringComparer.OrdinalIgnoreCase);
                work.CompletedSeriesIds = work.CompletedSeriesIds.Where(currentIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                work.Opportunities = work.Opportunities.Where(x => currentIds.Contains(x.SeriesId)).GroupBy(x => x.SeriesId, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToList();
                var representedIds = new HashSet<string>(work.Opportunities.Select(x => x.SeriesId), StringComparer.OrdinalIgnoreCase);
                work.CompletedSeriesIds = work.CompletedSeriesIds.Where(representedIds.Contains).ToList();
                var completed = new HashSet<string>(work.CompletedSeriesIds, StringComparer.OrdinalIgnoreCase); var failures = new List<string>();
                logger.Info("Availability cache build started or resumed. Shows={0}, AlreadyCompleted={1}", series.Length, completed.Count);
                for (var i = 0; i < series.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested(); var id = series[i].GetClientId();
                    if (completed.Contains(id)) { var resumedPct = series.Length == 0 ? 100 : completed.Count * 100d / series.Length; lock (AvailabilitySync) availabilityProgress = resumedPct; progress.Report(resumedPct); continue; }
                    try
                    {
                        var posterAvailable = false; var bannerAvailable = false; var seasonInventory = new List<AvailabilitySeason>();
                        foreach (var season in GetSeasons(series[i]))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var remote = await GetRemoteImages(season, false, "availability", cancellationToken).ConfigureAwait(false);
                            if (remote.Diagnostics.Any(x => !x.UsedStaleCache && (x.Status == "Paused" || x.Status == "RateLimited" || x.Status == "TimedOut" || x.Status == "Unavailable" || x.Status == "RateLimitSuspected"))) throw new IOException("A provider had a transient failure; this show will be retried.");
                            var seasonPoster = remote.Images.Any(x => x.Type == ImageType.Primary); var seasonBanner = remote.Images.Any(x => x.Type == ImageType.Banner);
                            posterAvailable |= seasonPoster; bannerAvailable |= seasonBanner;
                            seasonInventory.Add(new AvailabilitySeason { SeasonId = season.GetClientId(), Poster = seasonPoster, Banner = seasonBanner });
                        }
                        work.Opportunities.RemoveAll(x => string.Equals(x.SeriesId, id, StringComparison.OrdinalIgnoreCase));
                        var nextRefresh = work.FirstBuild ? RandomizedInitialRefreshUtc(id, work.StartedUtc) : DateTime.UtcNow.Add(AvailabilityLifetime);
                        work.Opportunities.Add(new AvailabilityOpportunity { SeriesId = id, SeriesName = series[i].Name, Poster = posterAvailable, Banner = bannerAvailable, NextRefreshUtc = nextRefresh, Seasons = seasonInventory });
                        work.CompletedSeriesIds.Add(id); completed.Add(id); WriteJsonAtomically(availabilityWorkPath, work);
                        var pct = series.Length == 0 ? 100 : completed.Count * 100d / series.Length; lock (AvailabilitySync) availabilityProgress = pct; progress.Report(pct);
                        logger.Info("Availability cache show completed. Show={0}, Available={1}, Progress={2:0.0}%", series[i].Name, posterAvailable || bannerAvailable, pct);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex) { failures.Add(id); logger.ErrorException("Availability cache show failed and will be retried. Show={0}, Id={1}", ex, series[i].Name, id); }
                }
                if (failures.Count > 0) throw new IOException("Availability scan retained its checkpoint but could not complete " + failures.Count + " show(s). They will be retried on the next run.");
                var finalCache = new AvailabilityCache { Version = AvailabilityCacheVersion, CreatedUtc = DateTime.UtcNow, Entries = work.Opportunities.ToList(), SeriesIds = work.Opportunities.Where(x => x.Poster || x.Banner).Select(x => x.SeriesId).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
                WriteJsonAtomically(availabilityPath, finalCache);
                var preference = NormalizeNotificationPreference(Plugin.Instance.Configuration.NotifyWhenAvailable);
                foreach (var opportunity in work.Opportunities)
                {
                    var previous = priorCache?.Entries?.FirstOrDefault(x => string.Equals(x.SeriesId, opportunity.SeriesId, StringComparison.OrdinalIgnoreCase));
                    var item = library.GetItemById(opportunity.SeriesId) as Series; if (item == null) continue;
                    var currentSeasons = GetSeasons(item);
                    if (HasNewLiveProviderType(opportunity, previous, currentSeasons, true)) { if (preference == "All" || preference == "Posters") await SendTransientAdminToast("New Season images available for show " + opportunity.SeriesName, cancellationToken).ConfigureAwait(false); else logger.Info("Availability notification skipped by preference. Type=Poster, Show={0}, Preference={1}", opportunity.SeriesName, preference); }
                    if (HasNewLiveProviderType(opportunity, previous, currentSeasons, false)) { if (preference == "All" || preference == "Banners") await SendTransientAdminToast("New Banner images available for show " + opportunity.SeriesName, cancellationToken).ConfigureAwait(false); else logger.Info("Availability notification skipped by preference. Type=Banner, Show={0}, Preference={1}", opportunity.SeriesName, preference); }
                }
                if (fileSystem.FileExists(availabilityWorkPath)) fileSystem.DeleteFile(availabilityWorkPath);
                CreateRunActivity("Consistent Season Images availability cache completed", "Scanned " + series.Length + " shows; " + finalCache.SeriesIds.Count + " currently have available season artwork.", LogSeverity.Info);
                logger.Info("Availability cache build completed. Shows={0}, AvailableShows={1}", series.Length, finalCache.SeriesIds.Count);
            }
            catch (OperationCanceledException) { CreateRunActivity("Consistent Season Images availability cache cancelled", "The resumable checkpoint was retained.", LogSeverity.Info); throw; }
            catch (Exception ex) { CreateRunActivity("Consistent Season Images availability cache failed", ex.Message, LogSeverity.Error); throw; }
            finally { lock (AvailabilitySync) availabilityBuilding = false; }
        }

        private static DateTime RandomizedInitialRefreshUtc(string seriesId, DateTime startedUtc)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(seriesId ?? string.Empty));
                var fraction = BitConverter.ToUInt32(bytes, 0) / (double)uint.MaxValue;
                return startedUtc.AddTicks((long)(AvailabilityLifetime.Ticks * fraction));
            }
        }

        private static string NormalizeNotificationPreference(string value)
        {
            if (string.Equals(value, "All", StringComparison.OrdinalIgnoreCase)) return "All";
            if (string.Equals(value, "Banners", StringComparison.OrdinalIgnoreCase)) return "Banners";
            return "Posters";
        }

        private async Task SendTransientAdminToast(string text, CancellationToken cancellationToken)
        {
            var administratorIds = new HashSet<long>(users.GetUserIdList(new UserQuery { IsAdministrator = true, IsDisabled = false }, cancellationToken));
            var targets = sessions.Sessions.Where(x => administratorIds.Contains(x.UserInternalId)).ToArray(); var delivered = 0;
            foreach (var session in targets)
            {
                try { await sessions.SendMessageCommand(null, session.Id, new MediaBrowser.Model.Session.MessageCommand { Header = "Consistent Season Images", Text = text, TimeoutMs = 3000 }, cancellationToken).ConfigureAwait(false); delivered++; }
                catch (Exception ex) { logger.ErrorException("Transient admin notification failed. Session={0}, User={1}", ex, session.Id, session.UserName); }
            }
            logger.Info("Transient admin notification fired. ActiveAdminSessions={0}, Delivered={1}, Text={2}", targets.Length, delivered, text);
        }

        private void CreateRunActivity(string name, string overview, LogSeverity severity)
        {
            try { activities.Create(new ActivityLogEntry { Name = name, Overview = overview, ShortOverview = overview, Type = "ConsistentSeasonImagesAvailability", Date = DateTimeOffset.UtcNow, Severity = severity }); logger.Info("Persistent dashboard activity created. Name={0}", name); }
            catch (Exception ex) { logger.ErrorException("Persistent dashboard activity could not be created. Name={0}", ex, name); }
        }

        public object Get(ShowRequest request)
        {
            var series = library.GetItemById(request.SeriesId) as Series;
            if (series == null) throw new ArgumentException("Series not found.");
            var result = CreateShowResult(series, GetSeasons(series), true);
            result.Ignored = (Plugin.Instance.Configuration.IgnoredSeriesIds ?? new string[0]).Contains(result.Id, StringComparer.OrdinalIgnoreCase);
            logger.Debug("Show summary completed. Series={0}, Id={1}, Seasons={2}", series.Name, request.SeriesId, result.Seasons.Count);
            return result;
        }

        public async Task<object> Get(FetchRequest request)
        {
            var fetchTimer = Stopwatch.StartNew();
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 12);
            logger.Debug("Fetch started. CorrelationId={0}, SeriesId={1}", correlationId, request.SeriesId);
            try
            {
                var series = library.GetItemById(request.SeriesId) as Series; if (series == null) throw new ArgumentException("Series not found.");
                var seasons = GetSeasons(series).Where(s => string.IsNullOrEmpty(request.SeasonId) || string.Equals(s.GetClientId(), request.SeasonId, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.IndexNumber).ToArray();
                if (!string.IsNullOrEmpty(request.SeasonId) && seasons.Length == 0) throw new ArgumentException("Season not found in series.");
                logger.Debug("Fetch resolved series. Name={0}, Path={1}, Seasons={2}, ProviderIds={3}", series.Name, series.Path ?? "(null)", seasons.Length, string.Join(",", series.ProviderIds.Select(x => x.Key + "=" + x.Value)));
                LogAvailableProviders("Series", series);
                var result = new FetchResult { SeriesId = request.SeriesId, Name = series.Name, CorrelationId = correlationId, Seasons = new List<SeasonResult>() };
                var cacheHits = 0; var cacheMisses = 0; var providerMilliseconds = 0L;
                foreach (var season in seasons)
                {
                    var seasonTimer = Stopwatch.StartNew();
                    logger.Debug("Fetch season identity. Season={0}, Id={1}, SeriesId={2}, ProviderIds={3}, ImageInfos={4}", season.Name, season.GetClientId(), season.SeriesId, string.Join(",", season.ProviderIds.Select(x => x.Key + "=" + x.Value)), string.Join(";", season.ImageInfos.Select(x => x.Type + "=" + x.Path)));
                    LogAvailableProviders("Season", season);
                    var fetched = await GetRemoteImages(season, request.IncludeAllLanguages, correlationId).ConfigureAwait(false);
                    cacheHits += fetched.CacheHits; cacheMisses += fetched.CacheMisses; providerMilliseconds += fetched.ProviderMilliseconds;
                    var posters = fetched.Images.Where(x => x.Type == ImageType.Primary).ToList();
                    var banners = fetched.Images.Where(x => x.Type == ImageType.Banner).ToList();
                    var posterResults = Map(posters, ImageType.Primary); var bannerResults = Map(banners, ImageType.Banner);
                    posterResults = DistinctImages(posterResults); bannerResults = DistinctImages(bannerResults);
                    logger.Debug("Fetch season completed. Season={0}, Id={1}, HasCurrentPoster={2}, HasCurrentBanner={3}, Posters={4}, Banners={5}, ElapsedMs={6}, CacheHits={7}, CacheMisses={8}", season.Name, season.GetClientId(), season.HasImage(ImageType.Primary, 0), season.HasImage(ImageType.Banner, 0), posterResults.Count, bannerResults.Count, seasonTimer.ElapsedMilliseconds, fetched.CacheHits, fetched.CacheMisses);
                    LogProviderCounts(season, posterResults.Concat(bannerResults));
                    var seasonPoster = LocalImageUrl(season, ImageType.Primary); var seasonBanner = LocalImageUrl(season, ImageType.Banner);
                    var posterTag = ImageTag(season, ImageType.Primary); var bannerTag = ImageTag(season, ImageType.Banner);
                    logger.Debug("Current Emby image URL. Season={0}, Id={1}, Type=Primary, Url={2}", season.Name, season.GetClientId(), CurrentImageUrl(season, ImageType.Primary, posterTag) ?? "(none)");
                    logger.Debug("Current Emby image URL. Season={0}, Id={1}, Type=Banner, Url={2}", season.Name, season.GetClientId(), CurrentImageUrl(season, ImageType.Banner, bannerTag) ?? "(none)");
                    result.Seasons.Add(new SeasonResult { Id = season.GetClientId(), Name = season.Name, Number = season.IndexNumber, CurrentPoster = seasonPoster, CurrentBanner = seasonBanner, CurrentPosterItemId = seasonPoster == null ? null : season.GetClientId(), CurrentBannerItemId = seasonBanner == null ? null : season.GetClientId(), CurrentPosterTag = posterTag, CurrentBannerTag = bannerTag, Posters = posterResults, Banners = bannerResults, ProviderDiagnostics = fetched.Diagnostics });
                }
                logger.Info("Fetch completed. CorrelationId={0}, Series={1}, Seasons={2}, TotalPosters={3}, TotalBanners={4}, ElapsedMs={5}, ProviderElapsedMs={6}, CacheHits={7}, CacheMisses={8}", correlationId, series.Name, result.Seasons.Count, result.Seasons.Sum(x => x.Posters.Count), result.Seasons.Sum(x => x.Banners.Count), fetchTimer.ElapsedMilliseconds, providerMilliseconds, cacheHits, cacheMisses);
                return result;
            }
            catch (Exception ex) { logger.ErrorException("Fetch failed. CorrelationId={0}, SeriesId={1}", ex, correlationId, request.SeriesId); throw; }
        }

        public async Task<object> Post(ApplyRequest request)
        {
            logger.Debug("Apply started. SeasonId={0}, ImageType={1}, Url={2}, SourceItemId={3}", request.SeasonId, request.ImageType, request.ImageUrl ?? "(null)", request.SourceItemId ?? "(null)");
            try { var season = library.GetItemById(request.SeasonId) as Season; if (season == null) throw new ArgumentException("Season not found."); ImageType type; var name = request.ImageType == "Poster" ? "Primary" : request.ImageType; if (!Enum.TryParse(name, true, out type) || type != ImageType.Primary && type != ImageType.Banner) throw new ArgumentException("ImageType must be Poster or Banner."); var source = request.ImageUrl; if (!string.IsNullOrEmpty(request.SourceItemId)) { var sourceItem = library.GetItemById(request.SourceItemId); var sourceInfo = sourceItem?.GetImageInfo(type, 0); if (sourceInfo == null || string.IsNullOrEmpty(sourceInfo.Path) || !fileSystem.FileExists(sourceInfo.Path)) throw new ArgumentException("The selected current source image is unavailable."); source = sourceInfo.Path; logger.Debug("Apply resolved local current source. SourceItem={0}, Type={1}, Path={2}", sourceItem.Name, type, source); } else { Uri uri; if (!Uri.TryCreate(source, UriKind.Absolute, out uri) || uri.Scheme != "https" && uri.Scheme != "http") throw new ArgumentException("A valid HTTP(S) image URL is required."); } await providers.SaveImage(season, library.GetLibraryOptions(season), source, type, 0, new long[0], new DirectoryService(logger, fileSystem), true, CancellationToken.None).ConfigureAwait(false); season.UpdateToRepository(ItemUpdateType.ImageUpdate); logger.Info("Apply completed. Season={0}, Id={1}, ImageType={2}", season.Name, request.SeasonId, type); return new ApplyResult { Success = true, SeasonId = request.SeasonId, ImageType = request.ImageType }; }
            catch (Exception ex) { logger.ErrorException("Apply failed. SeasonId={0}, ImageType={1}, Url={2}", ex, request.SeasonId, request.ImageType, request.ImageUrl); throw; }
        }

        public object Delete(RemoveImageRequest request)
        {
            logger.Debug("Remove image started. SeasonId={0}, ImageType={1}", request.SeasonId, request.ImageType);
            try
            {
                var season = library.GetItemById(request.SeasonId) as Season;
                if (season == null) throw new ArgumentException("Season not found.");
                ImageType type;
                var name = request.ImageType == "Poster" ? "Primary" : request.ImageType;
                if (!Enum.TryParse(name, true, out type) || type != ImageType.Primary && type != ImageType.Banner) throw new ArgumentException("ImageType must be Poster or Banner.");
                if (!season.HasImage(type, 0)) throw new ArgumentException("The selected current image no longer exists.");
                season.DeleteImage(type, 0);
                season.UpdateToRepository(ItemUpdateType.ImageUpdate);
                logger.Info("Remove image completed. Season={0}, Id={1}, ImageType={2}", season.Name, request.SeasonId, type);
                return new ApplyResult { Success = true, SeasonId = request.SeasonId, ImageType = request.ImageType };
            }
            catch (Exception ex) { logger.ErrorException("Remove image failed. SeasonId={0}, ImageType={1}", ex, request.SeasonId, request.ImageType); throw; }
        }

        public object Delete(ClearCacheRequest request)
        {
            var removed = fileSystem.DirectoryExists(cachePath) ? fileSystem.GetFilePaths(cachePath, true).Count() : 0;
            if (fileSystem.DirectoryExists(cachePath)) fileSystem.DeleteDirectory(cachePath, true);
            logger.Info("Provider image cache cleared. Path={0}, FilesRemoved={1}", cachePath, removed);
            return new ClearCacheResult { Success = true, FilesRemoved = removed };
        }

        private ShowResult CreateShowResult(Series series, Season[] seasons, bool includeSeasons)
        {
            seasons = seasons ?? new Season[0];
            var regularSeasons = seasons.Where(x => x.IndexNumber.HasValue && x.IndexNumber.Value >= 1).ToArray();
            return new ShowResult
            {
                Id = series.GetClientId(),
                Name = series.Name,
                MissingPoster = regularSeasons.Any(x => !x.HasImage(ImageType.Primary, 0)),
                MissingPosterIncludingSpecials = seasons.Any(x => !x.HasImage(ImageType.Primary, 0)),
                MissingBanner = regularSeasons.Any(x => !x.HasImage(ImageType.Banner, 0)),
                MissingBannerIncludingSpecials = seasons.Any(x => !x.HasImage(ImageType.Banner, 0)),
                Seasons = includeSeasons ? seasons.OrderBy(x => x.IndexNumber).Select(x => new SeasonSummary { Id = x.GetClientId(), Name = x.Name, Number = x.IndexNumber, CurrentPoster = x.HasImage(ImageType.Primary, 0), CurrentBanner = x.HasImage(ImageType.Banner, 0), CurrentPosterTag = ImageTag(x, ImageType.Primary), CurrentBannerTag = ImageTag(x, ImageType.Banner) }).ToList() : null
            };
        }

        private Season[] GetSeasons(Series series) { return library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { typeof(Season).Name }, AncestorIds = new[] { series.InternalId }, Recursive = true }).OfType<Season>().ToArray(); }
        private sealed class ProviderFetchResult { public List<RemoteImageInfo> Images { get; set; } = new List<RemoteImageInfo>(); public List<ProviderDiagnostic> Diagnostics { get; set; } = new List<ProviderDiagnostic>(); public int CacheHits { get; set; } public int CacheMisses { get; set; } public long ProviderMilliseconds { get; set; } }
        private sealed class CacheReadResult { public List<RemoteImageInfo> Images { get; set; } public DateTime CreatedUtc { get; set; } public bool IsFresh { get; set; } }
        private sealed class ProviderGate { public SemaphoreSlim Lock { get; } = new SemaphoreSlim(1, 1); public DateTime NextRequestUtc { get; set; } public int ConsecutiveFailures { get; set; } public DateTime OpenUntilUtc { get; set; } }
        private sealed class ProviderCircuitOpenException : Exception { public int RetryAfterSeconds { get; } public ProviderCircuitOpenException(int seconds) : base("Provider requests are paused after repeated failures.") { RetryAfterSeconds = seconds; } }
        private async Task<ProviderFetchResult> GetRemoteImages(Season season, bool includeAllLanguages, string correlationId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var libraryOptions = library.GetLibraryOptions(season);
            var fetched = await GetRegisteredProviderImages(season, libraryOptions, x => IsMovieDb(x.Name) || IsTvdb(x.Name) || string.Equals(x.GetType().FullName, "MovieDb.MovieDbSeasonImageProvider", StringComparison.Ordinal) || (x.GetType().FullName ?? string.Empty).IndexOf("Tvdb", StringComparison.OrdinalIgnoreCase) >= 0, "TheMovieDb+TheTVDB", correlationId, cancellationToken).ConfigureAwait(false);
            // These BaseItem helpers apply Emby's inheritance rules. Reading LibraryOptions or
            // ServerConfiguration directly can legitimately return blank even when Emby's image
            // picker has resolved a current language for this item.
            var language = FirstNonBlank(season.GetPreferredImageLanguage(libraryOptions), season.GetPreferredMetadataLanguage(libraryOptions), libraryOptions.PreferredImageLanguage, libraryOptions.PreferredMetadataLanguage, configuration.Configuration.PreferredMetadataLanguage, "en");
            var beforeFilter = fetched.Images.Count;
            var providerLanguages = LanguageSummary(fetched.Images);
            fetched.Images = fetched.Images.Where(x => (x.Type == ImageType.Primary || x.Type == ImageType.Banner) && (includeAllLanguages || MatchesLanguage(x, language))).GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            foreach (var diagnostic in fetched.Diagnostics)
            {
                diagnostic.PosterCount = fetched.Images.Count(x => SameProvider(x.ProviderName, diagnostic.Provider) && x.Type == ImageType.Primary);
                diagnostic.BannerCount = fetched.Images.Count(x => SameProvider(x.ProviderName, diagnostic.Provider) && x.Type == ImageType.Banner);
                if ((diagnostic.Status == "Success" || diagnostic.Status == "Cached") && diagnostic.PosterCount + diagnostic.BannerCount == 0) { diagnostic.Status = "Empty"; diagnostic.Message = includeAllLanguages ? "Provider returned no images." : "Provider returned no images matching the selected language."; }
            }
            logger.Debug("Provider image aggregation completed. Season={0}, Id={1}, IncludeAllLanguages={2}, EffectiveLanguage={3}, BeforeLanguageFilter={4}, AfterLanguageFilter={5}, ProviderLanguages={6}, Providers={7}, CacheHits={8}, CacheMisses={9}", season.Name, season.GetClientId(), includeAllLanguages, language ?? "(none)", beforeFilter, fetched.Images.Count, providerLanguages, ProviderSummary(fetched.Images), fetched.CacheHits, fetched.CacheMisses);
            return fetched;
        }
        private async Task<ProviderFetchResult> GetRegisteredProviderImages(BaseItem item, MediaBrowser.Model.Configuration.LibraryOptions libraryOptions, Func<IRemoteImageProvider, bool> selector, string selectionName, string correlationId, CancellationToken cancellationToken)
        {
            var result = new ProviderFetchResult();
            var property = providers.GetType().GetProperty("ImageProviders");
            var registered = property == null ? null : property.GetValue(providers, null) as IEnumerable;
            if (registered == null)
            {
                logger.Error("Provider manager does not expose its ImageProviders collection. RuntimeType={0}, PropertyFound={1}", providers.GetType().AssemblyQualifiedName, property != null);
                return result;
            }
            var remoteProviders = registered.Cast<object>().OfType<IRemoteImageProvider>().ToArray();
            logger.Debug("Registered remote-provider inventory. Item={0}, ItemType={1}, Count={2}, Providers={3}", item.Name, item.GetType().FullName, remoteProviders.Length, string.Join(";", remoteProviders.Select(x => x.Name + "|" + x.GetType().FullName)));
            var selectedProviders = remoteProviders.Where(selector).ToArray();
            logger.Debug("Registered image-provider selection. Selection={0}, Item={1}, Matches={2}, Providers={3}", selectionName, item.Name, selectedProviders.Length, string.Join(",", selectedProviders.Select(x => x.Name + "|" + x.GetType().FullName)));
            foreach (var provider in selectedProviders)
            {
                ProviderDiagnostic diagnostic = null;
                try
                {
                    var supports = provider.Supports(item);
                    var supportedTypes = supports ? provider.GetSupportedImages(item).ToArray() : new ImageType[0];
                    if (!supports || !supportedTypes.Any(x => x == ImageType.Primary || x == ImageType.Banner)) continue;
                    diagnostic = new ProviderDiagnostic { Provider = provider.Name, SupportsPosters = supportedTypes.Contains(ImageType.Primary), SupportsBanners = supportedTypes.Contains(ImageType.Banner) };
                    result.Diagnostics.Add(diagnostic);
                    var cached = await ReadCache(item, provider, libraryOptions).ConfigureAwait(false);
                    if (cached != null && cached.IsFresh) { result.Images.AddRange(cached.Images); result.CacheHits++; diagnostic.Status = "Cached"; diagnostic.Message = "Loaded from the provider metadata cache."; diagnostic.Attempts = 0; continue; }
                    result.CacheMisses++;
                    var timer = Stopwatch.StartNew();
                    Exception lastError = null;
                    List<RemoteImageInfo> matching = null;
                    for (var attempt = 1; attempt <= MaximumProviderAttempts; attempt++)
                    {
                        diagnostic.Attempts = attempt;
                        try { matching = await InvokeProvider(provider, item, libraryOptions, cancellationToken).ConfigureAwait(false); lastError = null; break; }
                        catch (Exception ex)
                        {
                            if (cancellationToken.IsCancellationRequested) throw;
                            lastError = ex;
                            var transient = IsTransient(ex);
                            logger.Debug("Provider attempt failed. CorrelationId={0}, Item={1}, Provider={2}, Attempt={3}, Transient={4}, Error={5}", correlationId, item.Name, provider.Name, attempt, transient, FlattenMessage(ex));
                            if (!transient || attempt == MaximumProviderAttempts || ex is ProviderCircuitOpenException) break;
                            var retryAfter = TryGetRetryAfterSeconds(ex);
                            await Task.Delay(TimeSpan.FromSeconds(retryAfter.HasValue ? Math.Min(120, Math.Max(1, retryAfter.Value)) : attempt == 1 ? 1 : 3), cancellationToken).ConfigureAwait(false);
                        }
                    }
                    timer.Stop(); result.ProviderMilliseconds += timer.ElapsedMilliseconds;
                    if (lastError == null)
                    {
                        result.Images.AddRange(matching); await WriteCache(item, provider, libraryOptions, matching).ConfigureAwait(false);
                        diagnostic.Status = matching.Count == 0 ? "Empty" : "Success"; diagnostic.Message = matching.Count == 0 ? "Provider returned no images." : "Provider responded normally.";
                        if (diagnostic.Attempts > 1) logger.Info("Provider recovered after retry. CorrelationId={0}, Item={1}, Provider={2}, Results={3}, Attempts={4}, ElapsedMs={5}", correlationId, item.Name, provider.Name, matching.Count, diagnostic.Attempts, timer.ElapsedMilliseconds);
                        else logger.Debug("Registered provider invocation completed. CorrelationId={0}, Item={1}, Provider={2}, Results={3}, Attempts={4}, ElapsedMs={5}", correlationId, item.Name, provider.Name, matching.Count, diagnostic.Attempts, timer.ElapsedMilliseconds);
                    }
                    else
                    {
                        ClassifyFailure(lastError, diagnostic);
                        if (cached != null) { result.Images.AddRange(cached.Images); diagnostic.UsedStaleCache = true; diagnostic.StaleCacheAgeHours = (DateTime.UtcNow - cached.CreatedUtc).TotalHours; diagnostic.Message += " Showing stale cached results."; }
                        logger.ErrorException("Registered provider invocation failed. CorrelationId={0}, Item={1}, Provider={2}, Status={3}, Attempts={4}, UsedStaleCache={5}", lastError, correlationId, item.Name, provider.Name, diagnostic.Status, diagnostic.Attempts, diagnostic.UsedStaleCache);
                    }
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    if (diagnostic == null) { diagnostic = new ProviderDiagnostic { Provider = provider.Name }; result.Diagnostics.Add(diagnostic); }
                    ClassifyFailure(ex, diagnostic);
                    logger.ErrorException("Registered provider setup failed. CorrelationId={0}, Item={1}, Provider={2}, RuntimeType={3}", ex, correlationId, item.Name, provider.Name, provider.GetType().AssemblyQualifiedName);
                }
            }
            return result;
        }
        private async Task<List<RemoteImageInfo>> InvokeProvider(IRemoteImageProvider provider, BaseItem item, MediaBrowser.Model.Configuration.LibraryOptions libraryOptions, CancellationToken cancellationToken)
        {
            var gateKey = provider.GetType().AssemblyQualifiedName ?? provider.Name;
            var gate = ProviderGates.GetOrAdd(gateKey, _ => new ProviderGate());
            await gate.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                if (gate.OpenUntilUtc > now) throw new ProviderCircuitOpenException(Math.Max(1, (int)Math.Ceiling((gate.OpenUntilUtc - now).TotalSeconds)));
                var delay = gate.NextRequestUtc - now;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                gate.NextRequestUtc = DateTime.UtcNow + ProviderMinimumInterval;
                try
                {
                    using (var timeout = new CancellationTokenSource(ProviderTimeout))
                    using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
                    {
                        IEnumerable<RemoteImageInfo> fetched;
                        if (provider is IRemoteImageProviderWithOptions withOptions)
                        {
                            var options = new RemoteImageFetchOptions { Item = item, LibraryOptions = libraryOptions, DirectoryService = new DirectoryService(logger, fileSystem) };
                            fetched = await withOptions.GetImages(options, cancellation.Token).ConfigureAwait(false);
                        }
                        else fetched = await provider.GetImages(item, libraryOptions, cancellation.Token).ConfigureAwait(false);
                        gate.ConsecutiveFailures = 0; gate.OpenUntilUtc = DateTime.MinValue;
                        return (fetched ?? Enumerable.Empty<RemoteImageInfo>()).Where(x => x.Type == ImageType.Primary || x.Type == ImageType.Banner).ToList();
                    }
                }
                catch
                {
                    gate.ConsecutiveFailures++;
                    if (gate.ConsecutiveFailures >= MaximumProviderAttempts) gate.OpenUntilUtc = DateTime.UtcNow.AddMinutes(1);
                    throw;
                }
            }
            finally { gate.Lock.Release(); }
        }
        private static bool SameProvider(string imageProvider, string diagnosticProvider)
        {
            if (string.Equals(imageProvider, diagnosticProvider, StringComparison.OrdinalIgnoreCase)) return true;
            return IsMovieDb(imageProvider) && IsMovieDb(diagnosticProvider) || IsTvdb(imageProvider) && IsTvdb(diagnosticProvider);
        }
        private static bool IsTransient(Exception ex)
        {
            if (ex is ProviderCircuitOpenException || ex is TimeoutException || ex is TaskCanceledException || ex is OperationCanceledException) return true;
            var status = TryGetStatusCode(ex);
            if (status == 408 || status == 425 || status == 429 || status >= 500 && status <= 599) return true;
            var text = FlattenMessage(ex).ToLowerInvariant();
            return text.Contains("timeout") || text.Contains("timed out") || text.Contains("too many request") || text.Contains("rate limit") || text.Contains("temporar") || text.Contains("connection") || text.Contains("unavailable") || text.Contains("dns") || text.Contains("socket");
        }
        private static void ClassifyFailure(Exception ex, ProviderDiagnostic diagnostic)
        {
            var status = TryGetStatusCode(ex);
            var text = FlattenMessage(ex).ToLowerInvariant();
            if (ex is ProviderCircuitOpenException circuit)
            {
                diagnostic.Status = "Paused"; diagnostic.Message = "Provider requests are temporarily paused after repeated failures."; diagnostic.RetryAfterSeconds = circuit.RetryAfterSeconds;
            }
            else if (status == 429) { diagnostic.Status = "RateLimited"; diagnostic.Message = "Provider rate-limited the request."; diagnostic.RetryAfterSeconds = TryGetRetryAfterSeconds(ex); }
            else if (ex is TimeoutException || ex is TaskCanceledException || ex is OperationCanceledException || text.Contains("timeout") || text.Contains("timed out")) { diagnostic.Status = "TimedOut"; diagnostic.Message = "Provider timed out."; }
            else if (status >= 500 && status <= 599 || text.Contains("unavailable") || text.Contains("connection") || text.Contains("dns") || text.Contains("socket")) { diagnostic.Status = "Unavailable"; diagnostic.Message = "Provider appears to be unavailable."; }
            else if (text.Contains("too many request") || text.Contains("rate limit")) { diagnostic.Status = "RateLimitSuspected"; diagnostic.Message = "Provider rate limiting is suspected."; }
            else { diagnostic.Status = "Failed"; diagnostic.Message = "Provider request failed; see the Emby server log."; }
        }
        private static int? TryGetStatusCode(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                foreach (var name in new[] { "StatusCode", "HttpStatusCode" })
                {
                    try
                    {
                        var property = current.GetType().GetProperty(name);
                        if (property != null) { var value = property.GetValue(current, null); if (value != null) return Convert.ToInt32(value); }
                    }
                    catch { }
                }
                var message = current.Message ?? string.Empty;
                foreach (var code in new[] { 408, 425, 429, 500, 502, 503, 504 }) if (message.IndexOf(code.ToString(), StringComparison.Ordinal) >= 0) return code;
            }
            return null;
        }
        private static int? TryGetRetryAfterSeconds(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                try
                {
                    var property = current.GetType().GetProperty("RetryAfter") ?? current.GetType().GetProperty("RetryAfterSeconds");
                    if (property == null) continue;
                    var value = property.GetValue(current, null);
                    if (value is TimeSpan span) return Math.Max(1, (int)Math.Ceiling(span.TotalSeconds));
                    if (value is DateTime date) return Math.Max(1, (int)Math.Ceiling((date.ToUniversalTime() - DateTime.UtcNow).TotalSeconds));
                    if (value != null) return Math.Max(1, Convert.ToInt32(value));
                }
                catch { }
            }
            return null;
        }
        private static string FlattenMessage(Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current != null; current = current.InnerException) if (!string.IsNullOrWhiteSpace(current.Message)) messages.Add(current.Message);
            return string.Join(" --> ", messages);
        }
        private static bool MatchesLanguage(RemoteImageInfo image, string preferredLanguage)
        {
            if (string.IsNullOrWhiteSpace(preferredLanguage) || string.IsNullOrWhiteSpace(image.Language)) return true;
            var preferred = preferredLanguage.Split('-')[0];
            var actual = image.Language.Split('-')[0];
            return string.Equals(preferredLanguage, image.Language, StringComparison.OrdinalIgnoreCase) || string.Equals(preferred, actual, StringComparison.OrdinalIgnoreCase);
        }
        private static string FirstNonBlank(params string[] values) { return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)); }
        private static string LanguageSummary(IEnumerable<RemoteImageInfo> images)
        {
            var groups = images.GroupBy(x => string.IsNullOrWhiteSpace(x.Language) ? "(neutral)" : x.Language, StringComparer.OrdinalIgnoreCase).Select(x => x.Key + "=" + x.Count()).ToArray();
            return groups.Length == 0 ? "(none)" : string.Join(",", groups);
        }
        private string CacheFile(BaseItem item, IRemoteImageProvider provider, MediaBrowser.Model.Configuration.LibraryOptions options)
        {
            var identity = item.GetClientId() + "|" + provider.GetType().AssemblyQualifiedName + "|" + (options.PreferredImageLanguage ?? options.PreferredMetadataLanguage ?? string.Empty) + "|" + string.Join(";", item.ProviderIds.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value));
            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", string.Empty).ToLowerInvariant();
                return Path.Combine(cachePath, hash + ".json");
            }
        }
        private async Task<CacheReadResult> ReadCache(BaseItem item, IRemoteImageProvider provider, MediaBrowser.Model.Configuration.LibraryOptions options)
        {
            var path = CacheFile(item, provider, options);
            await CacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!fileSystem.FileExists(path)) return null;
                var entry = json.DeserializeFromFile<ProviderImageCacheEntry>(path);
                if (entry == null) return null;
                var age = DateTime.UtcNow - entry.CreatedUtc;
                var fresh = age < CacheLifetime;
                logger.Debug("Provider image cache {0}. Item={1}, Provider={2}, AgeHours={3:F1}, Results={4}", fresh ? "hit" : "stale", item.Name, provider.Name, age.TotalHours, entry.Images == null ? 0 : entry.Images.Count);
                return new CacheReadResult { CreatedUtc = entry.CreatedUtc, IsFresh = fresh, Images = (entry.Images ?? new List<CachedRemoteImage>()).Select(FromCached).ToList() };
            }
            catch (Exception ex) { logger.Warn("Provider image cache read failed; fetching fresh. Path={0}, Error={1}", path, ex.Message); return null; }
            finally { CacheLock.Release(); }
        }
        private async Task WriteCache(BaseItem item, IRemoteImageProvider provider, MediaBrowser.Model.Configuration.LibraryOptions options, List<RemoteImageInfo> images)
        {
            var path = CacheFile(item, provider, options);
            await CacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!fileSystem.DirectoryExists(cachePath)) fileSystem.CreateDirectory(cachePath);
                json.SerializeToFile(new ProviderImageCacheEntry { CreatedUtc = DateTime.UtcNow, Images = images.Select(ToCached).ToList() }, path);
            }
            catch (Exception ex) { logger.Warn("Provider image cache write failed. Path={0}, Error={1}", path, ex.Message); }
            finally { CacheLock.Release(); }
        }
        private static CachedRemoteImage ToCached(RemoteImageInfo image) { return new CachedRemoteImage { ProviderName = image.ProviderName, Url = image.Url, ThumbnailUrl = image.ThumbnailUrl, Height = image.Height, Width = image.Width, Language = image.Language, DisplayLanguage = image.DisplayLanguage, Type = image.Type }; }
        private static RemoteImageInfo FromCached(CachedRemoteImage image) { return new RemoteImageInfo { ProviderName = image.ProviderName, Url = image.Url, ThumbnailUrl = image.ThumbnailUrl, Height = image.Height, Width = image.Width, Language = image.Language, DisplayLanguage = image.DisplayLanguage, Type = image.Type }; }
        private static bool IsMovieDb(string providerName)
        {
            return !string.IsNullOrWhiteSpace(providerName) && (providerName.IndexOf("MovieDb", StringComparison.OrdinalIgnoreCase) >= 0 || providerName.IndexOf("Movie Database", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(providerName, "TMDB", StringComparison.OrdinalIgnoreCase));
        }
        private static bool IsTvdb(string providerName)
        {
            return !string.IsNullOrWhiteSpace(providerName) && providerName.IndexOf("TVDB", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static string ProviderSummary(IEnumerable<RemoteImageInfo> images)
        {
            var groups = images.GroupBy(x => x.ProviderName ?? "(unknown)").Select(x => x.Key + "=" + x.Count()).ToArray();
            return groups.Length == 0 ? "(none)" : string.Join(",", groups);
        }
        private void LogAvailableProviders(string itemKind, BaseItem item)
        {
            var available = providers.GetRemoteImageProviderInfo(item, library.GetLibraryOptions(item)).ToArray();
            if (available.Length == 0) { logger.Warn("No remote image fetchers registered. ItemKind={0}, Name={1}, Id={2}", itemKind, item.Name, item.GetClientId()); return; }
            foreach (var provider in available) logger.Debug("Remote image fetcher available. ItemKind={0}, Name={1}, Id={2}, Fetcher={3}, Supported={4}", itemKind, item.Name, item.GetClientId(), provider.Name, string.Join(",", provider.SupportedImages));
        }
        private string ImageTag(BaseItem item, ImageType type) { var info = item.GetImageInfo(type, 0); return info == null ? null : imageProcessor.GetImageCacheTag(item, info); }
        private static string CurrentImageUrl(BaseItem item, ImageType type, string tag) { return item.HasImage(type, 0) ? "/Items/" + item.GetClientId() + "/Images/" + type + "?tag=" + Uri.EscapeDataString(tag ?? string.Empty) + "&quality=90" : null; }
        private static List<ImageResult> DistinctImages(IEnumerable<ImageResult> images) { return images.Where(x => !string.IsNullOrWhiteSpace(x.Url)).GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList(); }
        private void LogProviderCounts(Season season, IEnumerable<ImageResult> images) { foreach (var group in images.GroupBy(x => new { x.Provider, x.Type })) logger.Debug("Fetcher result. Season={0}, Fetcher={1}, Type={2}, Count={3}", season.Name, group.Key.Provider ?? "(unknown)", group.Key.Type, group.Count()); }
        private static string LocalImageUrl(BaseItem item, ImageType type) { return item.HasImage(type, 0) ? "/Items/" + item.GetClientId() + "/Images/" + type : null; }
        private static List<ImageResult> Map(IEnumerable<RemoteImageInfo> images, ImageType type) { return images.Where(i => i.Type == type).Select(i => new ImageResult { Url = i.Url, ThumbnailUrl = i.ThumbnailUrl ?? i.Url, Type = type == ImageType.Primary ? "Poster" : "Banner", Provider = i.ProviderName, Width = i.Width, Height = i.Height }).ToList(); }
    }

}
