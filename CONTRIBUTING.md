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
    - Ensure you have .NET 10.0 SDK installed

3. **Restore NuGet packages**
    - Right-click the solution in Solution Explorer
    - Select "Restore NuGet Packages"
    - Package: Newtonsoft.Json

4. **Build and run**
    - Press F5 to build and run in debug mode
    - Or Build → Build Solution (Ctrl+Shift+B)

## Project Structure

- `Services/` - Core application services
    - `ConfigService.cs` - Configuration management and RC3 migration
    - `ProfileService.cs` - Profile management and RC3 migration
    - `SearchService.cs` - Steam game search and icon fetching
    - `GreenLumaService.cs` - AppList generation and GreenLuma launch
    - `UpdateService.cs` - Auto-update functionality
    - `IconCacheService.cs` - Icon caching and management
- `Models/` - Data models
    - `Config.cs` - Application configuration model
    - `Profile.cs` - Game profile model
    - `Game.cs` - Game data with INotifyPropertyChanged
    - `UpdateInfo.cs` - Update information model
- `Dialogs/` - WPF dialog windows
    - `SettingsDialog.xaml` - Settings UI
    - `CreateProfileDialog.xaml` - Profile creation UI
    - `CustomMessageBox.xaml` - Custom message boxes
- `Utilities/` - Helper classes
    - `PathDetector.cs` - Auto-detection of Steam/GreenLuma paths
    - `IconUrlConverter.cs` - WPF value converter for icons
    - `AutostartManager.cs` - Windows startup integration
- `MainWindow.xaml` - Main application window

## Code Style

- Follow C# naming conventions (PascalCase for public members, camelCase for private)
- Use `async`/`await` for asynchronous operations
- Implement `INotifyPropertyChanged` for data-bound properties
- Keep methods focused and single-purpose
- Use meaningful variable and method names
- Add XML documentation comments for public APIs

## WPF Best Practices

- Use MVVM patterns where appropriate
- Leverage data binding over code-behind manipulation
- Use `SynchronizationContext` for UI thread marshaling
- Implement proper resource cleanup in `Dispose` methods
- Use value converters for data transformation in bindings

## Pull Request Process

1. Create a new branch for your feature
2. Make your changes following code style guidelines
3. Test thoroughly in both Debug and Release builds
4. Commit with clear, descriptive messages
5. Push to your fork
6. Open a Pull Request with a clear description

## Testing

Before submitting:

- Build in both Debug and Release configurations
- Test all modified features
- Verify no binding errors in debug output
- Test with both fresh install and RC3 migration scenarios
- Check for memory leaks with long-running operations
- Verify icon loading works correctly

## Reporting Bugs

Use the GitHub Issues tab and include:

- Clear description of the bug
- Steps to reproduce
- Expected vs actual behavior
- Screenshots if applicable
- Your Windows version
- Application version

## Feature Requests

Open an issue with:

- Clear description of the feature
- Why it would be useful
- Any implementation ideas
- Potential impact on existing features

## Questions?

Feel free to open a discussion or issue for any questions!
