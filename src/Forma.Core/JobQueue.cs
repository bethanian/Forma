using System.Threading.Channels;

namespace Forma.Core;

public sealed class JobQueue
{
    private readonly JobStore Store;
    private readonly MediaEngine Engine;
    private readonly Channel<MediaJob> Channel = System.Threading.Channels.Channel.CreateUnbounded<MediaJob>(new() { SingleReader = true });
    private readonly Dictionary<string, CancellationTokenSource> Tokens = [];
    private readonly Lock Gate = new();
    private readonly List<MediaJob> Items;
    public event Action<MediaJob>? Changed;
    public IReadOnlyList<MediaJob> Jobs { get { lock (Gate) return Items.ToArray(); } }
    public JobQueue(JobStore Store, MediaEngine Engine)
    {
        this.Store = Store; this.Engine = Engine;
        Items = Store.Load().ToList();
        foreach (var Job in Items.Where(Job => Job.State is JobState.Running or JobState.Queued))
        {
            Job.State = JobState.Interrupted; Job.Message = "Forma closed before this job finished. Retry to start again.";
            Store.Save(Job);
            var Staging = Path.Combine(Job.Request.Destination, ".forma-" + Job.Id);
            if (Job.Id.Length == 32 && Job.Id.All(Uri.IsHexDigit))
                try { if (Directory.Exists(Staging)) Directory.Delete(Staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        _ = Task.Run(ConsumeAsync);
    }
    public MediaJob Enqueue(JobRequest Request)
    {
        MediaEngine.Validate(Request);
        var Job = new MediaJob { Request = Request };
        Store.Save(Job);
        lock (Gate) { Items.Insert(0, Job); Tokens.Add(Job.Id, new()); }
        Changed?.Invoke(Job);
        Channel.Writer.TryWrite(Job);
        return Job;
    }
    public void Cancel(string Id) { lock (Gate) if (Tokens.TryGetValue(Id, out var Token)) Token.Cancel(); }
    public void CancelAll() { lock (Gate) foreach (var Token in Tokens.Values) Token.Cancel(); }
    private async Task ConsumeAsync()
    {
        await foreach (var Job in Channel.Reader.ReadAllAsync())
        {
            CancellationTokenSource Token;
            lock (Gate) Token = Tokens[Job.Id];
            try
            {
                Token.Token.ThrowIfCancellationRequested();
                Job.State = JobState.Running; Job.Message = "Starting"; Store.Save(Job); Changed?.Invoke(Job);
                var LastUpdate = DateTime.MinValue;
                await Engine.ExecuteAsync(Job, (Progress, Message) =>
                {
                    if ((DateTime.UtcNow - LastUpdate).TotalMilliseconds < 150 && Progress < 100) return;
                    LastUpdate = DateTime.UtcNow; Job.Progress = Progress; Job.Message = Message; Changed?.Invoke(Job);
                }, Token.Token);
                Job.State = JobState.Completed;
            }
            catch (OperationCanceledException) { Job.State = JobState.Cancelled; Job.Message = "Cancelled · source preserved"; }
            catch (Exception Error) { Job.State = JobState.Failed; Job.Message = Error.Message; }
            finally
            {
                try { Store.Save(Job); }
                catch (Exception Error) { Job.Message += " · History could not be saved: " + Error.Message; }
                lock (Gate) { Tokens.Remove(Job.Id); Token.Dispose(); }
                Changed?.Invoke(Job);
            }
        }
    }
}
