# Smart Home for Jellyfin

Netflix-style rows on the Jellyfin home screen, built for Jellyfin 12.4.

- **Because you watched [movie]**: similar titles to your last two movies, unwatched only
- **Top picks for you**: highly rated unwatched movies in the genre you watch most
- **New on the server**: latest movies and shows
- **Shelves**: a daily rotation of your collections (pairs with [Smart Shelves](../jellyfin-smart-shelves)), unwatched first

Rows use wide backdrop cards with hover zoom and scroll arrows, and pick up your Jellyfin theme's fonts and colors. Every row is per user, and the mix changes daily.

## How it works

```
Jellyfin server
 └─ Smart Home plugin
     ├─ StartupTask    → asks File Transformation to patch index.html
     ├─ IndexPatch     → adds <script src="../SmartHome/client.js">
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

Copy the build output into a folder in your server's plugin directory, then restart Jellyfin:

```
<jellyfin config>/plugins/SmartHome_1.0.0.0/Jellyfin.Plugin.SmartHome.dll
```

## Settings

`ShelfRows` (default 6) and `ItemsPerRow` (default 20) in the plugin configuration.
