# GreenLuma Manager

A modern desktop app for managing your GreenLuma AppList. No more entering app IDs one by one - just search, click, and
launch.

![Version](https://img.shields.io/github/v/release/3vil3vo/GreenLuma-Manager?label=version)
![License](https://img.shields.io/badge/license-MIT-green)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)

### Need help or just want to hang out? Check out my [Discord server](https://discord.gg/9Vhtpayj4U) (.gg/9Vhtpayj4U)!

## Features

- **Smart Search** - Find any Steam game or DLC instantly with real-time results from Steam's API
- **Profile Management** - Keep different game lists organized with multiple profiles
- **Auto-Detection** - Automatically finds your Steam and GreenLuma folders
- **One-Click Launch** - Generate your AppList and fire up GreenLuma without the hassle
- **Modern UI** - A clean WPF interface built with MaterialDesign
- **Stealth Mode** - Configure GreenLuma's injection settings for discrete operation
- **Auto-Updates** - Keeps you up to date with the latest features and fixes
- **Auto-Start** - Option to launch with Windows and replace Steam startup

## Getting Started

### Download and Run

1. Grab the latest `GreenLuma-Manager.exe` from [Releases](../../releases)
2. Double-click and run it
3. The app will auto-detect your Steam and GreenLuma paths
4. If it doesn't find them, set them manually in Settings (⚙️)

### Building from Source

#### Prerequisites

- Visual Studio 2022 or later
- .NET 10.0 or higher

#### Steps

1. **Clone the repository**
   ```bash
   git clone https://github.com/3vil3vo/GreenLuma-Manager.git
   cd GreenLuma-Manager
   ```

2. **Open in Visual Studio**
    - Open `GreenLuma-Manager.slnx`

3. **Restore NuGet packages**
    - Right-click solution → Restore NuGet Packages

4. **Build the project**
    - Build → Build Solution (Ctrl+Shift+B)
    - Find the executable in `bin/Debug/` or `bin/Release/`

## How to Use

1. **First Time Setup**
    - On first launch, the app auto-detects Steam and GreenLuma paths
    - If migrating from RC3, your settings and profiles are automatically imported
    - Adjust settings in Settings (⚙️) if needed

2. **Finding Games**
    - Type a game name or AppID into the search box
    - Results appear instantly from Steam's database with icons
    - Click the + button to add games to your current profile

3. **Managing Profiles**
    - Select a profile from the dropdown menu
    - Create new profiles with the + button
    - Add games with +, remove with the delete button
    - Each profile is saved automatically

4. **Launching GreenLuma**
    - Click "Generate AppList" to write files to your GreenLuma folder
    - Hit "Launch GreenLuma" - the app will close Steam and start GreenLuma
    - Enable stealth mode in settings for discrete injection

## Requirements

- Windows 10/11
- .NET 10.0 or higher
- Steam installed
- GreenLuma 2025/6 installed

## Special Thanks

Shoutout to [BlueAmulet's GreenLuma-2025-Manager](https://github.com/BlueAmulet/GreenLuma-2025-Manager) for inspiration.

## Contributing

Found a bug? Want to add a feature? Pull requests are always welcome! Check [CONTRIBUTING.md](CONTRIBUTING.md) for
guidelines.

## License

MIT License - See [LICENSE](LICENSE) for details.

## Disclaimer

This is an educational project. Use it responsibly and at your own risk. We're not responsible if something goes wrong.

## Author

Built with ☕ by [3vil3vo](https://github.com/3vil3vo)

## Need Help?

Ran into an issue or have an idea? [Open an issue](../../issues) and let's fix it!
