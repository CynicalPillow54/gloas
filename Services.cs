using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;

namespace GameDisplaySwitcher;

public sealed class MonitorService
{
    private readonly string _tool = Path.Combine(AppContext.BaseDirectory, "Tools", "MultiMonitorTool.exe");

    public bool IsAvailable => File.Exists(_tool);

    public async Task<List<MonitorInfo>> EnumerateAsync()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"gds-monitors-{Guid.NewGuid():N}.csv");
        try
        {
            await RunAsync("/scomma", csv);
            if (!File.Exists(csv)) return [];
            var rows = ParseCsv(await File.ReadAllTextAsync(csv));
            if (rows.Count < 2) return [];
            var headers = rows[0];
            int Col(params string[] names) => Array.FindIndex(headers, h => names.Any(n => h.Equals(n, StringComparison.OrdinalIgnoreCase)));
            var name = Col("Name"); var id = Col("Monitor ID"); var serial = Col("Monitor Serial Number", "Serial Number");
            var text = Col("Monitor String"); var active = Col("Active");
            return rows.Skip(1).Where(r => name >= 0 && r.Length > name && !string.IsNullOrWhiteSpace(r[name]))
                .Select(r => new MonitorInfo
                {
                    Name = Get(r, name), MonitorId = Get(r, id), Serial = Get(r, serial),
                    MonitorString = Get(r, text), Active = Get(r, active).Equals("Yes", StringComparison.OrdinalIgnoreCase)
                }).GroupBy(m => m.StableId).Select(g => g.First()).ToList();
        }
        finally { try { File.Delete(csv); } catch { } }
    }

    public async Task CaptureDesktopAsync() => await RunAsync("/SaveConfig", SettingsStore.DesktopProfilePath);

    public async Task RestoreDesktopAsync()
    {
        if (!File.Exists(SettingsStore.DesktopProfilePath)) throw new InvalidOperationException("Capture the desktop layout first.");
        await RunAsync("/LoadConfig", SettingsStore.DesktopProfilePath);
    }

    public async Task ApplyGamingAsync(string stableId)
    {
        var monitors = await EnumerateAsync();
        var target = monitors.FirstOrDefault(m => m.StableId.Equals(stableId, StringComparison.OrdinalIgnoreCase));
        if (target is null) throw new InvalidOperationException("The configured gaming display is not connected.");
        await RunAsync("/enable", target.StableId);
        await RunAsync("/SetOrientation", target.StableId, "0");
        await RunAsync("/SetPrimary", target.StableId);
        var others = monitors.Where(m => !m.StableId.Equals(target.StableId, StringComparison.OrdinalIgnoreCase) && m.Active)
            .Select(m => m.StableId).ToArray();
        if (others.Length > 0) await RunAsync(["/disable", .. others]);
        await Task.Delay(1200);
        var after = await EnumerateAsync();
        var active = after.Where(m => m.Active).ToList();
        if (active.Count != 1 || !active[0].StableId.Equals(target.StableId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows did not accept the requested display layout.");
    }

    public void ShowIdentifiers()
    {
        var windows = new List<Window>();
        foreach (var screen in Screen.AllScreens)
        {
            var displayNumber = screen.DeviceName.Replace(@"\\.\DISPLAY", "", StringComparison.OrdinalIgnoreCase);
            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 20, 25, 35)),
                Topmost = true, ShowInTaskbar = false, Left = screen.Bounds.Left + 30, Top = screen.Bounds.Top + 30,
                Width = Math.Min(360, screen.Bounds.Width - 60), Height = 190,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = $"Display {displayNumber}\n{screen.DeviceName}", Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 38, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            w.Show(); windows.Add(w);
        }
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); windows.ForEach(w => w.Close()); };
        timer.Start();
    }

    private async Task RunAsync(params string[] args)
    {
        if (!IsAvailable) throw new FileNotFoundException("MultiMonitorTool.exe is missing from the Tools folder.");
        var psi = new ProcessStartInfo(_tool) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start MultiMonitorTool.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"MultiMonitorTool returned exit code {process.ExitCode}.");
    }

    private static string Get(string[] row, int i) => i >= 0 && i < row.Length ? row[i] : "";
    private static List<string[]> ParseCsv(string input)
    {
        var result = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '"') { if (quoted && i + 1 < input.Length && input[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((c == '\r' || c == '\n') && !quoted)
            {
                if (c == '\r' && i + 1 < input.Length && input[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear(); if (row.Any(x => x.Length > 0)) result.Add(row.ToArray()); row.Clear();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); result.Add(row.ToArray()); }
        return result;
    }
}

public static class SteamService
{
    public static void OpenBigPicture()
    {
        Process.Start(new ProcessStartInfo("steam://open/bigpicture") { UseShellExecute = true });
    }
}

public static class StartupService
{
    private const string KeyName = "GameDisplaySwitcher";
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue(KeyName, $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue(KeyName, false);
    }
}

[Flags]
public enum PadButtons : ushort
{
    DPadUp=0x0001, DPadDown=0x0002, DPadLeft=0x0004, DPadRight=0x0008,
    Menu=0x0010, View=0x0020, LeftStick=0x0040, RightStick=0x0080,
    LB=0x0100, RB=0x0200, A=0x1000, B=0x2000, X=0x4000, Y=0x8000
}

public sealed class GamepadService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct XInputState { public uint Packet; public XInputGamepad Gamepad; }
    [StructLayout(LayoutKind.Sequential)] private struct XInputGamepad
    { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY; }
    [DllImport("xinput1_4.dll", EntryPoint="XInputGetState")] private static extern uint GetState(uint index, out XInputState state);

    private readonly System.Threading.Timer _timer;
    private readonly ushort[] _previous = new ushort[4];
    public event Action<int, IReadOnlySet<string>, IReadOnlySet<string>>? StateChanged;
    public GamepadService() => _timer = new(_ => Poll(), null, 0, 33);
    private void Poll()
    {
        for (uint i = 0; i < 4; i++)
        {
            if (GetState(i, out var state) != 0) { _previous[i] = 0; continue; }
            var now = state.Gamepad.Buttons;
            if (now == _previous[i]) continue;
            var pressed = Names(now); var newly = Names((ushort)(now & ~_previous[i])); _previous[i] = now;
            StateChanged?.Invoke((int)i, pressed, newly);
        }
    }
    private static HashSet<string> Names(ushort value) => Enum.GetValues<PadButtons>()
        .Where(x => (value & (ushort)x) != 0).Select(x => x.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public void Dispose() => _timer.Dispose();
}

public sealed class SequenceMatcher
{
    private readonly Func<SequenceSettings> _settings;
    private int _step; private int _controller = -1; private DateTime _last = DateTime.MinValue; private DateTime _cooldown = DateTime.MinValue;
    public SequenceMatcher(Func<SequenceSettings> settings) => _settings = settings;
    public bool Feed(int controller, IReadOnlySet<string> currentlyPressed, IReadOnlySet<string> newlyPressed)
    {
        var config = _settings(); if (config.Steps.Count == 0 || newlyPressed.Count == 0 || DateTime.UtcNow < _cooldown) return false;
        if (_step > 0 && (controller != _controller || (DateTime.UtcNow - _last).TotalMilliseconds > config.MaxGapMs)) Reset();
        var expected = config.Steps[_step].Buttons.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expected.SetEquals(currentlyPressed))
        {
            _controller = controller; _last = DateTime.UtcNow; _step++;
            if (_step == config.Steps.Count) { Reset(); _cooldown = DateTime.UtcNow.AddSeconds(3); return true; }
        }
        else if (_step == 0 && config.Steps[0].Buttons.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(currentlyPressed))
        { _controller = controller; _last = DateTime.UtcNow; _step = 1; }
        return false;
    }
    private void Reset() { _step = 0; _controller = -1; _last = DateTime.MinValue; }
}
