# Forma architecture and delivery

Three passes: foundation (native shell, durable jobs, safe process boundary), build (four processing tools and history/settings), polish (real workflow verification, visual review, compact interactions and portable packaging).

`Forma.App` is a Windows 11 WinUI 3 desktop application. `Forma.Core` owns immutable job requests, SQLite persistence, sequential background orchestration, process lifetime, media probing and engine arguments. `worker` is an isolated, optional Python/Pedalboard Dreamy processor. The UI dispatches snapshots onto its own thread; engines never touch controls.

Jobs are persisted before entering the queue. On restart, pending/running jobs become Interrupted and can be explicitly retried. Cancellation kills the process tree and cleans the job's unique staging directory. Output moves within the destination volume only after ffprobe validation; existing files are never replaced. Each batch input is a distinct cancellable job. Sequential processing limits resource pressure. URLs are passed as arguments after `--`, never through a shell. yt-dlp configuration and plugins are disabled. The explicit downloader updater reads the official stable GitHub release, verifies SHA-256 before atomic replacement in local app data, and preserves the previous tool on failure/cancellation. Custom tool folders take precedence. Downloader preflight checks a supported executable version and a JavaScript runtime for YouTube.

Portable unpackaged deployment uses self-contained .NET and Windows App SDK. External media tools are user-managed rather than silently downloaded or bundled with incomplete redistribution notices. Dreamy is an independent process using GPLv3 Pedalboard; original application code is MIT, with no claim that third-party binaries are MIT.

Official references consulted 2026-10-30:
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
- https://github.com/microsoft/WindowsAppSDK/releases
- https://github.com/yt-dlp/yt-dlp and https://github.com/yt-dlp/yt-dlp/wiki/EJS
- https://ffmpeg.org/legal.html
- https://github.com/spotify/pedalboard

Design: native title bar; 180px navigation; 32px workspace inset; 4/8/12/16/24/32 spacing; Segoe UI Variable; theme resources; subtle borders; native focus/hover/motion. No dashboard or recording. Advanced encoding/effect controls are collapsed. Motion follows system animation preference through native control transitions.
