using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace Forma.App;

public partial class App : Application
{
    private Window? Window;
    private readonly Mutex Instance = new(false, "Local\\Forma.Desktop");
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, Args) => Log(Args.Exception);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs Args)
    {
        bool Acquired;
        try { Acquired = Instance.WaitOne(0); } catch (AbandonedMutexException) { Acquired = true; }
        if (!Acquired)
        {
            var Existing = NativeMethods.FindWindow(null, "Forma");
            if (Existing != 0) { NativeMethods.ShowWindow(Existing, 9); NativeMethods.SetForegroundWindow(Existing); }
            Exit(); return;
        }
        try { Window = new MainWindow(); Window.Activate(); }
        catch (Exception Error)
        {
            Log(Error);
            Window = new Window { Title = "Forma", Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Forma could not start.\n\n" + (string.IsNullOrWhiteSpace(Error.Message) ? Error.GetType().Name + $" (0x{Error.HResult:X8})" : Error.Message) + "\n\nDetails: " + Path.Combine(Core.AppSettings.DataFolder, "startup.log"), TextWrapping = TextWrapping.Wrap, Margin = new(32) } };
            Window.Closed += (_, _) => Exit();
            Window.AppWindow.Resize(new(700, 400)); Window.Activate();
        }
    }
    private static void Log(Exception Error)
    {
        try { Directory.CreateDirectory(Core.AppSettings.DataFolder); File.WriteAllText(Path.Combine(Core.AppSettings.DataFolder, "startup.log"), $"{DateTimeOffset.Now:O}\n{Error}"); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindow(string? ClassName, string Title);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint Window, int Command);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint Window);
}
