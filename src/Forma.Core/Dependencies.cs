namespace Forma.Core;

public sealed class Dependencies(AppSettings Settings)
{
    public static string ManagedToolsFolder => Path.Combine(AppSettings.DataFolder, "tools");
    public string Resolve(string Name)
    {
        if (Name == "python") return Settings.PythonPath;
        var Executable = Name + ".exe";
        var SearchPath = string.Join(Path.PathSeparator, Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        foreach (var Folder in new[] { Settings.ToolFolder, ManagedToolsFolder, Path.Combine(AppContext.BaseDirectory, "tools") }.Concat(SearchPath.Split(Path.PathSeparator)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!string.IsNullOrWhiteSpace(Folder) && File.Exists(Path.Combine(Folder.Trim('"'), Executable))) return Path.GetFullPath(Path.Combine(Folder.Trim('"'), Executable));
        throw new FileNotFoundException($"{Executable} is missing. Choose its folder in Settings or add it to PATH, then restart Forma.");
    }
    public async Task<string> DescribeAsync(CancellationToken Cancellation = default)
    {
        var Lines = new List<string>();
        foreach (var Name in new[] { "ffmpeg", "ffprobe", "yt-dlp", "deno", "python" })
        {
            try
            {
                var Executable = Resolve(Name);
                var Result = await new ProcessRunner().RunAsync(Executable, [Name is "ffmpeg" or "ffprobe" ? "-version" : "--version"], null, Cancellation, TimeSpan.FromSeconds(10));
                Lines.Add($"{Result.Split('\n')[0].Trim()}\n{Executable}");
            }
            catch (Exception Error) when (Error is IOException or OperationCanceledException) { Lines.Add($"{Name}: {Error.Message}"); }
        }
        try { Lines.Add((await new ProcessRunner().RunAsync(Resolve("python"), ["-c", "import pedalboard,numpy; print('Pedalboard ' + pedalboard.__version__ + ' / NumPy ' + numpy.__version__)"], null, Cancellation, TimeSpan.FromSeconds(15))).Trim()); }
        catch (Exception Error) when (Error is IOException or OperationCanceledException) { Lines.Add("Dreamy worker: " + Error.Message); }
        return string.Join("\n\n", Lines);
    }
}
