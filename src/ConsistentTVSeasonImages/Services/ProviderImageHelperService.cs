using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
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
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Serialization;

namespace ConsistentTVSeasonImages.Services
{
    [Route("/ProviderImageHelper/Discover", "GET")]
    [Authenticated(Roles = "admin")]
    public sealed class DiscoverRequest : IReturn<List<ShowResult>> { public string Filter { get; set; } public string Search { get; set; } }
    [Route("/ProviderImageHelper/Fetch", "GET")]
    [Authenticated(Roles = "admin")]
    public sealed class FetchRequest : IReturn<FetchResult> { public string SeriesId { get; set; } public bool IncludeAllLanguages { get; set; } }
    [Route("/ProviderImageHelper/Apply", "POST")]
    [Authenticated(Roles = "admin")]
    public sealed class ApplyRequest : IReturn<ApplyResult> { public string SeasonId { get; set; } public string ImageType { get; set; } public string ImageUrl { get; set; } public string SourceItemId { get; set; } }
    [Route("/ProviderImageHelper/Image", "DELETE")]
    [Authenticated(Roles = "admin")]
    public sealed class RemoveImageRequest : IReturn<ApplyResult> { public string SeasonId { get; set; } public string ImageType { get; set; } }
    [Route("/ProviderImageHelper/Cache", "DELETE")]
    [Authenticated(Roles = "admin")]
    public sealed class ClearCacheRequest : IReturn<ClearCacheResult> { }

    public sealed class ShowResult { public string Id { get; set; } public string Name { get; set; } public bool MissingPoster { get; set; } public bool MissingBanner { get; set; } }
    public sealed class ImageResult { public string Url { get; set; } public string ThumbnailUrl { get; set; } public string Type { get; set; } public string Provider { get; set; } public int? Width { get; set; } public int? Height { get; set; } }
    public sealed class SeasonResult { public string Id { get; set; } public string Name { get; set; } public int? Number { get; set; } public string CurrentPoster { get; set; } public string CurrentBanner { get; set; } public string CurrentPosterItemId { get; set; } public string CurrentBannerItemId { get; set; } public string CurrentPosterTag { get; set; } public string CurrentBannerTag { get; set; } public List<ImageResult> Posters { get; set; } public List<ImageResult> Banners { get; set; } }
    public sealed class FetchResult { public string SeriesId { get; set; } public string Name { get; set; } public List<SeasonResult> Seasons { get; set; } }
    public sealed class ApplyResult { public bool Success { get; set; } public string SeasonId { get; set; } public string ImageType { get; set; } }
    public sealed class ClearCacheResult { public bool Success { get; set; } public int FilesRemoved { get; set; } }
    public sealed class ProviderImageCacheEntry { public DateTime CreatedUtc { get; set; } public List<CachedRemoteImage> Images { get; set; } }
    public sealed class CachedRemoteImage { public string ProviderName { get; set; } public string Url { get; set; } public string ThumbnailUrl { get; set; } public int? Height { get; set; } public int? Width { get; set; } public string Language { get; set; } public string DisplayLanguage { get; set; } public ImageType Type { get; set; } }

    public sealed class ProviderImageHelperService : IService
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
        private static readonly SemaphoreSlim CacheLock = new SemaphoreSlim(1, 1);
        private readonly ILibraryManager library; private readonly IProviderManager providers; private readonly IFileSystem fileSystem; private readonly IImageProcessor imageProcessor; private readonly IServerConfigurationManager configuration; private readonly IJsonSerializer json; private readonly ILogger logger; private readonly string cachePath;
        public ProviderImageHelperService(ILibraryManager library, IProviderManager providers, IFileSystem fileSystem, IImageProcessor imageProcessor, IServerConfigurationManager configuration, IApplicationPaths applicationPaths, IJsonSerializer json, ILogManager logs)
        { this.library = library; this.providers = providers; this.fileSystem = fileSystem; this.imageProcessor = imageProcessor; this.configuration = configuration; this.json = json; cachePath = Path.Combine(applicationPaths.CachePath, "consistent-tv-season-images", "provider-images"); logger = Plugin.Logger ?? logs.GetLogger("Consistent TV Season Images"); logger.Info("ProviderImageHelperService instantiated. Provider cache={0}, LifetimeHours={1}", cachePath, CacheLifetime.TotalHours); }

        public object Get(DiscoverRequest request)
        {
            var filter = (request.Filter ?? "all").ToLowerInvariant(); var search = request.Search ?? string.Empty;
            logger.Info("Discover started. Filter={0}, Search={1}", filter, search);
            try
            {
                var queried = library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { typeof(Series).Name }, Recursive = true, IsVirtualItem = false, HasPath = true }).OfType<Series>().ToList();
                var excluded = queried.Where(s => string.IsNullOrEmpty(s.Path) || !string.IsNullOrEmpty(s.ExternalId)).ToList();
                foreach (var item in excluded) logger.Debug("Discover excluded non-library series. Name={0}, Id={1}, Path={2}, ExternalId={3}", item.Name, item.GetClientId(), item.Path ?? "(null)", item.ExternalId ?? "(null)");
                var result = queried.Where(s => !string.IsNullOrEmpty(s.Path) && string.IsNullOrEmpty(s.ExternalId) && s.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(s => { var seasons = GetSeasons(s); return new ShowResult { Id = s.GetClientId(), Name = s.Name, MissingPoster = seasons.Any(x => !x.HasImage(ImageType.Primary, 0)), MissingBanner = seasons.Any(x => !x.HasImage(ImageType.Banner, 0)) }; })
                    .Where(s => filter == "all" || filter == "missingposters" && s.MissingPoster || filter == "missingbanners" && s.MissingBanner).OrderBy(s => s.Name).ToList();
                logger.Info("Discover completed. Queried={0}, Excluded={1}, Returned={2}", queried.Count, excluded.Count, result.Count);
                return result;
            }
            catch (Exception ex) { logger.ErrorException("Discover failed. Filter={0}, Search={1}", ex, filter, search); throw; }
        }

        public async Task<object> Get(FetchRequest request)
        {
            var fetchTimer = Stopwatch.StartNew();
            logger.Info("Fetch started. SeriesId={0}", request.SeriesId);
            try
            {
                var series = library.GetItemById(request.SeriesId) as Series; if (series == null) throw new ArgumentException("Series not found.");
                var seasons = GetSeasons(series).OrderBy(s => s.IndexNumber).ToArray();
                logger.Info("Fetch resolved series. Name={0}, Path={1}, Seasons={2}, ProviderIds={3}", series.Name, series.Path ?? "(null)", seasons.Length, string.Join(",", series.ProviderIds.Select(x => x.Key + "=" + x.Value)));
                LogAvailableProviders("Series", series);
                var result = new FetchResult { SeriesId = request.SeriesId, Name = series.Name, Seasons = new List<SeasonResult>() };
                var cacheHits = 0; var cacheMisses = 0; var providerMilliseconds = 0L;
                foreach (var season in seasons)
                {
                    var seasonTimer = Stopwatch.StartNew();
                    logger.Info("Fetch season identity. Season={0}, Id={1}, SeriesId={2}, ProviderIds={3}, ImageInfos={4}", season.Name, season.GetClientId(), season.SeriesId, string.Join(",", season.ProviderIds.Select(x => x.Key + "=" + x.Value)), string.Join(";", season.ImageInfos.Select(x => x.Type + "=" + x.Path)));
                    LogAvailableProviders("Season", season);
                    var fetched = await GetRemoteImages(season, request.IncludeAllLanguages).ConfigureAwait(false);
                    cacheHits += fetched.CacheHits; cacheMisses += fetched.CacheMisses; providerMilliseconds += fetched.ProviderMilliseconds;
                    var posters = fetched.Images.Where(x => x.Type == ImageType.Primary).ToList();
                    var banners = fetched.Images.Where(x => x.Type == ImageType.Banner).ToList();
                    var posterResults = Map(posters, ImageType.Primary); var bannerResults = Map(banners, ImageType.Banner);
                    posterResults = DistinctImages(posterResults); bannerResults = DistinctImages(bannerResults);
                    logger.Info("Fetch season completed. Season={0}, Id={1}, HasCurrentPoster={2}, HasCurrentBanner={3}, Posters={4}, Banners={5}, ElapsedMs={6}, CacheHits={7}, CacheMisses={8}", season.Name, season.GetClientId(), season.HasImage(ImageType.Primary, 0), season.HasImage(ImageType.Banner, 0), posterResults.Count, bannerResults.Count, seasonTimer.ElapsedMilliseconds, fetched.CacheHits, fetched.CacheMisses);
                    LogProviderCounts(season, posterResults.Concat(bannerResults));
                    var seasonPoster = LocalImageUrl(season, ImageType.Primary); var seasonBanner = LocalImageUrl(season, ImageType.Banner);
                    var posterTag = ImageTag(season, ImageType.Primary); var bannerTag = ImageTag(season, ImageType.Banner);
                    logger.Info("Current Emby image URL. Season={0}, Id={1}, Type=Primary, Url={2}", season.Name, season.GetClientId(), CurrentImageUrl(season, ImageType.Primary, posterTag) ?? "(none)");
                    logger.Info("Current Emby image URL. Season={0}, Id={1}, Type=Banner, Url={2}", season.Name, season.GetClientId(), CurrentImageUrl(season, ImageType.Banner, bannerTag) ?? "(none)");
                    result.Seasons.Add(new SeasonResult { Id = season.GetClientId(), Name = season.Name, Number = season.IndexNumber, CurrentPoster = seasonPoster, CurrentBanner = seasonBanner, CurrentPosterItemId = seasonPoster == null ? null : season.GetClientId(), CurrentBannerItemId = seasonBanner == null ? null : season.GetClientId(), CurrentPosterTag = posterTag, CurrentBannerTag = bannerTag, Posters = posterResults, Banners = bannerResults });
                }
                logger.Info("Fetch completed. Series={0}, Seasons={1}, TotalPosters={2}, TotalBanners={3}, ElapsedMs={4}, ProviderElapsedMs={5}, CacheHits={6}, CacheMisses={7}", series.Name, result.Seasons.Count, result.Seasons.Sum(x => x.Posters.Count), result.Seasons.Sum(x => x.Banners.Count), fetchTimer.ElapsedMilliseconds, providerMilliseconds, cacheHits, cacheMisses);
                return result;
            }
            catch (Exception ex) { logger.ErrorException("Fetch failed. SeriesId={0}", ex, request.SeriesId); throw; }
        }

        public async Task<object> Post(ApplyRequest request)
        {
            logger.Info("Apply started. SeasonId={0}, ImageType={1}, Url={2}, SourceItemId={3}", request.SeasonId, request.ImageType, request.ImageUrl ?? "(null)", request.SourceItemId ?? "(null)");
            try { var season = library.GetItemById(request.SeasonId) as Season; if (season == null) throw new ArgumentException("Season not found."); ImageType type; var name = request.ImageType == "Poster" ? "Primary" : request.ImageType; if (!Enum.TryParse(name, true, out type) || type != ImageType.Primary && type != ImageType.Banner) throw new ArgumentException("ImageType must be Poster or Banner."); var source = request.ImageUrl; if (!string.IsNullOrEmpty(request.SourceItemId)) { var sourceItem = library.GetItemById(request.SourceItemId); var sourceInfo = sourceItem?.GetImageInfo(type, 0); if (sourceInfo == null || string.IsNullOrEmpty(sourceInfo.Path) || !fileSystem.FileExists(sourceInfo.Path)) throw new ArgumentException("The selected current source image is unavailable."); source = sourceInfo.Path; logger.Info("Apply resolved local current source. SourceItem={0}, Type={1}, Path={2}", sourceItem.Name, type, source); } else { Uri uri; if (!Uri.TryCreate(source, UriKind.Absolute, out uri) || uri.Scheme != "https" && uri.Scheme != "http") throw new ArgumentException("A valid HTTP(S) image URL is required."); } await providers.SaveImage(season, library.GetLibraryOptions(season), source, type, 0, new long[0], new DirectoryService(logger, fileSystem), true, CancellationToken.None).ConfigureAwait(false); season.UpdateToRepository(ItemUpdateType.ImageUpdate); logger.Info("Apply completed. Season={0}, Id={1}, ImageType={2}", season.Name, request.SeasonId, type); return new ApplyResult { Success = true, SeasonId = request.SeasonId, ImageType = request.ImageType }; }
            catch (Exception ex) { logger.ErrorException("Apply failed. SeasonId={0}, ImageType={1}, Url={2}", ex, request.SeasonId, request.ImageType, request.ImageUrl); throw; }
        }

        public object Delete(RemoveImageRequest request)
        {
            logger.Info("Remove image started. SeasonId={0}, ImageType={1}", request.SeasonId, request.ImageType);
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

        private Season[] GetSeasons(Series series) { return library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { typeof(Season).Name }, AncestorIds = new[] { series.InternalId }, Recursive = true }).OfType<Season>().ToArray(); }
        private sealed class ProviderFetchResult { public List<RemoteImageInfo> Images { get; set; } = new List<RemoteImageInfo>(); public int CacheHits { get; set; } public int CacheMisses { get; set; } public long ProviderMilliseconds { get; set; } }
        private async Task<ProviderFetchResult> GetRemoteImages(Season season, bool includeAllLanguages)
        {
            var libraryOptions = library.GetLibraryOptions(season);
            var fetched = await GetRegisteredProviderImages(season, libraryOptions, x => IsMovieDb(x.Name) || IsTvdb(x.Name) || string.Equals(x.GetType().FullName, "MovieDb.MovieDbSeasonImageProvider", StringComparison.Ordinal) || (x.GetType().FullName ?? string.Empty).IndexOf("Tvdb", StringComparison.OrdinalIgnoreCase) >= 0, "TheMovieDb+TheTVDB").ConfigureAwait(false);
            // These BaseItem helpers apply Emby's inheritance rules. Reading LibraryOptions or
            // ServerConfiguration directly can legitimately return blank even when Emby's image
            // picker has resolved a current language for this item.
            var language = FirstNonBlank(season.GetPreferredImageLanguage(libraryOptions), season.GetPreferredMetadataLanguage(libraryOptions), libraryOptions.PreferredImageLanguage, libraryOptions.PreferredMetadataLanguage, configuration.Configuration.PreferredMetadataLanguage, "en");
            var beforeFilter = fetched.Images.Count;
            var providerLanguages = LanguageSummary(fetched.Images);
            fetched.Images = fetched.Images.Where(x => (x.Type == ImageType.Primary || x.Type == ImageType.Banner) && (includeAllLanguages || MatchesLanguage(x, language))).GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            logger.Info("Provider image aggregation completed. Season={0}, Id={1}, IncludeAllLanguages={2}, EffectiveLanguage={3}, BeforeLanguageFilter={4}, AfterLanguageFilter={5}, ProviderLanguages={6}, Providers={7}, CacheHits={8}, CacheMisses={9}", season.Name, season.GetClientId(), includeAllLanguages, language ?? "(none)", beforeFilter, fetched.Images.Count, providerLanguages, ProviderSummary(fetched.Images), fetched.CacheHits, fetched.CacheMisses);
            return fetched;
        }
        private async Task<ProviderFetchResult> GetRegisteredProviderImages(BaseItem item, MediaBrowser.Model.Configuration.LibraryOptions libraryOptions, Func<IRemoteImageProvider, bool> selector, string selectionName)
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
            logger.Info("Registered image-provider selection. Selection={0}, Item={1}, Matches={2}, Providers={3}", selectionName, item.Name, selectedProviders.Length, string.Join(",", selectedProviders.Select(x => x.Name + "|" + x.GetType().FullName)));
            foreach (var provider in selectedProviders)
            {
                try
                {
                    var supports = provider.Supports(item);
                    var supportedTypes = supports ? provider.GetSupportedImages(item).ToArray() : new ImageType[0];
                    if (!supports || !supportedTypes.Any(x => x == ImageType.Primary || x == ImageType.Banner)) continue;
                    var cached = await ReadCache(item, provider, libraryOptions).ConfigureAwait(false);
                    if (cached != null) { result.Images.AddRange(cached); result.CacheHits++; continue; }
                    result.CacheMisses++;
                    var timer = Stopwatch.StartNew();
                    IEnumerable<RemoteImageInfo> fetched;
                    if (provider is IRemoteImageProviderWithOptions withOptions)
                    {
                        var options = new RemoteImageFetchOptions { Item = item, LibraryOptions = libraryOptions, DirectoryService = new DirectoryService(logger, fileSystem) };
                        fetched = await withOptions.GetImages(options, CancellationToken.None).ConfigureAwait(false);
                    }
                    else fetched = await provider.GetImages(item, libraryOptions, CancellationToken.None).ConfigureAwait(false);
                    var matching = (fetched ?? Enumerable.Empty<RemoteImageInfo>()).Where(x => x.Type == ImageType.Primary || x.Type == ImageType.Banner).ToList();
                    timer.Stop(); result.ProviderMilliseconds += timer.ElapsedMilliseconds; result.Images.AddRange(matching);
                    await WriteCache(item, provider, libraryOptions, matching).ConfigureAwait(false);
                    logger.Info("Registered provider invocation completed. Item={0}, Provider={1}, Results={2}, ElapsedMs={3}", item.Name, provider.Name, matching.Count, timer.ElapsedMilliseconds);
                }
                catch (Exception ex) { logger.ErrorException("Registered provider invocation failed. Item={0}, Provider={1}, RuntimeType={2}", ex, item.Name, provider.Name, provider.GetType().AssemblyQualifiedName); }
            }
            return result;
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
        private async Task<List<RemoteImageInfo>> ReadCache(BaseItem item, IRemoteImageProvider provider, MediaBrowser.Model.Configuration.LibraryOptions options)
        {
            var path = CacheFile(item, provider, options);
            await CacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!fileSystem.FileExists(path)) return null;
                var entry = json.DeserializeFromFile<ProviderImageCacheEntry>(path);
                if (entry == null || DateTime.UtcNow - entry.CreatedUtc >= CacheLifetime)
                {
                    fileSystem.DeleteFile(path);
                    logger.Info("Provider image cache expired. Item={0}, Provider={1}", item.Name, provider.Name);
                    return null;
                }
                logger.Debug("Provider image cache hit. Item={0}, Provider={1}, AgeHours={2:F1}, Results={3}", item.Name, provider.Name, (DateTime.UtcNow - entry.CreatedUtc).TotalHours, entry.Images == null ? 0 : entry.Images.Count);
                return (entry.Images ?? new List<CachedRemoteImage>()).Select(FromCached).ToList();
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
            foreach (var provider in available) logger.Info("Remote image fetcher available. ItemKind={0}, Name={1}, Id={2}, Fetcher={3}, Supported={4}", itemKind, item.Name, item.GetClientId(), provider.Name, string.Join(",", provider.SupportedImages));
        }
        private string ImageTag(BaseItem item, ImageType type) { var info = item.GetImageInfo(type, 0); return info == null ? null : imageProcessor.GetImageCacheTag(item, info); }
        private static string CurrentImageUrl(BaseItem item, ImageType type, string tag) { return item.HasImage(type, 0) ? "/Items/" + item.GetClientId() + "/Images/" + type + "?tag=" + Uri.EscapeDataString(tag ?? string.Empty) + "&quality=90" : null; }
        private static List<ImageResult> DistinctImages(IEnumerable<ImageResult> images) { return images.Where(x => !string.IsNullOrWhiteSpace(x.Url)).GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList(); }
        private void LogProviderCounts(Season season, IEnumerable<ImageResult> images) { foreach (var group in images.GroupBy(x => new { x.Provider, x.Type })) logger.Info("Fetcher result. Season={0}, Fetcher={1}, Type={2}, Count={3}", season.Name, group.Key.Provider ?? "(unknown)", group.Key.Type, group.Count()); }
        private static string LocalImageUrl(BaseItem item, ImageType type) { return item.HasImage(type, 0) ? "/Items/" + item.GetClientId() + "/Images/" + type : null; }
        private static List<ImageResult> Map(IEnumerable<RemoteImageInfo> images, ImageType type) { return images.Where(i => i.Type == type).Select(i => new ImageResult { Url = i.Url, ThumbnailUrl = i.ThumbnailUrl ?? i.Url, Type = type == ImageType.Primary ? "Poster" : "Banner", Provider = i.ProviderName, Width = i.Width, Height = i.Height }).ToList(); }
    }

}
