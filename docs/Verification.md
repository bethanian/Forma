# Verification record

## Forma 1.1 — 2026-11-05

The reported YouTube failure was traced to the executable actually resolved from PATH: yt-dlp 2026.06.09. The failed job had HTTP 403 in its stored details. Installing the official stable 2026.10.26 build with its publisher SHA-256 fixed the same public URL. The original globally installed executable was preserved; Forma now resolves its managed tool copy ahead of PATH and reports outdated versions before starting work.

Live integration checks passed for official release download/checksum/install in a test folder, public media inspection/download, YouTube format inspection, a complete 360p video/audio download and merge, and full MP3 extraction. The reproduced video contains both streams and has a probed duration of 746.214 seconds. No cookie extraction, alternate account, access-control bypass or client impersonation was used. Instagram remains unverified.

The final combined integration run passed **123 checks**, including the live workflows and new audio fixture; its local report is `artifacts/tests/20261105-232915/results.json`. The audio fixture measures all six new presets on an original eight-second stereo chord progression: finite samples, approximately -1 dBFS sample peak, duration including tails, silent endpoints, positive stereo correlation, bounded block-boundary jumps, actual high-frequency attenuation for Underwater, increased bass balance for Midnight Drive, and silent mono input. The original six preset choices also passed timing/headroom checks. Saved 1.0 effects deserialize with their original defaults for newly added controls. The app built with zero warnings/errors and was relaunched from the updated local executable. The resolved NuGet dependency audit reported no known vulnerable packages at verification time.

These are signal and behavior checks, not a subjective listening certification. No commercial song is included in the repository, test fixtures or release. See [AudioEffects.md](AudioEffects.md) for the new sound directions and their limitations.

## Forma 1.0 — 2026-10-30

Environment: Windows x64, .NET SDK 10.0.403, Windows App SDK 1.8.260921001, FFmpeg/ffprobe 9.0.2, Python 3.14, Pedalboard 0.9.26, NumPy 2.3.4. Local test dependencies are isolated under `.tools` where practical; system media installations are not modified.

The integration suite completed **85 checks**, with its machine-readable report at `artifacts/tests/20261030-192431/results.json` in the development workspace. It generated stereo sine audio and video-with-audio fixtures rather than using private media.

Verified with actual processing:

- MP3, WAV, FLAC, AAC, MP4, MKV and WebM conversion; video-to-MP3 extraction.
- H.264 and H.265 compression and resolution-bound handling.
- All six Dreamy choices, including Custom defaults; output duration matches slowdown within 30ms. NumPy verified finite samples and peak below 0.9 / approximately -1 dBFS for these fixtures.
- Input hashes remain unchanged; output collisions retain both files; per-job staging folders are removed.
- Invalid/missing input, audio-only input requested as video, invalid URLs and nonfinite effect values are rejected.
- A running external process is cancelled within five seconds; process timeout kills it.
- Queue cancellation, serial batch completion, SQLite persistence and interrupted-job cleanup/retry state.
- Actual format inspection and download of `https://download.blender.org/peach/trailer/trailer_400p.ogg`, followed by ffprobe validation and safe publication, using official yt-dlp 2026.10.26. The isolated executable's SHA-256 matched the publisher's checksum list.
- YouTube metadata/available-format extraction for `aqz-KE-bpKQ` with an explicit Deno runtime using the same official yt-dlp release. The installed older yt-dlp executable was left unchanged.

UI verification used the running WinUI app through the Windows computer-use skill: native file picker, adding a real video, queueing a conversion, persistence after restart, retrying a missing-dependency failure and seeing the completed MP3 in History. An actual Dreamy preview was generated and native playback advanced to its two-second end, with seek and play controls visible. The first app run exposed a stale inherited PATH; resolution now includes the saved user/machine PATH. Real screenshots were inspected against the Wavely reference. Dark and light surfaces/title bars were verified. The actual self-contained portable app was launched successfully into its last-used tool, with history bindings verified. Packaging checks caught omitted XAML resources; the project now publishes its PRI and XBF explicitly. An unpackaged-incompatible contrast event subscription was removed; contrast colors are read on activation.

Known verification boundaries:

- One initial W3C-hosted media URL returned a Cloudflare access-denied error. It was reported as failure; no bypass was attempted. A different public Blender-hosted fixture passed.
- A removed yt-dlp YouTube test video returned unavailable. A different public YouTube video passed metadata extraction. A complete YouTube download/merge and an Instagram download have not been verified in this environment.
- Tests use short synthetic media, not every codec, hours-long workloads, exhausted drives, removable-drive failures or every provider/network condition. There is a preflight free-space check and filesystem/process error handling, but hardware failures were not simulated.
- Peak checks cover the tested WAV fixtures; they are not a promise about perceived loudness, lossy true-peak overshoot or every musical input. No subjective listening evaluation was performed.
- Windows accessibility names and native keyboard/focus behavior were inspected through UI Automation; a full screen-reader audit and multiple physical DPI/monitor configurations were not performed.
- Releases are portable x64 ZIPs, unsigned. Signing, Microsoft Store submission, OS toast registration, playlists and authenticated/cookie-based downloading are not included.

The final UI pass keeps preset/format controls on one row, exposes primary actions at the default size, uses filename labels with full-path tooltips, and hides completed progress bars. A second launch was verified to leave one running application process. NuGet's vulnerability audit reported no known vulnerable packages for the resolved application dependencies at verification time. This does not cover the independently installed media tools.

Release artifact checks: the source ZIP was extracted into a separate directory and built successfully with **0 warnings and 0 errors**. The portable ZIP was separately extracted and that executable launched successfully with Audio effects, Export and Preview present. The published ZIP's SHA-256 matched its checksum file. Final self-contained portable archive size is approximately 87.5 MB; the much smaller source archive excludes toolchains, media dependencies, generated test files and build outputs.

Reproduce checks using the commands in README. A passing test means the specific measured behavior above, not blanket production certification.
