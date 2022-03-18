# GenshinStudio
Check out the [original AssetStudio project](https://github.com/Perfare/AssetStudio) for more information.

This is the release of GenshinStudio, Modded AssetStudio that should work with Genshin Impact/YuanShen.

Note: Requires Internet connection to fetch asset_index jsons.
_____________________________________________________________________________________________________________________________

Some features are:
```
- BLK/CAB methods support.
- Integration with `Radioegor146` repo to load asset_index through `Options -> Specify AI version`.
- Exportable Assets (not all of them) with XOR/JSON support for `MiHoYoBinData`
- Togglable debug console.
- Container/filename recovery for Assets.
```
_____________________________________________________________________________________________________________________________
How to use:

CAB Method:
```
1. Extract blks to a specific location (File -> Extract folder).
2. Build CAB Map (Misc. -> Build CABMap).
3. Load AssetIndex file: (Options -> Specify AI version) online or (Misc. -> Select AI JSON) generated locally.
```
BLK Method:
```
1. Build BLK Map (Misc. -> Build BLKMap).
2. Load BLK files.
```

~~NOTE: to generate the .json file locally, use [AssetIndexReader](https://github.com/Razmoth/AssetIndexReader) CLI tool with binary asset_index file, which can be found in 31049740.blk.
Extract it using `File -> Extract File` in [GenshinStudio](https://github.com/Razmoth/GenshinStudio), then used the resulted `.bin` file in [this](https://github.com/Razmoth/AssetIndexReader) tool, should work.
[Make sure XOR key is turned off in `Export Options`]~~

NOTE: ![AssetIndexReader](https://github.com/Razmoth/AssetIndexReader) support has been dropped to avoid breaking compatibility, also 2.5.0 assets causes an breaking ![issue](https://github.com/khang06/AssetStudio/issues/11), make sure to enable `Disable Renderer` option in `Export Options` before loading 2.5.0 assets.

Looking forward for feedback for issues/bugs to fix and update.
_____________________________________________________________________________________________________________________________
Special Thank to:
- Khang06: [genshinblkstuff](https://github.com/khang06/genshinblkstuff) for blk/mhy0 extraction.
- Radioegor146: [gi-asset-indexes](https://github.com/radioegor146/gi-asset-indexes) for recovered/updated asset_index's.
- Ds5678: [AssetRipper](https://github.com/AssetRipper/AssetRipper)[[discord](https://discord.gg/XqXa53W2Yh)] for information about Asset Formats & Parsing.
