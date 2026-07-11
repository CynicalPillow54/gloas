# Game Display Switcher

A Windows tray application that switches to a single landscape gaming display,
opens Steam Big Picture, and restores a captured multi-monitor desktop layout.

The repository root is `%USERPROFILE%\Documents\Projects\gloas`. The published
application is generated in `publish\`; user settings and the captured display
profile remain outside the repository in `%LOCALAPPDATA%\GameDisplaySwitcher`.

## First-time setup

1. Build the application or open `publish\GameDisplaySwitcher.exe`.
2. Use **Identify displays**, then select the intended gaming display.
3. Arrange all monitors in their normal desktop configuration and choose
   **Capture current desktop**.
4. Choose **Test Gaming mode**. It rolls back automatically after 15 seconds
   unless you keep the gaming layout.
5. Connect the Xbox controller and use the Controller sequences tab to record or
   edit both sequences.

Defaults are `View + Menu`, then `A` for Gaming mode and `View + Menu`, then `B`
for Desktop mode. The tray app observes controller input but does not consume it,
so games still receive those button presses.

## Command line

- `GameDisplaySwitcher.exe --gaming`
- `GameDisplaySwitcher.exe --desktop`
- `GameDisplaySwitcher.exe --background`
- `GameDisplaySwitcher.exe --show`

Configuration, the captured desktop profile, and logs are stored in
`%LOCALAPPDATA%\GameDisplaySwitcher`.

## Build and test

From the repository root:

```powershell
dotnet build -c Release
dotnet run --project .\Tests\GameDisplaySwitcher.Tests.csproj -c Release
dotnet publish -c Release -o .\publish
```

The published build is self-contained for Windows x64 and does not require a
separate .NET runtime on the machine where it runs.

The desktop shortcuts use the published executable with `--show`, `--gaming`,
and `--desktop`. If the repository is moved again, recreate or repoint those
shortcuts and the `GameDisplaySwitcher` entry under the current user's Windows
Run registry key.

MultiMonitorTool is included unmodified under its freeware licence. See
`Tools\readme.txt` in the published folder.
