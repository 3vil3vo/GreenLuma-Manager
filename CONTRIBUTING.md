# Contributing to GreenLuma Manager

Thank you for your interest in contributing to GreenLuma Manager!

## Development Setup

1. **Fork and clone the repository**
   ```bash
   git clone https://github.com/YOUR_USERNAME/GreenLuma-Manager.git
   cd GreenLuma-Manager
   ```

2. **Open in Visual Studio**
    - Open `GreenLuma-Manager.slnx` in Visual Studio 2022 or later
    - Make sure you have the .NET 10.0 SDK installed

3. **Restore NuGet packages**
    - Right-click the solution in Solution Explorer
    - Select "Restore NuGet Packages"
    - Packages used: Newtonsoft.Json, SteamKit2, SharpCompress, Microsoft.Web.WebView2

4. **Build and run**
    - Press F5 to build and run in debug mode
    - Or Build → Build Solution (Ctrl+Shift+B)

## Project Structure

- `Controllers/` - Thin classes that connect the UI to the services below
    - `AppListController.cs` - Generating and importing AppLists
    - `GameListController.cs` - Filtering and displaying the current profile's game list
    - `GreenLumaLauncher.cs` - Wraps GreenLuma launch and path checks for the UI
    - `NotificationManager.cs` - Toasts, status indicator, and loading animations
    - `ProfileController.cs` - Loading, saving, and switching profiles
    - `SearchController.cs` - Running searches and showing results
- `Services/` - Core application logic
    - `ConfigService.cs` - Loading, saving, and migrating configuration
    - `ProfileService.cs` - Reading and writing profile files
    - `SearchService.cs` - Matching games against the bundled and Steam API app lists
    - `GreenLumaService.cs` - AppList generation, DLLInjector.ini updates, and launching GreenLuma
    - `GreenLumaVersionPromptService.cs` - The one-time prompt for a mislabeled GreenLuma version
    - `GreenLumaDeploymentService.cs` - Extracts a GreenLuma zip and deploys Normal or Stealth mode files
    - `GreenLumaUpdateService.cs` - Checks the GreenLuma forum thread for a newer release
    - `WebView2Helper.cs` - Shared, isolated WebView2 environment for the forum update check
    - `DepotService.cs` and `SteamService.cs` - Resolving depot and DLC info through SteamKit2
    - `UpdateService.cs` - Checking for and applying app updates
    - `IconCacheService.cs` - Downloading and caching game icons
    - `PluginService.cs` - Loading, enabling, and removing plugins
    - `Logger.cs` - Leveled logging (Debug/Info/Warn/Error) with automatic caller info
    - `HttpClientProvider.cs` - Shared HttpClient instance
- `Models/` - Data models
    - `Config.cs` - Application configuration
    - `Profile.cs` - A saved game list
    - `Game.cs` - A single game or DLC entry, with `INotifyPropertyChanged`
    - `PluginInfo.cs` and `PluginManifest.cs` - Plugin metadata
    - `UpdateInfo.cs` - Update check result
    - `GreenLumaDeploymentResult.cs` - Result of a GreenLuma zip deploy
    - `GreenLumaVersionInfo.cs` - Result of a GreenLuma forum version check
    - `AppListProgressReport.cs` - Progress reporting for long-running AppList operations
- `Plugins/` - `IPlugin.cs`, the interface external plugins implement
- `Dialogs/` - WPF dialog windows
    - `SettingsDialog.xaml` - Settings UI
    - `CreateProfileDialog.xaml` - Profile creation UI
    - `CustomMessageBox.xaml` - Custom message boxes used across the app
    - `GreenLumaVersionDialog.xaml` - The one-time GreenLuma version prompt
    - `PluginsDialog.xaml` - Import, enable, disable, and remove plugins
- `Utilities/` - Helper classes
    - `PathDetector.cs` - Auto-detection of Steam/GreenLuma paths
    - `IconUrlConverter.cs` - WPF value converter for icons
    - `AutostartManager.cs` - Windows startup integration
    - `RelayCommand.cs` - Simple `ICommand` implementation for keyboard shortcuts
- `MainWindow.xaml` - Main application window

## Code Style

- Follow C# naming conventions (PascalCase for public members, camelCase for private)
- Use `async`/`await` for asynchronous operations
- Implement `INotifyPropertyChanged` for data-bound properties
- Keep methods focused and single-purpose
- Use meaningful variable and method names
- Do not add comments that just restate what the code does. Only comment on something that is not obvious from
  the code itself
- Do not use em dashes in code, comments, or documentation
- Match the existing ReSharper formatting style (see the rest of the codebase for examples)

## WPF Best Practices

- Use MVVM patterns where appropriate
- Prefer data binding over code-behind manipulation
- Use `Dispatcher` for UI thread marshaling
- Implement proper resource cleanup in `Dispose` methods
- Use value converters for data transformation in bindings

## Pull Request Process

1. Create a new branch for your feature
2. Make your changes following the code style guidelines above
3. Test thoroughly in both Debug and Release builds
4. Commit with clear, descriptive messages
5. Push to your fork
6. Open a Pull Request with a clear description

## Testing

Before submitting:

- Build in both Debug and Release configurations
- Test all modified features
- Verify no binding errors in the debug output
- Test with both a fresh install and an existing config file
- Check for memory leaks with long-running operations
- Verify icon loading and search still work correctly
- If you touch GreenLuma launching or AppList generation, test with both stealth mode on and off, and with a
  GreenLuma version below 1.8.0 and one at 1.8.0 or above
- If you touch the GreenLuma zip deploy, test both a fresh install into an empty folder and updating an
  already-configured install, for both Normal and Stealth mode
- If you touch anything using WebView2, verify the WebView2 Runtime is installed on the test machine first

## Reporting Bugs

Use the GitHub Issues tab and include:

- A clear description of the bug
- Steps to reproduce
- Expected vs actual behavior
- Screenshots if applicable
- Your Windows version
- The application version

## Feature Requests

Open an issue with:

- A clear description of the feature
- Why it would be useful
- Any implementation ideas
- Potential impact on existing features

## Questions?

Feel free to open a discussion or issue for any questions!
