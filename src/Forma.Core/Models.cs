using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Forma.Core;

public enum ToolKind { Download, Compress, Convert, AudioEffects }
public enum JobState { Queued, Running, Completed, Failed, Cancelled, Interrupted }
public sealed record EffectOptions(double Speed = .92, double Wet = .6, double Room = .85, double Damping = .25, double Lowpass = 10000, double Width = 1.4, double Chorus = .15)
{
    public static IReadOnlyDictionary<string, EffectOptions> Presets { get; } = new Dictionary<string, EffectOptions>
    {
        ["Classic Arena"] = new(), ["Deep Space"] = new(.8, .8, .97, .1, 8000, 1.8, .25),
        ["Rainy Parking Lot"] = new(.95, .5, .7, .6, 6000, 1.1, .05), ["Cathedral"] = new(.88, .7, .95, .15, 11000, 1.6, .2),
        ["Bathroom at 3AM"] = new(.97, .45, .4, .8, 12000, .9, .08), ["Custom"] = new()
    };
    public void Validate()
    {
        if (!double.IsFinite(Speed + Wet + Room + Damping + Lowpass + Width + Chorus) || Speed is < .7 or > 1 || Wet is < 0 or > 1 || Room is < 0 or > 1 || Damping is < 0 or > 1 || Lowpass is < 2000 or > 20000 || Width is < .5 or > 2.5 || Chorus is < 0 or > 1)
            throw new ArgumentException("Effect values are outside the supported range.");
    }
}
public sealed record JobRequest(ToolKind Tool, string Input, string Destination, string Format = "mp4", string Codec = "libx264", int Quality = 25, int Height = 0, string Speed = "medium", int Bitrate = 192, string DownloadFormat = "bv*+ba/b", EffectOptions? Effects = null, bool Preview = false);
public sealed class MediaJob : INotifyPropertyChanged
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required JobRequest Request { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.Now;
    public JobState State { get; set; } = JobState.Queued;
    public double Progress { get; set; }
    public string Message { get; set; } = "Waiting";
    public string Output { get; set; } = "";
    public long InputBytes { get; set; }
    public long OutputBytes { get; set; }
    public string Name => Request.Tool == ToolKind.Download ? Request.Input : Path.GetFileName(Request.Input);
    public string Summary => $"{Request.Tool.ToString().Replace("AudioEffects", "Audio effects")} · {Created.LocalDateTime:g}";
    public string Status => $"{State} · {Message}";
    public bool CanRetry => State is JobState.Failed or JobState.Cancelled or JobState.Interrupted;
    public bool IsActive => State is JobState.Queued or JobState.Running;
    public bool IsIndeterminate => IsActive && Progress <= 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() { foreach (var Name in new[] { nameof(State), nameof(Progress), nameof(Message), nameof(Output), nameof(Status), nameof(CanRetry), nameof(IsActive), nameof(IsIndeterminate) }) PropertyChanged?.Invoke(this, new(Name)); }
}
public sealed class AppSettings
{
    public string Theme { get; set; } = "Default";
    public string LastTool { get; set; } = "Download";
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Forma");
    public string ToolFolder { get; set; } = "";
    public string PythonPath { get; set; } = "python.exe";
    public int DefaultBitrate { get; set; } = 192;
    public int DefaultCrf { get; set; } = 25;
    public bool CompletionSound { get; set; }
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forma");
    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(DataFolder, "settings.json"))) ?? new(); }
        catch (Exception Error) when (Error is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        var PathName = Path.Combine(DataFolder, "settings.json");
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(this));
        File.Move(PathName + ".tmp", PathName, true);
    }
}
public sealed record MediaInfo(double Duration, long Size, bool HasVideo, bool HasAudio);
public sealed record DownloadChoice(string Id, string Label) { public override string ToString() => Label; }
