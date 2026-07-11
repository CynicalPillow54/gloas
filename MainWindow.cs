using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace GameDisplaySwitcher;

public sealed class MainWindow : Window
{
    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly MonitorService _monitors = new();
    private readonly GamepadService _gamepads = new();
    private readonly SequenceMatcher _gamingMatcher;
    private readonly SequenceMatcher _desktopMatcher;
    private readonly WinForms.NotifyIcon _tray;
    private readonly ComboBox _monitorPicker = new() { MinWidth = 420, Margin = new Thickness(0, 5, 0, 12) };
    private readonly TextBlock _status = new() { Foreground = Brushes.SlateGray, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _startup = new() { Content = "Start silently when I sign in to Windows", Margin = new Thickness(0, 8, 0, 8) };
    private readonly SequenceEditor _gamingEditor;
    private readonly SequenceEditor _desktopEditor;
    private readonly SemaphoreSlim _modeLock = new(1, 1);
    private bool _exitRequested;

    public MainWindow()
    {
        Title = "Game Display Switcher";
        Width = 820; Height = 720; MinWidth = 720; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));
        _gamingMatcher = new(() => _settings.GamingSequence);
        _desktopMatcher = new(() => _settings.DesktopSequence);
        _gamingEditor = new("Gaming sequence", _settings.GamingSequence, SaveSettings);
        _desktopEditor = new("Desktop sequence", _settings.DesktopSequence, SaveSettings);
        Content = BuildUi();

        _tray = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Game Display Switcher",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Gaming mode", null, (_, _) => Dispatcher.Invoke(() => _ = SetGamingModeAsync(true)));
        _tray.ContextMenuStrip.Items.Add("Desktop mode", null, (_, _) => Dispatcher.Invoke(() => _ = SetDesktopModeAsync()));
        _tray.ContextMenuStrip.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        _tray.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);

        _startup.IsChecked = _settings.StartWithWindows;
        _startup.Checked += (_, _) => { _settings.StartWithWindows = true; SaveSettings(); StartupService.Set(true); };
        _startup.Unchecked += (_, _) => { _settings.StartWithWindows = false; SaveSettings(); StartupService.Set(false); };
        Closing += (_, e) => { if (!_exitRequested) { e.Cancel = true; Hide(); } };

        _gamepads.StateChanged += OnGamepadState;
        try { StartupService.Set(_settings.StartWithWindows); } catch (Exception ex) { SettingsStore.Log(ex.ToString()); }
        Loaded += async (_, _) => await RefreshMonitorsAsync();
    }

    private UIElement BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = "Game Display Switcher", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brushes.MidnightBlue });
        header.Children.Add(new TextBlock { Text = "Switch your monitor layout and Steam using shortcuts or an Xbox controller.", FontSize = 14, Foreground = Brushes.DimGray });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Displays & Steam", Content = BuildDisplayTab() });
        tabs.Items.Add(new TabItem { Header = "Controller sequences", Content = BuildControllerTab() });
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildDisplayTab()
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(Heading("Gaming display"));
        panel.Children.Add(new TextBlock { Text = "Choose the display that should remain active in landscape mode.", Foreground = Brushes.DimGray });
        panel.Children.Add(_monitorPicker);
        _monitorPicker.SelectionChanged += (_, _) =>
        {
            if (_monitorPicker.SelectedItem is MonitorInfo m)
            { _settings.GamingMonitorId = m.StableId; _settings.GamingMonitorLabel = m.Label; SaveSettings(); }
        };
        var row = new WrapPanel();
        row.Children.Add(Button("Refresh displays", async (_, _) => await RefreshMonitorsAsync()));
        row.Children.Add(Button("Identify displays", (_, _) => _monitors.ShowIdentifiers()));
        panel.Children.Add(row);
        panel.Children.Add(Heading("Desktop profile", 22));
        panel.Children.Add(new TextBlock { Text = "Arrange all displays normally, then capture that layout for reliable restoration.", Foreground = Brushes.DimGray });
        var profileRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        profileRow.Children.Add(Button("Capture current desktop", async (_, _) => await CaptureDesktopAsync()));
        profileRow.Children.Add(Button("Test Gaming mode (15s rollback)", async (_, _) => await TestGamingAsync()));
        panel.Children.Add(profileRow);
        panel.Children.Add(Heading("Actions", 22));
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(PrimaryButton("Gaming mode", async (_, _) => await SetGamingModeAsync(true)));
        actions.Children.Add(Button("Desktop mode", async (_, _) => await SetDesktopModeAsync()));
        panel.Children.Add(actions);
        panel.Children.Add(_startup);
        panel.Children.Add(new TextBlock { Text = "Desktop mode restores the screens only; exit Big Picture from Steam's Power menu.", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 6, 0, 0) });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement BuildControllerTab()
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Press Record, then enter each step on the controller. Buttons pressed together appear as one chord. Inputs are observed, not blocked from games.",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(_gamingEditor);
        panel.Children.Add(_desktopEditor);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task RefreshMonitorsAsync()
    {
        try
        {
            SetStatus("Reading displays…");
            var list = await _monitors.EnumerateAsync();
            _monitorPicker.ItemsSource = list;
            _monitorPicker.SelectedItem = list.FirstOrDefault(m => m.StableId.Equals(_settings.GamingMonitorId, StringComparison.OrdinalIgnoreCase));
            SetStatus(list.Count == 0 ? "No displays were returned by MultiMonitorTool." : $"Found {list.Count} display(s). Settings are saved automatically.");
        }
        catch (Exception ex) { ReportError("Could not read displays", ex); }
    }

    private async Task CaptureDesktopAsync()
    {
        try
        {
            await _monitors.CaptureDesktopAsync();
            SetStatus($"Desktop profile captured at {DateTime.Now:t}.");
            Notify("Desktop profile captured", "Your current monitor layout can now be restored.");
        }
        catch (Exception ex) { ReportError("Could not capture the desktop profile", ex); }
    }

    private async Task TestGamingAsync()
    {
        if (!ValidateSetup()) return;
        try
        {
            await _monitors.ApplyGamingAsync(_settings.GamingMonitorId);
            var dialog = new RollbackWindow(15) { Owner = this };
            var keep = dialog.ShowDialog() == true;
            if (!keep) { await _monitors.RestoreDesktopAsync(); SetStatus("Test completed; desktop layout restored."); }
            else SetStatus("Gaming layout kept. Use Desktop mode to restore all displays.");
        }
        catch (Exception ex)
        {
            try { await _monitors.RestoreDesktopAsync(); } catch { }
            ReportError("Gaming mode test failed", ex);
        }
    }

    private async Task SetGamingModeAsync(bool launchSteam)
    {
        if (!ValidateSetup() || !await _modeLock.WaitAsync(0)) return;
        try
        {
            SetStatus("Switching to Gaming mode…");
            await _monitors.ApplyGamingAsync(_settings.GamingMonitorId);
            if (launchSteam) SteamService.OpenBigPicture();
            SetStatus("Gaming mode is active."); Notify("Gaming mode", "Gaming display enabled; Steam Big Picture opened.");
        }
        catch (Exception ex)
        {
            try { await _monitors.RestoreDesktopAsync(); } catch { }
            ReportError("Could not enter Gaming mode", ex);
        }
        finally { _modeLock.Release(); }
    }

    private async Task SetDesktopModeAsync()
    {
        if (!await _modeLock.WaitAsync(0)) return;
        try
        {
            SetStatus("Restoring the desktop layout…");
            await _monitors.RestoreDesktopAsync();
            SetStatus("Desktop mode is active."); Notify("Desktop mode", "Your saved monitor layout was restored.");
        }
        catch (Exception ex) { ReportError("Could not restore Desktop mode", ex); }
        finally { _modeLock.Release(); }
    }

    private bool ValidateSetup()
    {
        if (string.IsNullOrWhiteSpace(_settings.GamingMonitorId)) { SetStatus("Choose a gaming display first."); ShowSettings(); return false; }
        if (!File.Exists(SettingsStore.DesktopProfilePath)) { SetStatus("Capture your normal desktop layout first."); ShowSettings(); return false; }
        return true;
    }

    private void OnGamepadState(int controller, IReadOnlySet<string> pressed, IReadOnlySet<string> newly)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _gamingEditor.UpdateController(controller, pressed, newly);
            _desktopEditor.UpdateController(controller, pressed, newly);
            if (_gamingMatcher.Feed(controller, pressed, newly)) _ = SetGamingModeAsync(true);
            else if (_desktopMatcher.Feed(controller, pressed, newly)) _ = SetDesktopModeAsync();
        });
    }

    public void HandleCommand(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "--gaming": _ = SetGamingModeAsync(true); break;
            case "--desktop": _ = SetDesktopModeAsync(); break;
            case "--background": Hide(); break;
            default: ShowSettings(); break;
        }
    }

    private void ShowSettings() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void ExitApp() { _exitRequested = true; _gamepads.Dispose(); _tray.Visible = false; _tray.Dispose(); Application.Current.Shutdown(); }
    private void SaveSettings() { SettingsStore.Save(_settings); }
    private void SetStatus(string text) => _status.Text = text;
    private void Notify(string title, string body) => _tray.ShowBalloonTip(3500, title, body, WinForms.ToolTipIcon.Info);
    private void ReportError(string title, Exception ex)
    {
        SettingsStore.Log($"{title}: {ex}"); SetStatus($"{title}: {ex.Message}"); Notify(title, ex.Message);
    }

    private static TextBlock Heading(string text, double top = 0) => new()
    { Text = text, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brushes.MidnightBlue, Margin = new Thickness(0, top, 0, 6) };
    private static Button Button(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 8), MinWidth = 120 };
        b.Click += click; return b;
    }
    private static Button PrimaryButton(string text, RoutedEventHandler click)
    {
        var b = Button(text, click); b.Background = Brushes.RoyalBlue; b.Foreground = Brushes.White; b.BorderBrush = Brushes.RoyalBlue; return b;
    }
}

public sealed class SequenceEditor : Border
{
    private static readonly string[] Names = ["DPadUp", "DPadLeft", "DPadRight", "DPadDown", "View", "Menu", "LB", "RB", "LeftStick", "RightStick", "X", "Y", "A", "B"];
    private readonly SequenceSettings _sequence; private readonly Action _save;
    private readonly ListBox _steps = new() { Height = 105, MinWidth = 260 };
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _manualChord = new(StringComparer.OrdinalIgnoreCase);
    private readonly CheckBox _record = new() { Content = "Record from controller", Margin = new Thickness(0, 6, 12, 6) };
    private readonly TextBlock _live = new() { Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _recordTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private HashSet<string> _pending = [];

    public SequenceEditor(string title, SequenceSettings sequence, Action save)
    {
        _sequence = sequence; _save = save;
        BorderBrush = new SolidColorBrush(Color.FromRgb(216, 222, 232)); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8);
        Padding = new Thickness(14); Margin = new Thickness(0, 0, 0, 14); Background = Brushes.White;
        var root = new StackPanel(); Child = root;
        root.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.MidnightBlue });
        var content = new Grid { Margin = new Thickness(0, 10, 0, 0) }; content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(300) });
        var left = new StackPanel(); left.Children.Add(new TextBlock { Text = "Sequence steps", FontWeight = FontWeights.SemiBold }); left.Children.Add(_steps);
        var editRow = new WrapPanel();
        editRow.Children.Add(SmallButton("Remove", (_, _) => Remove())); editRow.Children.Add(SmallButton("↑", (_, _) => Move(-1))); editRow.Children.Add(SmallButton("↓", (_, _) => Move(1))); editRow.Children.Add(SmallButton("Clear", (_, _) => { _sequence.Steps.Clear(); Changed(); }));
        left.Children.Add(editRow);
        var timing = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        timing.Children.Add(new TextBlock { Text = "Maximum gap (ms):", VerticalAlignment = VerticalAlignment.Center });
        var timeout = new TextBox { Text = _sequence.MaxGapMs.ToString(), Width = 65, Margin = new Thickness(8, 0, 0, 0) };
        timeout.LostFocus += (_, _) => { if (int.TryParse(timeout.Text, out var n)) { _sequence.MaxGapMs = Math.Clamp(n, 300, 5000); timeout.Text = _sequence.MaxGapMs.ToString(); _save(); } };
        timing.Children.Add(timeout); left.Children.Add(timing);
        Grid.SetColumn(left, 0); content.Children.Add(left);
        var diagram = BuildDiagram(); Grid.SetColumn(diagram, 1); content.Children.Add(diagram); root.Children.Add(content);
        var recordRow = new StackPanel { Orientation = Orientation.Horizontal }; recordRow.Children.Add(_record); recordRow.Children.Add(_live); root.Children.Add(recordRow);
        var add = SmallButton("Add selected buttons as step", (_, _) => AddManual()); add.Padding = new Thickness(10, 6, 10, 6); root.Children.Add(add);
        _recordTimer.Tick += (_, _) => { _recordTimer.Stop(); if (_record.IsChecked == true && _pending.Count > 0) AddStep(_pending); };
        Refresh();
    }

    private UIElement BuildDiagram()
    {
        var grid = new Grid { Margin = new Thickness(18, 0, 0, 0) };
        for (var i = 0; i < 5; i++) grid.RowDefinitions.Add(new());
        for (var i = 0; i < 6; i++) grid.ColumnDefinitions.Add(new());
        Place("LB",0,0,2); Place("RB",0,4,2); Place("View",1,2); Place("Menu",1,3);
        Place("DPadUp",1,0); Place("DPadLeft",2,0); Place("DPadRight",2,1); Place("DPadDown",3,0);
        Place("Y",1,5); Place("X",2,4); Place("B",2,5); Place("A",3,5);
        Place("LeftStick",4,1,2); Place("RightStick",4,3,2);
        return grid;
        void Place(string name, int row, int column, int span=1)
        {
            var b = new Button { Content = Label(name), Margin = new Thickness(2), Padding = new Thickness(5), MinHeight = 31, Tag = name };
            b.Click += (_, _) => Toggle(name); _buttons[name] = b; Grid.SetRow(b,row); Grid.SetColumn(b,column); Grid.SetColumnSpan(b,span); grid.Children.Add(b);
        }
    }

    public void UpdateController(int index, IReadOnlySet<string> pressed, IReadOnlySet<string> newly)
    {
        _live.Text = pressed.Count == 0 ? $"Controller {index + 1} connected" : $"Controller {index + 1}: {string.Join(" + ", pressed.Select(Label))}";
        foreach (var pair in _buttons) pair.Value.Background = pressed.Contains(pair.Key) ? Brushes.LightGreen : (_manualChord.Contains(pair.Key) ? Brushes.LightBlue : SystemColors.ControlBrush);
        if (_record.IsChecked == true && newly.Count > 0) { _pending = pressed.ToHashSet(StringComparer.OrdinalIgnoreCase); _recordTimer.Stop(); _recordTimer.Start(); }
    }

    private void Toggle(string name) { if (!_manualChord.Add(name)) _manualChord.Remove(name); foreach (var p in _buttons) p.Value.Background = _manualChord.Contains(p.Key) ? Brushes.LightBlue : SystemColors.ControlBrush; }
    private void AddManual() { if (_manualChord.Count == 0) return; AddStep(_manualChord); _manualChord.Clear(); foreach (var b in _buttons.Values) b.Background = SystemColors.ControlBrush; }
    private void AddStep(IEnumerable<string> buttons) { var values = Names.Where(n => buttons.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList(); if (values.Count == 0) return; _sequence.Steps.Add(new() { Buttons = values }); Changed(); }
    private void Remove() { if (_steps.SelectedIndex < 0) return; _sequence.Steps.RemoveAt(_steps.SelectedIndex); Changed(); }
    private void Move(int delta) { var i = _steps.SelectedIndex; var target = i + delta; if (i < 0 || target < 0 || target >= _sequence.Steps.Count) return; (_sequence.Steps[i], _sequence.Steps[target]) = (_sequence.Steps[target], _sequence.Steps[i]); Changed(); _steps.SelectedIndex = target; }
    private void Changed() { _save(); Refresh(); }
    private void Refresh() { _steps.ItemsSource = null; _steps.ItemsSource = _sequence.Steps.Select((s,i) => $"{i+1}. {string.Join(" + ", s.Buttons.Select(Label))}").ToList(); }
    private static string Label(string value) => value switch { "DPadUp"=>"D-pad ↑", "DPadDown"=>"D-pad ↓", "DPadLeft"=>"D-pad ←", "DPadRight"=>"D-pad →", "LeftStick"=>"L stick", "RightStick"=>"R stick", _=>value };
    private static Button SmallButton(string text, RoutedEventHandler action) { var b = new Button { Content=text, Margin=new Thickness(0,5,6,0), Padding=new Thickness(8,3,8,3) }; b.Click += action; return b; }
}

public sealed class RollbackWindow : Window
{
    private readonly TextBlock _countdown = new(); private int _seconds; private readonly DispatcherTimer _timer;
    public RollbackWindow(int seconds)
    {
        _seconds = seconds; Title = "Confirm gaming display"; Width = 420; Height = 200; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text="Can you see this clearly on the gaming display?", FontSize=18, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap });
        _countdown.Margin = new Thickness(0,10,0,15); panel.Children.Add(_countdown);
        var row = new WrapPanel();
        var keep = new Button { Content="Keep gaming layout", Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,8,0), IsDefault=true };
        keep.Click += (_,_) => { DialogResult=true; Close(); }; row.Children.Add(keep);
        var revert = new Button { Content="Revert now", Padding=new Thickness(12,7,12,7), IsCancel=true }; revert.Click += (_,_) => { DialogResult=false; Close(); }; row.Children.Add(revert); panel.Children.Add(row); Content=panel;
        _timer = new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) }; _timer.Tick += (_,_) => { _seconds--; Update(); if (_seconds <= 0) { _timer.Stop(); DialogResult=false; Close(); } };
        Loaded += (_,_) => { Update(); _timer.Start(); }; Closed += (_,_) => _timer.Stop();
    }
    private void Update() => _countdown.Text = $"The desktop layout will be restored automatically in {_seconds} seconds.";
}
