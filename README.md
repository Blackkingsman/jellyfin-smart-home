# Smart Home for Jellyfin

Netflix-style home screen for Jellyfin 12.4: personal rows on the home page, plus smart collections that keep themselves up to date.

**Home rows** (per user, new mix daily)
- **Because you watched [title]**: similar movies and shows to what you watched last, unwatched only
- **Top picks for you / TV picks for you**: best-rated unwatched titles in the genre you watch most
- **New on the server**: latest movies and shows
- **Shelves**: a daily rotation of your collections, unwatched first

**Smart Shelves** (collections with generated posters)
- Movies: Top Rated, Critics' Picks, Fresh Hits, Date Night, Fright Night, Mind-Benders, and more
- Per genre, for movies and TV: **Top Rated [genre]** and **Recent [genre] Hits** (last five years)
- Decades: e.g. 80s Comedy, 90s Action, 2000s Drama, for the biggest combos in your library
- TV: New Episodes, Top Rated TV, Kids & Family TV, Docuseries
- Ratings blend audience and critic scores, and brand-new releases without reviews don't jump the line
- Updates 10 minutes after new content is added, and nightly at 4 AM. Run it by hand from **Dashboard → Scheduled Tasks → Update Smart Shelves**.

Rows use wide backdrop cards with hover zoom and scroll arrows, and pick up your Jellyfin theme's fonts and colors.

## How it works

```
Jellyfin server
 └─ Smart Home plugin
     ├─ StartupTask    → asks File Transformation to patch index.html
     ├─ IndexPatch     → adds <script src="../SmartHome/client.js">
     ├─ ShelfTask      → builds the shelf collections and posters (after scans + nightly)
     └─ /SmartHome/... → serves the script and row settings
Browser
 └─ client.js → queries Jellyfin's API as the signed-in user and draws the rows
                above the built-in home sections
```

Requires the **File Transformation** plugin.

Works in browsers, Jellyfin Media Player, and the Jellyfin Android and iOS apps (they use the web client). Native TV apps (Android TV, Roku, Swiftfin) don't load web scripts.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build Jellyfin.Plugin.SmartHome -c Release
```

## Install

1. In Jellyfin: **Dashboard → Plugins → Repositories → +**, and add
   `https://raw.githubusercontent.com/Blackkingsman/jellyfin-smart-home/main/manifest.json`
2. **Catalog → Smart Home → Install**, then restart Jellyfin.

New versions show up as updates in the same place.

## Release

Build, zip the DLL into `releases/smart-home_<version>.zip`, and add a version entry to `manifest.json` with the zip's MD5 checksum.

## Settings

`ShelfRows` (default 8) and `ItemsPerRow` (default 20) in the plugin configuration.

Poster font: [Bebas Neue](https://github.com/dharmatype/Bebas-Neue), SIL Open Font License (`Fonts/OFL.txt`).
