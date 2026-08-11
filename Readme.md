# Consistent TV Season Images

Consistent TV Season Images is an Emby Server plugin for reviewing and standardising poster and banner artwork across every season of a TV series.

Instead of editing each season separately, the plugin presents the current artwork and the available images from Emby's registered TheMovieDB and TheTVDB providers in one comparison workspace. This makes it easier to choose a visually consistent set and apply it across the series.

## What it does

- Finds TV shows in local Emby libraries and highlights shows with missing season posters or banners.
- Displays every season of a selected series together with its current poster and banner.
- Collects remote season artwork from the TheMovieDB and TheTVDB image providers already registered with Emby.
- Uses the library's preferred image or metadata language by default, while retaining language-neutral artwork.
- Can show artwork in all available languages when a wider selection is needed.
- Provides poster-only, banner-only, and combined layouts.
- Lets administrators drag candidate images into comparison columns to plan a consistent set before making changes.
- Applies an individual image immediately or applies the selected comparison column across all seasons.
- Supports multiple comparison columns and multiple season panes for evaluating alternatives.
- Allows the current poster or banner to be removed from a season.
- Shows provider status, empty results, failures, and retry controls directly in the image pools.

## Provider caching and reliability

Provider image metadata is cached for one hour to make repeated comparisons faster and reduce requests to external services. The cache can be cleared from the plugin page when a fresh provider lookup is required.

Provider requests are rate-limited, have a 30-second timeout, and retry transient failures with backoff. If a provider is temporarily unavailable, the plugin can display stale cached results where available. Fetch timings, cache activity, provider results, and correlation IDs are written to the Emby log for troubleshooting.

## Using the plugin

1. Open **Consistent TV Season Images** from the Emby administration interface.
2. Filter the library or search for a show, then select **Load show**.
3. Choose whether to display posters, banners, or both. Enable all languages if required.
4. Review the current artwork and the provider image pools for each season.
5. Apply a candidate directly, or drag candidates into a comparison column.
6. Select a completed comparison column and use **Apply All** to update its populated season images.

Applying or removing artwork changes the corresponding season image in the Emby library. Empty cells are skipped by **Apply All**.

## Requirements

- Emby Server 4.9-compatible environment
- TV series with season items in a local Emby library
- Working TheMovieDB and/or TheTVDB metadata provider configuration in Emby
- An Emby administrator account to access the plugin page and its API

The plugin uses Emby's existing provider registrations and library language preferences; it does not run a separate metadata service.