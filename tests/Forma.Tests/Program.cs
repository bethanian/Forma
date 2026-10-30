using System.Diagnostics;
using System.Text.Json;
using Forma.Core;

var Workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var Root = Path.Combine(Workspace, "artifacts", "tests", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(Root);
var ToolFolder = Environment.GetEnvironmentVariable("FORMA_TEST_TOOLS") ?? throw new Exception("Set FORMA_TEST_TOOLS to the FFmpeg bin directory.");
var PythonPath = Path.Combine(Workspace, ".tools", "dreamy", "Scripts", "python.exe");
var Settings = new AppSettings { ToolFolder = Environment.GetEnvironmentVariable("FORMA_TEST_DOWNLOAD_TOOLS") ?? ToolFolder, PythonPath = PythonPath };
var Dependencies = new Dependencies(Settings);
var Engine = new MediaEngine(Dependencies, Path.Combine(Workspace, "worker", "dreamy_worker.py"));
var Runner = new ProcessRunner();
var Passed = new List<string>();
void Check(bool Condition, string Name) { if (!Condition) throw new Exception("FAIL: " + Name); Passed.Add(Name); Console.WriteLine("PASS: " + Name); }
async Task RejectAsync(Func<Task> Action, string Name)
{
    try { await Action(); } catch (Exception Error) when (Error is IOException or ArgumentException or OperationCanceledException) { Check(true, Name); return; }
    throw new Exception("Expected failure: " + Name);
}
var Audio = Path.Combine(Root, "source with spaces & ü.wav");
var Video = Path.Combine(Root, "source.mp4");
await Runner.RunAsync(Dependencies.Resolve("ffmpeg"), ["-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-ac", "2", Audio], null, default);
await Runner.RunAsync(Dependencies.Resolve("ffmpeg"), ["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=24:duration=2", "-f", "lavfi", "-i", "sine=frequency=660:duration=2", "-c:v", "libx264", "-c:a", "aac", "-shortest", Video], null, default);
var SourceHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Audio));
async Task<MediaJob> ProcessAsync(JobRequest Request)
{
    var Job = new MediaJob { Request = Request };
    await Engine.ExecuteAsync(Job, (Value, Message) => { Job.Progress = Value; Job.Message = Message; }, default);
    Check(File.Exists(Job.Output) && Job.OutputBytes > 0 && Job.Progress == 100, $"{Request.Tool} {Request.Format} {Request.Codec} output");
    var Info = await Engine.ProbeAsync(Job.Output, default);
    Check(Info.Duration > 1.5 && Info.Duration < 4, "Output duration valid");
    Check(!Directory.Exists(Path.Combine(Request.Destination, ".forma-" + Job.Id)), "Staging cleaned");
    return Job;
}
foreach (var Format in new[] { "mp3", "wav", "flac", "aac", "mp4", "mkv", "webm" })
    await ProcessAsync(new(ToolKind.Convert, Format is "mp4" or "mkv" or "webm" ? Video : Audio, Root, Format));
await ProcessAsync(new(ToolKind.Convert, Video, Root, "mp3"));
foreach (var Codec in new[] { "libx264", "libx265" }) await ProcessAsync(new(ToolKind.Compress, Video, Root, Codec: Codec, Height: 480));
foreach (var Preset in EffectOptions.Presets)
{
    var Job = await ProcessAsync(new(ToolKind.AudioEffects, Audio, Root, "wav", Effects: Preset.Value));
    var Info = await Engine.ProbeAsync(Job.Output, default);
    Check(Math.Abs(Info.Duration - 2 / Preset.Value.Speed) < .03, Preset.Key + " slowdown");
    var Peak = await Runner.RunAsync(PythonPath, ["-c", "import sys,numpy as n; from pedalboard.io import AudioFile;\nf=AudioFile(sys.argv[1]); a=f.read(f.frames); print(float(n.max(n.abs(a)))); assert n.isfinite(a).all(); assert n.max(n.abs(a)) < .9; assert n.max(n.abs(a)) > .85", Job.Output], null, default);
    Check(double.Parse(Peak.Trim(), System.Globalization.CultureInfo.InvariantCulture) < .9, Preset.Key + " finite audio / -1 dBFS headroom");
}
Check(SourceHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Audio))), "Original source unchanged");
var First = await ProcessAsync(new(ToolKind.Convert, Audio, Root, "mp3"));
var Second = await ProcessAsync(new(ToolKind.Convert, Audio, Root, "mp3"));
Check(First.Output != Second.Output && File.Exists(First.Output), "Collision creates unique output");
await RejectAsync(() => Engine.ExecuteAsync(new() { Request = new(ToolKind.Convert, Audio, Root, "mp4") }, (_, _) => { }, default), "Reject audio-only input for video output");
await RejectAsync(() => Engine.ExecuteAsync(new() { Request = new(ToolKind.Convert, Path.Combine(Root, "missing.wav"), Root) }, (_, _) => { }, default), "Missing input");
await File.WriteAllTextAsync(Path.Combine(Root, "invalid.mp4"), "not media");
await RejectAsync(() => Engine.ExecuteAsync(new() { Request = new(ToolKind.Convert, Path.Combine(Root, "invalid.mp4"), Root) }, (_, _) => { }, default), "Invalid media");
foreach (var Url in new[] { "file:///C:/test", "https://localhost/test", "https://user:password@example.com", "-o hacked" })
    await RejectAsync(() => { MediaEngine.Validate(new(ToolKind.Download, Url, Root)); return Task.CompletedTask; }, "Reject unsafe URL " + Url.Split(':')[0]);
await RejectAsync(() => { new EffectOptions(Speed: double.NaN).Validate(); return Task.CompletedTask; }, "Reject nonfinite effects");
var Cancellation = new CancellationTokenSource(250);
var Timer = Stopwatch.StartNew();
await RejectAsync(() => Runner.RunAsync(Dependencies.Resolve("ffmpeg"), ["-v", "error", "-re", "-f", "lavfi", "-i", "sine=duration=60", "-f", "null", "-"], null, Cancellation.Token), "Process cancellation");
Check(Timer.Elapsed < TimeSpan.FromSeconds(5), "Cancellation promptly kills process");
await RejectAsync(() => Runner.RunAsync(Dependencies.Resolve("ffmpeg"), ["-v", "error", "-re", "-f", "lavfi", "-i", "sine=duration=60", "-f", "null", "-"], null, default, TimeSpan.FromMilliseconds(250)), "Process timeout");
var Store = new JobStore(Path.Combine(Root, "db"));
var Interrupted = new MediaJob { Request = new(ToolKind.Convert, Audio, Root, "mp3"), State = JobState.Running };
Store.Save(Interrupted);
var Staging = Path.Combine(Root, ".forma-" + Interrupted.Id); Directory.CreateDirectory(Staging); File.WriteAllText(Path.Combine(Staging, "partial.mp3"), "partial");
var Queue = new JobQueue(Store, Engine);
Check(Queue.Jobs.Single().State == JobState.Interrupted && !Directory.Exists(Staging), "Restart recovery and staging cleanup");
var Queued = new[] { Queue.Enqueue(new(ToolKind.Convert, Audio, Root, "flac")), Queue.Enqueue(new(ToolKind.Convert, Audio, Root, "mp3")) };
var Deadline = DateTime.UtcNow.AddSeconds(30);
while (Queued.Any(Job => Job.State is JobState.Queued or JobState.Running) && DateTime.UtcNow < Deadline) await Task.Delay(50);
Check(Queued.All(Job => Job.State == JobState.Completed), "Batch queue completes");
Check(new JobStore(Path.Combine(Root, "db")).Load().Count(Job => Job.State == JobState.Completed) == 2, "History persists completed jobs");
var Slow = Queue.Enqueue(new(ToolKind.Compress, Video, Root, Speed: "slow")); Queue.Cancel(Slow.Id);
await Task.Delay(500);
Check(Slow.State == JobState.Cancelled, "Queued job cancellation");
if (Environment.GetEnvironmentVariable("FORMA_NETWORK_TESTS") == "1")
{
    var Url = "https://download.blender.org/peach/trailer/trailer_400p.ogg";
    var Choices = await Engine.InspectAsync(Url, default);
    Check(Choices.Count > 1, "Live public URL format inspection");
    var Download = new MediaJob { Request = new(ToolKind.Download, Url, Root) };
    await Engine.ExecuteAsync(Download, (_, _) => { }, default);
    Check(File.Exists(Download.Output) && Download.OutputBytes > 1000, "Live yt-dlp download, validation and publication");
}
await File.WriteAllTextAsync(Path.Combine(Root, "results.json"), JsonSerializer.Serialize(new { Passed = Passed.Count, Checks = Passed }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{Passed.Count} checks passed. Artifacts: {Root}");
