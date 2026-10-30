using System.Diagnostics;
using System.Text;

namespace Forma.Core;

public sealed class ProcessRunner
{
    public async Task<string> RunAsync(string Executable, IEnumerable<string> Arguments, Action<string>? OnLine, CancellationToken Cancellation, TimeSpan? Timeout = null, bool Capture = true)
    {
        using var Limit = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        if (Timeout is not null) Limit.CancelAfter(Timeout.Value);
        var Start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var Argument in Arguments) Start.ArgumentList.Add(Argument);
        Start.Environment["PYTHONUTF8"] = "1";
        using var Process = new Process { StartInfo = Start };
        try { Process.Start(); }
        catch (System.ComponentModel.Win32Exception Error) { throw new IOException($"Cannot start {Path.GetFileName(Executable)}. Check Settings → Dependencies. {Error.Message}", Error); }
        using var Registration = Limit.Token.Register(() => { try { if (!Process.HasExited) Process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        var Output = new StringBuilder();
        var Errors = new Queue<string>();
        async Task DrainAsync(StreamReader Reader, bool IsError)
        {
            while (await Reader.ReadLineAsync() is { } Line)
            {
                if (IsError) { Errors.Enqueue(Line); while (Errors.Count > 30) Errors.Dequeue(); }
                else if (Capture)
                {
                    if (Output.Length + Line.Length > 8 * 1024 * 1024) { Limit.Cancel(); throw new IOException("Tool returned too much metadata."); }
                    Output.AppendLine(Line);
                }
                try { OnLine?.Invoke(Line); } catch { Limit.Cancel(); throw; }
            }
        }
        try
        {
            await Task.WhenAll(DrainAsync(Process.StandardOutput, false), DrainAsync(Process.StandardError, true), Process.WaitForExitAsync());
            Limit.Token.ThrowIfCancellationRequested();
            if (Process.ExitCode != 0) throw new IOException(string.Join(Environment.NewLine, Errors).Trim() is { Length: > 0 } Detail ? Detail : $"{Path.GetFileName(Executable)} exited with code {Process.ExitCode}.");
            return Output.ToString();
        }
        finally { try { if (!Process.HasExited) { Process.Kill(true); await Process.WaitForExitAsync(); } } catch (InvalidOperationException) { } }
    }
}
