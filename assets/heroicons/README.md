# Toolbar symbols

The five SVG files are from [Heroicons](https://github.com/tailwindlabs/heroicons), 20 px solid collection, copyright Tailwind Labs, Inc., licensed under the included MIT `LICENSE`. `UPSTREAM.txt` identifies the upstream revision used when preparing these assets.

`glyphs.bin` contains flattened vector paths, not raster images. Regenerate it on Windows after editing or replacing SVG sources:

```powershell
dotnet run --project assets/ToolbarGenerator/ToolbarGenerator.csproj -c Release -- assets/heroicons
```

The build-time converter uses WPF geometry parsing. The resident application only uses the embedded vector data and Windows GDI+; it never loads WPF or fetches icons from the network.
