# Third-party dependencies

Forma's MIT license covers original source only. External dependencies retain their licenses. Portable releases include an inventory and copied upstream license/notice files under `licenses/` where shipped in NuGet packages. Source URLs below identify the upstream projects; redistribution must comply with their actual licenses and notices.

| Component | License / distribution |
| --- | --- |
| [.NET runtime](https://github.com/dotnet/runtime) | MIT and additional third-party notices; included in the self-contained release |
| [Windows App SDK / WinUI](https://github.com/microsoft/WindowsAppSDK) | MIT project code; Microsoft runtime/redist terms apply to packaged native components; included |
| [Microsoft.Data.Sqlite / EF Core](https://github.com/dotnet/efcore) | MIT; included |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | Apache-2.0; included |
| [SQLite](https://sqlite.org/copyright.html) | SQLite public domain; native package may carry additional packaging notices; included |
| [FFmpeg / ffprobe](https://ffmpeg.org/legal.html) | Normally LGPL-2.1-or-later; optional GPL components change the build's license. libx264/libx265 builds are GPL. User-installed, excluded from release |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp#license) | Project source Unlicense; Windows executable bundles third-party components and can be GPLv3+ as a combined binary. Installed separately from the publisher, manually or through Forma's explicitly requested verified updater; excluded from the Forma ZIP |
| [yt-dlp EJS](https://github.com/yt-dlp/ejs) | Check license/third-party notices of the exact official yt-dlp build. Included by its publisher, not by Forma |
| [Deno](https://github.com/denoland/deno) | MIT plus bundled third-party licenses; user-installed, excluded |
| [Python](https://docs.python.org/3/license.html) | PSF license and bundled notices; user-installed, excluded |
| [Pedalboard](https://github.com/spotify/pedalboard) | GPLv3, with JUCE and other dependencies; optional, user-installed in a separate Python environment, excluded |
| [NumPy](https://numpy.org/doc/stable/license.html) | BSD-3-Clause plus bundled dependency notices; user-installed, excluded |

The Dreamy worker is a separate original MIT Python script. Its use of Pedalboard does not make Pedalboard MIT. If you redistribute a combined worker environment or modified dependencies, review GPL obligations including corresponding source and license notices; a process boundary alone is not a blanket license exemption. Forma deliberately does not bundle that environment. Do not add external binaries to a public release without satisfying their license/source obligations. Codec patent obligations can vary by jurisdiction and use.

No Wavely code or artwork is included. Its screenshot was a user-supplied visual reference. The five supplied Python utilities informed behavior and presets; their console interfaces and automatic package installers are not shipped. Test fixtures are synthetic FFmpeg signals. The optional network test downloads the Blender-hosted Big Buck Bunny trailer for local verification; that trailer is not included in release packages.
