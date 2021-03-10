<h1 align="center">Jellyfin Missing Episode Provider Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.media">Jellyfin Project</a></h3>

<p align="center">
<img alt="Logo Banner" src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/SVG/banner-logo-solid.svg?sanitize=true"/>
<br/>
<br/>
<a href="https://github.com/jellyfin/jellyfin-plugin-missingepisodeprovider/actions?query=workflow%3A%22Test+Build+Plugin%22">
<img alt="GitHub Workflow Status" src="https://img.shields.io/github/workflow/status/jellyfin/jellyfin-plugin-missingepisodeprovider/Test%20Build%20Plugin.svg">
</a>
<a href="https://github.com/jellyfin/jellyfin-plugin-missingepisodeprovider">
<img alt="MIT License" src="https://img.shields.io/github/license/jellyfin/jellyfin-plugin-missingepisodeprovider.svg"/>
</a>
<a href="https://github.com/jellyfin/jellyfin-plugin-missingepisodeprovider/releases">
<img alt="Current Release" src="https://img.shields.io/github/release/jellyfin/jellyfin-plugin-missingepisodeprovider.svg"/>
</a>
</p>

## About
Jellyfin Missing Episode Provider is a plugin built with .NET that automatically populates your series with missing episode data based on TheTVDB's series data.

## Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command

```sh
dotnet publish --configuration Release --output bin
```

4. Place the resulting file in the `plugins` folder
