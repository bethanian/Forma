using System.Globalization;
using System.Text.Json;

namespace Forma.Core;

public sealed class MediaEngine(Dependencies Dependencies, string WorkerPath)
{
    private readonly ProcessRunner Runner = new();
    public static void Validate(JobRequest Request)
    {
        if (!Enum.IsDefined(Request.Tool)) throw new ArgumentException("Unsupported tool.");
        if (string.IsNullOrWhiteSpace(Request.Destination) || !Path.IsPathFullyQualified(Request.Destination)) throw new ArgumentException("Choose an absolute output folder.");
        if (Request.Tool == ToolKind.Download)
        {
            if (!Uri.TryCreate(Request.Input, UriKind.Absolute, out var Url) || Url.Scheme is not ("http" or "https") || Url.HostNameType != UriHostNameType.Dns || !string.IsNullOrEmpty(Url.UserInfo) || Url.IsLoopback)
                throw new ArgumentException("Enter a public HTTP or HTTPS media URL.");
            if (string.IsNullOrWhiteSpace(Request.DownloadFormat) || Request.DownloadFormat.Length > 512) throw new ArgumentException("Choose a download format.");
        }
        else if (!File.Exists(Request.Input)) throw new FileNotFoundException("The input file no longer exists.", Request.Input);
        if (!new[] { "mp3", "wav", "flac", "aac", "mp4", "mkv", "webm" }.Contains(Request.Format)) throw new ArgumentException("Unsupported output format.");
        if (Request.Quality is < 0 or > 51 || Request.Bitrate is < 64 or > 320 || !new[] { 0, 2160, 1440, 1080, 720, 480 }.Contains(Request.Height)) throw new ArgumentException("Quality or resolution is outside the supported range.");
        if (Request.Codec is not ("libx264" or "libx265") || Request.Speed is not ("fast" or "medium" or "slow")) throw new ArgumentException("Unsupported video encoder or speed.");
        Request.Effects?.Validate();
    }
    private List<string> DownloaderArguments()
    {
        var Arguments = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--socket-timeout", "30", "--retries", "3", "--fragment-retries", "3", "--no-overwrites", "--windows-filenames", "--ffmpeg-location", Path.GetDirectoryName(Dependencies.Resolve("ffmpeg"))! };
        try { Arguments.AddRange(["--js-runtimes", "deno:" + Dependencies.Resolve("deno")]); } catch (FileNotFoundException) { }
        return Arguments;
    }
    public async Task<IReadOnlyList<DownloadChoice>> InspectAsync(string Url, CancellationToken Cancellation)
    {
        Validate(new(ToolKind.Download, Url, Path.GetTempPath()));
        var Arguments = DownloaderArguments();
        Arguments.AddRange(["--dump-single-json", "--skip-download", "--", Url]);
        var Json = await Runner.RunAsync(Dependencies.Resolve("yt-dlp"), Arguments, null, Cancellation, TimeSpan.FromMinutes(2));
        using var Document = JsonDocument.Parse(Json);
        var Choices = new List<DownloadChoice> { new("bv*+ba/b", "Best available video"), new("audio", "Audio only · MP3") };
        if (Document.RootElement.TryGetProperty("formats", out var Formats))
            foreach (var Format in Formats.EnumerateArray())
            {
                if (!Format.TryGetProperty("format_id", out var Id) || !Format.TryGetProperty("vcodec", out var Video) || Video.GetString() is null or "none") continue;
                var Height = Format.TryGetProperty("height", out var H) ? H.ToString() : "?";
                var Extension = Format.GetProperty("ext").GetString();
                var HasAudio = Format.TryGetProperty("acodec", out var Audio) && Audio.GetString() is not (null or "none");
                Choices.Add(new(Id.GetString()! + (HasAudio ? "" : "+ba/b"), $"{Height}p · {Extension} · {Id.GetString()}"));
            }
        return Choices;
    }
    public async Task<MediaInfo> ProbeAsync(string PathName, CancellationToken Cancellation)
    {
        var Json = await Runner.RunAsync(Dependencies.Resolve("ffprobe"), ["-v", "error", "-show_entries", "format=duration,size:stream=codec_type", "-of", "json", PathName], null, Cancellation, TimeSpan.FromSeconds(30));
        using var Document = JsonDocument.Parse(Json);
        var Format = Document.RootElement.GetProperty("format");
        var Duration = Format.TryGetProperty("duration", out var D) && double.TryParse(D.GetString(), CultureInfo.InvariantCulture, out var Value) ? Value : 0;
        var Streams = Document.RootElement.GetProperty("streams").EnumerateArray().Select(Stream => Stream.GetProperty("codec_type").GetString()).ToArray();
        return new(Duration, new FileInfo(PathName).Length, Streams.Contains("video"), Streams.Contains("audio"));
    }
    public static List<string> EncodingArguments(JobRequest Request, string Input, string Output)
    {
        var Arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-i", Input };
        if (Request.Preview) Arguments.AddRange(["-t", "15"]);
        var AudioOnly = Request.Format is "mp3" or "wav" or "flac" or "aac";
        if (AudioOnly) Arguments.AddRange(["-vn", "-map", "0:a:0"]);
        else
        {
            Arguments.AddRange(["-map", "0:v:0", "-map", "0:a:0?", "-c:v", Request.Format == "webm" ? "libvpx-vp9" : Request.Codec, "-crf", Request.Quality.ToString(CultureInfo.InvariantCulture)]);
            if (Request.Format == "webm") Arguments.AddRange(["-b:v", "0"]);
            else Arguments.AddRange(["-preset", Request.Speed, "-pix_fmt", "yuv420p"]);
            Arguments.AddRange(["-vf", Request.Height > 0 ? $"scale=w='min(iw,{Request.Height * 16 / 9})':h='min(ih,{Request.Height})':force_original_aspect_ratio=decrease:force_divisible_by=2" : "scale=trunc(iw/2)*2:trunc(ih/2)*2"]);
            if (Request.Format == "mp4") Arguments.AddRange(["-movflags", "+faststart"]);
        }
        var Codec = Request.Format switch { "mp3" => "libmp3lame", "wav" => "pcm_s16le", "flac" => "flac", "webm" => "libopus", _ => "aac" };
        Arguments.AddRange(["-c:a", Codec]);
        if (Codec is not ("pcm_s16le" or "flac")) Arguments.AddRange(["-b:a", $"{Request.Bitrate}k"]);
        Arguments.AddRange(["-progress", "pipe:1", "-nostats", Output]);
        return Arguments;
    }
    public async Task ExecuteAsync(MediaJob Job, Action<double, string> Report, CancellationToken Cancellation)
    {
        var Request = Job.Request;
        Validate(Request);
        Directory.CreateDirectory(Request.Destination);
        var Root = Path.GetPathRoot(Request.Destination)!;
        var InputSize = Request.Tool == ToolKind.Download ? 0 : new FileInfo(Request.Input).Length;
        if (new DriveInfo(Root).AvailableFreeSpace < Math.Max(128L * 1024 * 1024, InputSize)) throw new IOException("The output drive has insufficient free space.");
        var Staging = Path.Combine(Request.Destination, ".forma-" + Job.Id);
        Directory.CreateDirectory(Staging);
        try
        {
            string Output;
            if (Request.Tool == ToolKind.Download)
            {
                var Arguments = DownloaderArguments();
                Arguments.AddRange(["--newline", "--progress-template", "download:FORMA_PROGRESS:%(progress._percent_str)s", "--print", "after_move:FORMA_OUTPUT:%(filepath)s", "-o", Path.Combine(Staging, "%(title).120B [%(id)s].%(ext)s")]);
                if (Request.DownloadFormat == "audio") Arguments.AddRange(["-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0"]);
                else Arguments.AddRange(["-f", Request.DownloadFormat, "--merge-output-format", "mp4"]);
                Arguments.AddRange(["--", Request.Input]);
                Output = "";
                await Runner.RunAsync(Dependencies.Resolve("yt-dlp"), Arguments, Line =>
                {
                    if (Line.StartsWith("FORMA_OUTPUT:")) Output = Line[13..].Trim();
                    if (Line.StartsWith("FORMA_PROGRESS:") && double.TryParse(Line[15..].Trim().TrimEnd('%'), CultureInfo.InvariantCulture, out var Percent)) Report(Math.Min(95, Percent * .95), "Downloading");
                }, Cancellation, Capture: false);
                if (string.IsNullOrWhiteSpace(Output) || !Path.GetFullPath(Output).StartsWith(Path.GetFullPath(Staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Downloader did not report a completed media file.");
            }
            else
            {
                var Info = await ProbeAsync(Request.Input, Cancellation);
                Job.InputBytes = Info.Size;
                if (!Info.HasAudio && (Request.Tool == ToolKind.AudioEffects || Request.Format is "mp3" or "wav" or "flac" or "aac")) throw new IOException("This file has no audio stream.");
                if (!Info.HasVideo && Request.Format is "mp4" or "mkv" or "webm") throw new IOException("Choose an audio output format for an audio-only input.");
                Output = Path.Combine(Staging, Path.GetFileNameWithoutExtension(Request.Input) + (Request.Tool == ToolKind.AudioEffects ? "_dreamy" : Request.Tool == ToolKind.Compress ? "_compressed" : "_converted") + "." + Request.Format);
                var Source = Request.Input;
                if (Request.Tool == ToolKind.AudioEffects)
                {
                    var Required = Info.Duration * 48000 * 2 * 4 * (1 + 2 / (Request.Effects?.Speed ?? .92));
                    if (new DriveInfo(Root).AvailableFreeSpace < Required + 128 * 1024 * 1024) throw new IOException("Insufficient free space for Dreamy's intermediate audio files.");
                    Source = Path.Combine(Staging, "decoded.wav");
                    var Decode = new List<string> { "-v", "error", "-nostdin", "-n", "-i", Request.Input };
                    if (Request.Preview) Decode.AddRange(["-t", "15"]);
                    Decode.AddRange(["-vn", "-ac", "2", "-ar", "48000", "-c:a", "pcm_f32le", Source]);
                    Report(0, "Decoding audio");
                    await Runner.RunAsync(Dependencies.Resolve("ffmpeg"), Decode, null, Cancellation);
                    var Processed = Path.Combine(Staging, "processed.wav");
                    var Options = Path.Combine(Staging, "effects.json");
                    await File.WriteAllTextAsync(Options, JsonSerializer.Serialize(Request.Effects ?? new()), Cancellation);
                    await Runner.RunAsync(Dependencies.Resolve("python"), [WorkerPath, Source, Processed, Options], Line =>
                    {
                        if (Line.StartsWith("PROGRESS:") && double.TryParse(Line[9..], CultureInfo.InvariantCulture, out var Percent)) Report(5 + Percent * .8, "Applying effects");
                    }, Cancellation, Capture: false);
                    Source = Processed;
                }
                await Runner.RunAsync(Dependencies.Resolve("ffmpeg"), EncodingArguments(Request with { Preview = false }, Source, Output), Line =>
                {
                    if (Line.StartsWith("out_time_us=") && double.TryParse(Line[12..], CultureInfo.InvariantCulture, out var Microseconds) && Info.Duration > 0)
                    {
                        var Fraction = Math.Clamp(Microseconds / 1_000_000 / Info.Duration, 0, 1);
                        Report(Request.Tool == ToolKind.AudioEffects ? 85 + Fraction * 10 : Fraction * 95, "Encoding");
                    }
                }, Cancellation, Capture: false);
            }
            Cancellation.ThrowIfCancellationRequested();
            Report(97, "Validating output");
            var Result = await ProbeAsync(Output, Cancellation);
            if (Result.Size == 0 || !Result.HasAudio && !Result.HasVideo || Result.Duration <= 0) throw new IOException("Output validation failed. No completed file was published.");
            if (!new[] { ".mp4", ".mkv", ".webm", ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus", ".mov", ".avi", ".m4v" }.Contains(Path.GetExtension(Output).ToLowerInvariant())) throw new IOException("The downloaded file has an unsupported media extension.");
            Cancellation.ThrowIfCancellationRequested();
            Job.OutputBytes = Result.Size;
            Job.Output = Publish(Output, Request.Destination);
            var Difference = Job.InputBytes > 0 ? (1 - (double)Job.OutputBytes / Job.InputBytes) * 100 : 0;
            Job.Message = Request.Tool == ToolKind.Compress ? $"{Size(Job.InputBytes)} → {Size(Job.OutputBytes)} · {(Difference >= 0 ? $"{Difference:F1}% smaller" : $"{-Difference:F1}% larger")}" : $"Saved · {Size(Job.OutputBytes)}";
            Report(100, Job.Message);
        }
        finally { try { Directory.Delete(Staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public static string Publish(string Source, string Destination)
    {
        var Name = Path.GetFileNameWithoutExtension(Source);
        var Extension = Path.GetExtension(Source);
        for (var Index = 0; Index < 10000; Index++)
        {
            var Target = Path.Combine(Destination, Name + (Index == 0 ? "" : $" ({Index})") + Extension);
            try { File.Move(Source, Target, false); return Target; }
            catch (IOException) when (File.Exists(Target)) { }
        }
        throw new IOException("Too many files with the same output name.");
    }
    public static string Size(long Bytes) => Bytes >= 1024 * 1024 ? $"{Bytes / 1048576d:F1} MB" : $"{Bytes / 1024d:F1} KB";
}
