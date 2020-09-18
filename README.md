<h1 align="center">Jellyfin Missing Episode Provider Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.media">Jellyfin Project</a></h3>

<p align="center">
Jellyfin Missing Episode Provider is a plugin built with .NET that automatically populates your series with missing episode data based on TheTVDB's series data.
</p>

## Build Process

1. Clone or download this repository

2. Ensure you have .NET Core SDK setup and installed

3. Build plugin with following command

```sh
dotnet publish --configuration Release --output bin
```

4. Place the resulting file in the `plugins` folder
