using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Forma.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.System;

namespace Forma.App;

public sealed class MainWindow : Window
{
    private readonly AppSettings Settings = AppSettings.Load();
    private readonly NavigationView Navigation = new() { PaneDisplayMode = NavigationViewPaneDisplayMode.Left, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed, IsPaneToggleButtonVisible = false, IsSettingsVisible = false, OpenPaneLength = 180, CompactPaneLength = 48, IsPaneOpen = true, AlwaysShowHeader = false };
    private readonly Grid Root = new();
    private readonly Grid Workspace = new() { Margin = new(32, 24, 32, 20) };
    private readonly TextBlock Heading = new() { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock Subtitle = new() { TextWrapping = TextWrapping.Wrap, Opacity = .65 };
    private readonly ContentControl Body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly InfoBar Notice = new() { IsOpen = false, IsClosable = true, Visibility = Visibility.Collapsed, Margin = new(0, 0, 0, 12) };
    private readonly Expander Activity = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed, Margin = new(0, 12, 0, 0) };
    private readonly ProgressBar ActivityProgress = new() { Minimum = 0, Maximum = 100, Height = 3 };
    private readonly TextBlock ActivityText = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ObservableCollection<MediaJob> UiJobs = [];
    private readonly Dictionary<string, FrameworkElement> Pages = [];
    private readonly JobQueue Queue;
    private readonly MediaEngine Engine;
    private readonly Dependencies Dependencies;
    private readonly MediaPlayerElement Player = new() { AreTransportControlsEnabled = true, Height = 84, Visibility = Visibility.Collapsed, Margin = new(0, 12, 0, 0) };
    private readonly HashSet<string> Announced = [];
    private readonly Windows.UI.ViewManagement.AccessibilitySettings Accessibility = new();
    private string CurrentPage = "Download";
    private CancellationTokenSource? Inspection;
    private CancellationTokenSource? DependencyUpdate;

    public MainWindow()
    {
        Title = "Forma";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Forma.ico"));
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new(900, 650));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter Presenter) Presenter.PreferredMinimumWidth = 700;
        Root.RequestedTheme = Enum.TryParse<ElementTheme>(Settings.Theme, out var Theme) ? Theme : ElementTheme.Default;
        Navigation.FontFamily = new("Segoe UI Variable");
        ApplyTheme();
        Root.ActualThemeChanged += (_, _) => ApplyTheme();
        Activated += (_, _) => ApplyTheme();
        Body.ContentTransitions = [new ContentThemeTransition()];
        Player.TransportControls.IsZoomButtonVisible = false;
        Content = Root;
        Dependencies = new(Settings);
        Engine = new(Dependencies, Path.Combine(AppContext.BaseDirectory, "worker", "dreamy_worker.py"));
        Queue = new(new(AppSettings.DataFolder), Engine);
        foreach (var Job in Queue.Jobs) UiJobs.Add(Job);
        Queue.Changed += Job => DispatcherQueue.TryEnqueue(() => OnJobChanged(Job));
        Workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Workspace.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var Header = Stack(6); Header.Margin = new(0, 0, 0, 24); Header.Children.Add(Heading); Header.Children.Add(Subtitle);
        Notice.Closed += (_, _) => Notice.Visibility = Visibility.Collapsed;
        Workspace.Children.Add(Header);
        AddAt(Workspace, Notice, 1); AddAt(Workspace, Body, 2); AddAt(Workspace, Player, 3); AddAt(Workspace, Activity, 4);
        Navigation.Content = Workspace;
        Navigation.PaneHeader = new TextBlock { Text = "Forma", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new(16, 20, 0, 20) };
        foreach (var (Name, Glyph, Tag) in new[] { ("Download", "\uE896", "Download"), ("Compress", "\uE740", "Compress"), ("Convert", "\uE8AB", "Convert"), ("Audio effects", "\uE8D6", "AudioEffects"), ("History", "\uE81C", "History") })
            Navigation.MenuItems.Add(new NavigationViewItem { Content = Name, Icon = new FontIcon { Glyph = Glyph }, Tag = Tag });
        Navigation.FooterMenuItems.Add(new NavigationViewItem { Content = "Settings", Icon = new SymbolIcon(Symbol.Setting), Tag = "Settings" });
        Navigation.SelectionChanged += (_, Args) => { if (Args.SelectedItem is NavigationViewItem Item) ShowPage((string)Item.Tag); };
        Root.Children.Add(Navigation);
        Application.Current.Resources["ActiveVisibility"] = new ActiveVisibilityConverter();
        Application.Current.Resources["FileName"] = new FileNameConverter();
        Activity.Header = ActivityText;
        var ActivityBody = Stack(12);
        ActivityBody.Children.Add(ActivityProgress);
        var ActivityActions = Row();
        ActivityActions.Children.Add(Button("View jobs", () => SelectPage("History")));
        ActivityActions.Children.Add(Button("Cancel all", () => Queue.CancelAll()));
        ActivityBody.Children.Add(ActivityActions); Activity.Content = ActivityBody;
        var Accelerator = new KeyboardAccelerator { Key = VirtualKey.O, Modifiers = VirtualKeyModifiers.Control };
        Accelerator.Invoked += (_, Args) => { if (Pages.TryGetValue(CurrentPage, out var Page) && Page.Tag is Action Pick) Pick(); Args.Handled = true; };
        Root.KeyboardAccelerators.Add(Accelerator);
        Closed += (_, _) => { Inspection?.Cancel(); DependencyUpdate?.Cancel(); Queue.CancelAll(); Player.MediaPlayer?.Dispose(); Settings.Save(); };
        Root.Loaded += (_, _) => { if (Navigation.SelectedItem is null) SelectPage(Enum.TryParse<ToolKind>(Settings.LastTool, out _) ? Settings.LastTool : "Download"); };
    }

    private void ApplyTheme()
    {
        var Dark = Root.ActualTheme == ElementTheme.Dark;
        var HighContrast = Accessibility.HighContrast;
        var Contrast = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        Root.Background = new SolidColorBrush(HighContrast ? Contrast : Dark ? Windows.UI.Color.FromArgb(255, 32, 33, 41) : Windows.UI.Color.FromArgb(255, 250, 250, 252));
        Navigation.Background = new SolidColorBrush(HighContrast ? Contrast : Dark ? Windows.UI.Color.FromArgb(255, 38, 39, 47) : Windows.UI.Color.FromArgb(255, 240, 240, 243));
        var Enabled = Dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 20, ref Enabled, sizeof(int));
    }
    private static StackPanel Stack(double Spacing = 16) => new() { Spacing = Spacing, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private static void AddAt(Grid Grid, FrameworkElement Child, int Row) { Microsoft.UI.Xaml.Controls.Grid.SetRow(Child, Row); Grid.Children.Add(Child); }
    private static TextBlock Text(string Value, double Opacity = 1) => new() { Text = Value, Opacity = Opacity, TextWrapping = TextWrapping.Wrap };
    private static Button Button(string Label, Action Click, bool Accent = false)
    {
        var Control = new Button { Content = Label, MinHeight = 32 };
        AutomationProperties.SetName(Control, Label);
        if (Accent) Control.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        Control.Click += (_, _) => Click(); return Control;
    }
    private static ComboBox Choice(string Label, IEnumerable<string> Values, int Index = 0)
    {
        var Control = new ComboBox { Header = Label, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Values.ToArray(), SelectedIndex = Index, MinWidth = 140 };
        AutomationProperties.SetName(Control, Label); return Control;
    }
    private static NumberBox Number(string Label, double Value, double Minimum, double Maximum, double Step = 1)
    {
        var Control = new NumberBox { Header = Label, Value = Value, Minimum = Minimum, Maximum = Maximum, SmallChange = Step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(Control, Label); return Control;
    }
    private static ScrollViewer Scroll(UIElement Content) => new() { Content = Content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 8, 8) };
    private void SelectPage(string Tag)
    {
        Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>().First(Item => (string)Item.Tag == Tag);
    }
    private void ShowPage(string Tag)
    {
        CurrentPage = Tag;
        if (Tag != "AudioEffects") { Player.MediaPlayer?.Pause(); Player.Visibility = Visibility.Collapsed; }
        if (Enum.TryParse<ToolKind>(Tag, out _)) { Settings.LastTool = Tag; Settings.Save(); }
        Notice.IsOpen = false; Notice.Visibility = Visibility.Collapsed;
        Heading.Text = Tag == "AudioEffects" ? "Audio effects" : Tag;
        Subtitle.Text = Tag switch { "Download" => "Save media from a link.", "Compress" => "Less space. The details you want to keep.", "Convert" => "The right format for what’s next.", "AudioEffects" => "Give your audio a little more space.", "History" => "Your recent files and activity.", _ => "Make Forma feel at home." };
        if (!Pages.TryGetValue(Tag, out var Page))
        {
            Page = Tag switch { "Download" => DownloadPage(), "History" => HistoryPage(), "Settings" => SettingsPage(), _ => ProcessingPage(Enum.Parse<ToolKind>(Tag)) };
            Pages[Tag] = Page;
        }
        Body.Content = Page;
    }
    private void Error(Exception Error) => Notify(Error.Message, InfoBarSeverity.Error);
    private void Notify(string Message, InfoBarSeverity Severity = InfoBarSeverity.Informational)
    {
        Notice.Title = ""; Notice.Message = Message.Length > 450 ? Message[..450] + "… See History for details." : Message; Notice.Severity = Severity; Notice.Visibility = Visibility.Visible; Notice.IsOpen = true;
    }
    private async Task GuardAsync(Func<Task> Work) { try { await Work(); } catch (Exception Exception) { Error(Exception); } }
    private void Guard(Action Work) { try { Work(); } catch (Exception Exception) { Error(Exception); } }
    private TextBox Destination(StackPanel Parent)
    {
        var Control = new TextBox { Header = "Save to", Text = Settings.OutputFolder, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(Control, "Output folder");
        var Grid = new Grid { ColumnSpacing = 8 }; Grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.Children.Add(Control);
        var Browse = Button("Browse…", () => _ = GuardAsync(async () => { var Result = await new FolderPicker(AppWindow.Id).PickSingleFolderAsync(); if (Result is not null) Control.Text = Result.Path; }));
        Browse.VerticalAlignment = VerticalAlignment.Bottom; Microsoft.UI.Xaml.Controls.Grid.SetColumn(Browse, 1); Grid.Children.Add(Browse); Parent.Children.Add(Grid); return Control;
    }

    private FrameworkElement DownloadPage()
    {
        var Panel = Stack(16);
        var Urls = new TextBox { Header = "Links", PlaceholderText = "Paste one or more media URLs", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 88, MaxHeight = 150 };
        AutomationProperties.SetName(Urls, "Media URLs"); Panel.Children.Add(Urls);
        var Formats = new ComboBox { Header = "Quality", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { new DownloadChoice("bv*+ba/b", "Best available video"), new DownloadChoice("bv*[height<=1080]+ba/b[height<=1080]", "Video · up to 1080p"), new DownloadChoice("bv*[height<=720]+ba/b[height<=720]", "Video · up to 720p"), new DownloadChoice("audio", "Audio only · MP3") }, SelectedIndex = 0 };
        AutomationProperties.SetName(Formats, "Download quality");
        var Actions = Row();
        var StopInspection = Button("Cancel", () => Inspection?.Cancel()); StopInspection.Visibility = Visibility.Collapsed;
        var Inspect = Button("Check formats", () => _ = GuardAsync(async () =>
        {
            var Url = SplitUrls(Urls.Text).FirstOrDefault() ?? throw new ArgumentException("Paste a media URL first.");
            Inspection?.Cancel(); Inspection = new(); StopInspection.Visibility = Visibility.Visible; Notify("Checking available formats…");
            try { Formats.ItemsSource = await Engine.InspectAsync(Url, Inspection.Token); Formats.SelectedIndex = 0; Notify("Formats for the first link are ready. Use Best available for a mixed batch."); }
            finally { StopInspection.Visibility = Visibility.Collapsed; }
        }));
        Actions.Children.Add(Inspect);
        Actions.Children.Add(StopInspection);
        Panel.Children.Add(Formats); Panel.Children.Add(Actions);
        var Output = Destination(Panel);
        var Start = Button("Download", () => Guard(() =>
        {
            var Inputs = SplitUrls(Urls.Text);
            if (Inputs.Length == 0) throw new ArgumentException("Paste at least one URL.");
            var Requests = Inputs.Select(Input => new JobRequest(ToolKind.Download, Input, Output.Text.Trim(), DownloadFormat: ((DownloadChoice)Formats.SelectedItem).Id)).ToArray();
            foreach (var Request in Requests) MediaEngine.Validate(Request);
            foreach (var Request in Requests) Queue.Enqueue(Request);
            Notify($"{Requests.Length} download{(Requests.Length == 1 ? "" : "s")} queued.");
        }), true);
        Panel.Children.Add(Start); Panel.Children.Add(Text("Public and legitimately accessible media. Some sites require authentication; Forma does not bypass access controls.", .55));
        return Scroll(Panel);
    }
    private static string[] SplitUrls(string Value) => Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();

    private FrameworkElement ProcessingPage(ToolKind Tool)
    {
        var Panel = Stack(16);
        var Files = new ObservableCollection<string>();
        var FileList = new ListView { ItemsSource = Files, MaxHeight = 128, SelectionMode = Tool == ToolKind.AudioEffects ? ListViewSelectionMode.Single : ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = Tool != ToolKind.AudioEffects };
        FileList.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><TextBlock Text=\"{Binding Converter={StaticResource FileName}}\" ToolTipService.ToolTip=\"{Binding}\" TextTrimming=\"CharacterEllipsis\" /></DataTemplate>");
        AutomationProperties.SetName(FileList, "Input files");
        var Empty = Text("Choose files or drop them here.", .65);
        void AddFiles(IReadOnlyList<string> Paths)
        {
            foreach (var PathName in Paths.Where(File.Exists))
            {
                if (Tool == ToolKind.AudioEffects) Files.Clear();
                if (!Files.Contains(PathName)) Files.Add(PathName);
            }
            Empty.Visibility = Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        async Task PickAsync()
        {
            var Picker = new FileOpenPicker(AppWindow.Id);
            Picker.FileTypeFilter.Add("*");
            if (Tool == ToolKind.AudioEffects) { var Result = await Picker.PickSingleFileAsync(); if (Result is not null) AddFiles([Result.Path]); }
            else AddFiles((await Picker.PickMultipleFilesAsync()).Select(File => File.Path).ToArray());
        }
        var FileArea = Stack(8); FileArea.Children.Add(Empty); FileArea.Children.Add(FileList);
        var FileActions = Row(); FileActions.Children.Add(Button(Tool == ToolKind.AudioEffects ? "Choose audio…" : "Add files…", () => _ = GuardAsync(PickAsync)));
        FileActions.Children.Add(Button("Remove", () => { foreach (var File in FileList.SelectedItems.Cast<string>().ToArray()) Files.Remove(File); Empty.Visibility = Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }));
        FileArea.Children.Add(FileActions);
        var FileBorder = (Border)XamlReader.Load("<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Padding=\"16\" BorderThickness=\"1\" CornerRadius=\"8\" BorderBrush=\"{ThemeResource CardStrokeColorDefaultBrush}\" Background=\"{ThemeResource CardBackgroundFillColorDefaultBrush}\" AllowDrop=\"True\" />");
        FileBorder.Child = FileArea;
        FileBorder.DragOver += (_, Args) => { Args.AcceptedOperation = Args.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None; };
        FileBorder.Drop += async (_, Args) => { if (Args.DataView.Contains(StandardDataFormats.StorageItems)) await GuardAsync(async () => AddFiles((await Args.DataView.GetStorageItemsAsync()).Select(File => File.Path).ToArray())); };
        Panel.Children.Add(FileBorder);
        var Format = Choice("Output format", Tool == ToolKind.AudioEffects ? ["wav", "mp3", "flac"] : Tool == ToolKind.Compress ? ["mp4", "mkv"] : ["mp3", "wav", "flac", "aac", "mp4", "mkv", "webm"]);
        var Preset = Choice("Preset", Tool == ToolKind.AudioEffects ? EffectOptions.Presets.Keys : ["Smaller File", "Balanced", "Higher Quality"], Tool == ToolKind.AudioEffects ? 0 : 1);
        var Crf = Number("Video quality · CRF", Settings.DefaultCrf, 0, 51);
        var Codec = Choice("Video codec", ["H.264", "H.265"]);
        var Resolution = Choice("Maximum resolution", ["Original", "2160p", "1440p", "1080p", "720p", "480p"]);
        var Speed = Choice("Encoding speed", ["fast", "medium", "slow"], 1);
        var Bitrate = Choice("Audio bitrate", ["128 kbps", "192 kbps", "256 kbps", "320 kbps"], Settings.DefaultBitrate switch { 128 => 0, 256 => 2, 320 => 3, _ => 1 });
        var EffectControls = new[] { Number("Speed & pitch", .92, .7, 1.25, .01), Number("Reverb wet", .6, 0, 1, .05), Number("Room size", .85, 0, 1, .05), Number("Damping", .25, 0, 1, .05), Number("Low-pass · Hz", 10000, 500, 20000, 500), Number("Stereo width", 1.4, .5, 2.5, .1), Number("Chorus mix", .15, 0, 1, .05), Number("High-pass · Hz", 40, 20, 800, 10), Number("Echo time · ms", 0, 0, 600, 10), Number("Echo mix", 0, 0, .3, .01), Number("Tape warmth", 0, 0, .6, .05), Number("Bass · dB", 0, -6, 6, .5), Number("Chorus rate · Hz", .5, .1, 2, .05), Number("Reverb tail · seconds", 0, 0, 4, .25) };
        var PresetNote = Text(EffectOptions.Description((string)Preset.SelectedItem), .65);
        if (Tool == ToolKind.Convert) Panel.Children.Add(Format);
        else
        {
            var Options = new Grid { ColumnSpacing = 12 };
            Options.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Options.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            Options.Children.Add(Preset); Microsoft.UI.Xaml.Controls.Grid.SetColumn(Format, 1); Options.Children.Add(Format); Panel.Children.Add(Options);
        }
        if (Tool == ToolKind.AudioEffects) Panel.Children.Add(PresetNote);
        var Advanced = Stack(12);
        if (Tool == ToolKind.AudioEffects) foreach (var Control in EffectControls) Advanced.Children.Add(Control);
        else { Advanced.Children.Add(Codec); Advanced.Children.Add(Resolution); Advanced.Children.Add(Crf); Advanced.Children.Add(Speed); }
        Advanced.Children.Add(Bitrate);
        Panel.Children.Add(new Expander { Header = "Advanced", Content = Advanced, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        void ApplyPreset()
        {
            if (Tool == ToolKind.AudioEffects)
            {
                var Effects = EffectOptions.Presets[(string)Preset.SelectedItem];
                var Values = new[] { Effects.Speed, Effects.Wet, Effects.Room, Effects.Damping, Effects.Lowpass, Effects.Width, Effects.Chorus, Effects.Highpass, Effects.DelayMs, Effects.DelayMix, Effects.Warmth, Effects.BassDb, Effects.ChorusRate, Effects.TailSeconds };
                for (var Index = 0; Index < Values.Length; Index++) EffectControls[Index].Value = Values[Index];
                PresetNote.Text = EffectOptions.Description((string)Preset.SelectedItem);
            }
            else Crf.Value = Preset.SelectedIndex switch { 0 => 30, 2 => 20, _ => 25 };
        }
        Preset.SelectionChanged += (_, _) => ApplyPreset();
        if (Tool == ToolKind.AudioEffects) ApplyPreset();
        var Output = Destination(Panel);
        void Start(bool Preview)
        {
            if (Files.Count == 0) throw new ArgumentException("Choose an input file first.");
            var Values = EffectControls.Select(Control => Control.Value).ToArray();
            var Effects = new EffectOptions(Values[0], Values[1], Values[2], Values[3], Values[4], Values[5], Values[6], Values[7], Values[8], Values[9], Values[10], Values[11], Values[12], Values[13]);
            if (!double.IsFinite(Crf.Value)) throw new ArgumentException("Enter a valid CRF value.");
            var Height = Resolution.SelectedIndex == 0 ? 0 : int.Parse(((string)Resolution.SelectedItem).TrimEnd('p'));
            var Requests = Files.Select(Input => new JobRequest(Tool, Input, Preview ? Path.Combine(AppSettings.DataFolder, "Previews") : Output.Text.Trim(), Preview ? "wav" : (string)Format.SelectedItem, Codec.SelectedIndex == 0 ? "libx264" : "libx265", (int)Crf.Value, Height, (string)Speed.SelectedItem, int.Parse(((string)Bitrate.SelectedItem).Split(' ')[0]), Effects: Tool == ToolKind.AudioEffects ? Effects : null, Preview: Preview, EffectName: Tool == ToolKind.AudioEffects ? (string)Preset.SelectedItem : "")).ToArray();
            foreach (var Request in Requests) MediaEngine.Validate(Request);
            foreach (var Request in Requests) Queue.Enqueue(Request);
            Notify(Preview ? "Creating a 15-second preview…" : $"{Requests.Length} file{(Requests.Length == 1 ? "" : "s")} queued.");
        }
        var Starts = Row();
        Starts.Children.Add(Button(Tool == ToolKind.AudioEffects ? "Export audio" : Tool == ToolKind.Compress ? "Compress" : "Convert", () => Guard(() => Start(false)), true));
        if (Tool == ToolKind.AudioEffects) Starts.Children.Add(Button("Preview · 15 s", () => Guard(() => Start(true))));
        Panel.Children.Add(Starts);
        if (Tool == ToolKind.Convert) Panel.Children.Add(Text("Choose an audio format to extract sound from a video.", .55));
        var Page = Scroll(Panel); Page.Tag = (Action)(() => _ = GuardAsync(PickAsync)); return Page;
    }

    private FrameworkElement HistoryPage()
    {
        var Panel = new Grid { RowSpacing = 12 };
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); Panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var Search = new AutoSuggestBox { PlaceholderText = "Find a file or link", QueryIcon = new SymbolIcon(Symbol.Find) };
        AutomationProperties.SetName(Search, "Search history");
        var List = new ListView { ItemsSource = UiJobs, SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(List, "Job history");
        List.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Spacing="5" Padding="0,8" HorizontalAlignment="Stretch">
                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                <TextBlock Text="{Binding Summary}" Opacity="0.55" FontSize="12" />
                <TextBlock Text="{Binding Status}" Opacity="0.8" TextTrimming="CharacterEllipsis" MaxLines="2" TextWrapping="Wrap" />
                <ProgressBar Value="{Binding Progress}" IsIndeterminate="{Binding IsIndeterminate}" Visibility="{Binding IsActive, Converter={StaticResource ActiveVisibility}}" Height="2" />
              </StackPanel>
            </DataTemplate>
            """);
        var Menu = new MenuFlyout();
        void AddMenu(string Label, Action<MediaJob> Action)
        {
            var Item = new MenuFlyoutItem { Text = Label };
            Item.Click += (_, _) => { if (List.SelectedItem is MediaJob Job) Guard(() => Action(Job)); };
            Menu.Items.Add(Item);
        }
        AddMenu("Open output", Job => OpenOutput(Job)); AddMenu("Show in Explorer", Job => Reveal(Job));
        AddMenu("Retry", Job => { if (Job.CanRetry) Queue.Enqueue(Job.Request); }); AddMenu("Cancel", Job => Queue.Cancel(Job.Id));
        List.ContextFlyout = Menu;
        List.RightTapped += (_, Args) => { if (Args.OriginalSource is FrameworkElement { DataContext: MediaJob Job }) List.SelectedItem = Job; };
        var Actions = Row();
        Actions.Children.Add(Button("Details", () => _ = GuardAsync(async () =>
        {
            if (List.SelectedItem is not MediaJob Job) return;
            var Detail = Text($"{Job.Name}\n\n{Job.Summary}\n\n{Job.Status}\n\n{Job.Output}"); Detail.IsTextSelectionEnabled = true;
            await new ContentDialog { Title = "Job details", Content = Scroll(Detail), CloseButtonText = "Close", XamlRoot = Root.XamlRoot }.ShowAsync();
        })));
        var Open = Button("Open", () => { if (List.SelectedItem is MediaJob Job) Guard(() => OpenOutput(Job)); });
        var RevealButton = Button("Show in folder", () => { if (List.SelectedItem is MediaJob Job) Guard(() => Reveal(Job)); });
        var Retry = Button("Retry", () => { if (List.SelectedItem is MediaJob Job && Job.CanRetry) Guard(() => Queue.Enqueue(Job.Request)); });
        var Cancel = Button("Cancel", () => { if (List.SelectedItem is MediaJob Job) Queue.Cancel(Job.Id); });
        Actions.Children.Add(Open); Actions.Children.Add(RevealButton); Actions.Children.Add(Retry); Actions.Children.Add(Cancel);
        void UpdateActions()
        {
            var Job = List.SelectedItem as MediaJob;
            Open.IsEnabled = RevealButton.IsEnabled = Job is { State: JobState.Completed } && File.Exists(Job.Output);
            Retry.IsEnabled = Job?.CanRetry == true; Cancel.IsEnabled = Job?.State is JobState.Running or JobState.Queued;
        }
        List.SelectionChanged += (_, _) => UpdateActions();
        Queue.Changed += _ => DispatcherQueue.TryEnqueue(UpdateActions); UpdateActions();
        var Header = Stack(12); Header.Children.Add(Search); Header.Children.Add(Actions);
        Panel.Children.Add(Header); AddAt(Panel, List, 1);
        Search.TextChanged += (_, _) => List.ItemsSource = string.IsNullOrWhiteSpace(Search.Text) ? UiJobs : UiJobs.Where(Job => Job.Name.Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        return Panel;
    }
    private void OpenOutput(MediaJob Job)
    {
        if (!File.Exists(Job.Output)) throw new FileNotFoundException("The completed file has been moved or deleted.");
        if (Job.Request.Tool == ToolKind.AudioEffects) { Player.Source = MediaSource.CreateFromUri(new Uri(Job.Output)); Player.Visibility = Visibility.Visible; Player.MediaPlayer.Play(); }
        else Process.Start(new ProcessStartInfo(Job.Output) { UseShellExecute = true });
    }
    private static void Reveal(MediaJob Job)
    {
        if (!File.Exists(Job.Output)) throw new FileNotFoundException("The completed file has been moved or deleted.");
        var Start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false }; Start.ArgumentList.Add("/select,"); Start.ArgumentList.Add(Job.Output); Process.Start(Start);
    }
    private FrameworkElement SettingsPage()
    {
        var Panel = Stack(20);
        var Theme = Choice("Appearance", ["System", "Light", "Dark"], Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 });
        Theme.SelectionChanged += (_, _) => Guard(() => { Settings.Theme = Theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "Default" }; Root.RequestedTheme = Enum.Parse<ElementTheme>(Settings.Theme); ApplyTheme(); Settings.Save(); });
        Panel.Children.Add(Theme);
        var Folder = Destination(Panel);
        var ToolFolder = new TextBox { Header = "Media tools folder · optional", Text = Settings.ToolFolder, PlaceholderText = "Folder containing ffmpeg.exe, ffprobe.exe, yt-dlp.exe, deno.exe" };
        var Python = new TextBox { Header = "Dreamy Python executable", Text = Settings.PythonPath, PlaceholderText = "Path to the Python environment with Pedalboard" };
        Panel.Children.Add(ToolFolder); Panel.Children.Add(Python);
        var Crf = Number("Default video CRF", Settings.DefaultCrf, 0, 51);
        var Bitrate = Choice("Default audio bitrate", ["128", "192", "256", "320"], Settings.DefaultBitrate switch { 128 => 0, 256 => 2, 320 => 3, _ => 1 });
        Panel.Children.Add(Crf); Panel.Children.Add(Bitrate);
        var Sound = new ToggleSwitch { Header = "Completion sound", IsOn = Settings.CompletionSound }; Panel.Children.Add(Sound);
        Panel.Children.Add(Button("Save preferences", () => Guard(() =>
        {
            if (!Path.IsPathFullyQualified(Folder.Text.Trim())) throw new ArgumentException("Choose an absolute output folder.");
            if (!double.IsFinite(Crf.Value)) throw new ArgumentException("Enter a valid default CRF.");
            Settings.OutputFolder = Folder.Text.Trim(); Settings.ToolFolder = ToolFolder.Text.Trim(); Settings.PythonPath = Python.Text.Trim(); Settings.DefaultCrf = (int)Crf.Value; Settings.DefaultBitrate = int.Parse((string)Bitrate.SelectedItem); Settings.CompletionSound = Sound.IsOn; Settings.Save();
            foreach (var Key in new[] { "Download", "Compress", "Convert", "AudioEffects" }) Pages.Remove(Key);
            Notify("Preferences saved. New workspaces use these defaults.", InfoBarSeverity.Success);
        }), true));
        var Versions = new TextBlock { Text = "Check installed tools and versions.", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = .7 };
        var DependencyPanel = Stack(12);
        DependencyPanel.Children.Add(Button("Check dependencies", () => _ = GuardAsync(async () => { Versions.Text = "Checking…"; Versions.Text = await Dependencies.DescribeAsync(); })));
        Button? Update = null;
        var StopUpdate = Button("Cancel update", () => DependencyUpdate?.Cancel()); StopUpdate.Visibility = Visibility.Collapsed;
        Update = Button("Update downloader", () => _ = GuardAsync(async () =>
        {
            if (Queue.Jobs.Any(Job => Job.Request.Tool == ToolKind.Download && Job.IsActive)) throw new IOException("Finish or cancel active downloads before updating yt-dlp.");
            DependencyUpdate?.Dispose(); DependencyUpdate = new(); Update!.IsEnabled = false; StopUpdate.Visibility = Visibility.Visible;
            try
            {
                var Version = await DownloaderUpdate.InstallAsync(Dependencies.ManagedToolsFolder, Message => DispatcherQueue.TryEnqueue(() => Versions.Text = Message), DependencyUpdate.Token);
                Versions.Text = await Dependencies.DescribeAsync();
                Notify($"yt-dlp {Version} installed. Retry the download in History." + (File.Exists(Path.Combine(Settings.ToolFolder, "yt-dlp.exe")) ? " Your custom media tools folder takes priority; clear it to use the managed downloader." : ""), InfoBarSeverity.Success);
            }
            finally { Update.IsEnabled = true; StopUpdate.Visibility = Visibility.Collapsed; }
        }));
        DependencyPanel.Children.Add(Update); DependencyPanel.Children.Add(StopUpdate);
        DependencyPanel.Children.Add(Versions);
        DependencyPanel.Children.Add(Text("Update downloader installs the official stable yt-dlp build after checking its SHA-256. Updates run only when requested. FFmpeg and Deno use your installed copies.", .65));
        Panel.Children.Add(new Expander { Header = "Dependencies", Content = DependencyPanel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        Panel.Children.Add(Text("Forma 1.1 · Local processing · No accounts or telemetry\nOriginal code MIT. FFmpeg, yt-dlp, Deno and Pedalboard retain their own licenses.", .55));
        return Scroll(Panel);
    }
    private void OnJobChanged(MediaJob Job)
    {
        if (!UiJobs.Contains(Job)) UiJobs.Insert(0, Job);
        Job.Refresh();
        var Active = Queue.Jobs.Where(Item => Item.State is JobState.Queued or JobState.Running).ToArray();
        Activity.Visibility = Active.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var Running = Active.FirstOrDefault(Item => Item.State == JobState.Running);
        ActivityText.Text = $"{Active.Length} active · {Running?.Message ?? "Waiting"}";
        ActivityProgress.Value = Running?.Progress ?? 0;
        ActivityProgress.IsIndeterminate = Running?.IsIndeterminate ?? true;
        if (Job.State is JobState.Completed or JobState.Failed or JobState.Cancelled && Announced.Add(Job.Id))
        {
            Notify(Job.State == JobState.Completed ? $"Ready · {Path.GetFileName(Job.Output)}" : Job.State == JobState.Failed ? Job.Message.Split('\n')[0] : $"{Job.State} · {Job.Name}", Job.State == JobState.Failed ? InfoBarSeverity.Error : Job.State == JobState.Completed ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
            if (Job.State == JobState.Completed)
            {
                if (Settings.CompletionSound) NativeMethods.MessageBeep(0x40);
                if (Job.Request.Preview) Guard(() => OpenOutput(Job));
            }
        }
    }
}

internal static partial class NativeMethods
{
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(nint Window, int Attribute, ref int Value, int Size);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MessageBeep(uint Type);
}

public sealed class ActiveVisibilityConverter : IValueConverter
{
    public object Convert(object Value, Type TargetType, object Parameter, string Language) => Value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object Value, Type TargetType, object Parameter, string Language) => throw new NotSupportedException();
}

public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object Value, Type TargetType, object Parameter, string Language) => Path.GetFileName(Value as string ?? "");
    public object ConvertBack(object Value, Type TargetType, object Parameter, string Language) => throw new NotSupportedException();
}
