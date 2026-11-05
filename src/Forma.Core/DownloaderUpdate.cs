using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forma.Core;

public static partial class DownloaderUpdate
{
    public static Version MinimumVersion { get; } = new(2026, 10, 26);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly SemaphoreSlim Gate = new(1);
    public static async Task<string> InstallAsync(string Folder, Action<string>? Report = null, CancellationToken Cancellation = default)
    {
        await Gate.WaitAsync(Cancellation);
        string? Temporary = null;
        try
        {
            Report?.Invoke("Checking the official yt-dlp release…");
            using var Request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
            Request.Headers.UserAgent.ParseAdd("Forma/1.1");
            using var Response = await Client.SendAsync(Request, Cancellation);
            Response.EnsureSuccessStatusCode();
            using var Release = JsonDocument.Parse(await Response.Content.ReadAsStringAsync(Cancellation));
            var Tag = Release.RootElement.GetProperty("tag_name").GetString()!;
            if (!Version.TryParse(Tag, out var ReleaseVersion) || ReleaseVersion < MinimumVersion || Release.RootElement.GetProperty("draft").GetBoolean() || Release.RootElement.GetProperty("prerelease").GetBoolean()) throw new IOException("The publisher did not return a supported stable yt-dlp release.");
            var BaseUrl = $"https://github.com/yt-dlp/yt-dlp/releases/download/{Uri.EscapeDataString(Tag)}/";
            var Checksums = await Client.GetStringAsync(BaseUrl + "SHA2-256SUMS", Cancellation);
            var Match = ChecksumLine().Match(Checksums);
            if (!Match.Success) throw new IOException("The official release is missing its Windows checksum.");
            Directory.CreateDirectory(Folder);
            Temporary = Path.Combine(Folder, ".yt-dlp-" + Guid.NewGuid().ToString("N") + ".tmp");
            Report?.Invoke($"Downloading yt-dlp {Tag}…");
            using (var Download = await Client.GetAsync(BaseUrl + "yt-dlp.exe", HttpCompletionOption.ResponseHeadersRead, Cancellation))
            {
                Download.EnsureSuccessStatusCode();
                if (Download.Content.Headers.ContentLength > 64 * 1024 * 1024) throw new IOException("The downloader release exceeds the supported size.");
                await using var Source = await Download.Content.ReadAsStreamAsync(Cancellation);
                await using var Target = new FileStream(Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                var Buffer = new byte[65536];
                int Count;
                while ((Count = await Source.ReadAsync(Buffer, Cancellation)) > 0)
                {
                    if (Target.Length + Count > 64 * 1024 * 1024) throw new IOException("The downloader release exceeds the supported size.");
                    await Target.WriteAsync(Buffer.AsMemory(0, Count), Cancellation);
                }
            }
            Report?.Invoke("Verifying the publisher's SHA-256 checksum…");
            await using (var File = System.IO.File.OpenRead(Temporary))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(File, Cancellation)).Equals(Match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) throw new IOException("yt-dlp checksum verification failed. The installed tool was preserved.");
            Cancellation.ThrowIfCancellationRequested();
            try { File.Move(Temporary, Path.Combine(Folder, "yt-dlp.exe"), true); }
            catch (IOException Error) { throw new IOException("Could not replace yt-dlp. Finish running downloads and try again. " + Error.Message, Error); }
            return Tag;
        }
        finally
        {
            try { if (Temporary is not null) File.Delete(Temporary); } finally { Gate.Release(); }
        }
    }
    [GeneratedRegex(@"^([a-fA-F0-9]{64})[ \t]+\*?yt-dlp\.exe[ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex ChecksumLine();
}
