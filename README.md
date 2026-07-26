# GreenLuma Manager

A modern desktop app for managing your GreenLuma AppList. No more entering app IDs one by one - just search, click, and launch.

![Version](https://img.shields.io/github/v/release/3vil3vo/GreenLuma-Manager?label=version)
![License](https://img.shields.io/badge/license-AGPL--3.0-green)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)

### Need help or just want to hang out? Check out my [Discord server](https://discord.gg/9Vhtpayj4U) (.gg/9Vhtpayj4U)!

## Features

- **Smart Search** - Find any Steam game or DLC by name or App ID. Add a Steam API key in Settings for the fullest,
  most up to date results
- **Profile Management** - Keep different game lists organized with multiple profiles
- **Auto-Detection** - Automatically finds your Steam and GreenLuma folders
- **One-Click Launch** - Generate your AppList and start GreenLuma without the hassle
- **AppList.ini Support** - Uses the newer AppList.ini format on GreenLuma 1.8.0 and up, and falls back to the
  classic per-file format on older versions
- **Plugin Support** - Import, enable, disable, and remove community plugins from the Plugins menu
- **Stealth Mode** - Configure GreenLuma's injection settings for discreet operation
- **Install / Update GreenLuma** - Point the app at a GreenLuma zip file and it extracts and deploys the right files for Normal or Stealth mode, for a fresh install or an existing one
- **GreenLuma Update Check** (optional) - Checks the GreenLuma forum thread for a newer release than what's installed
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
    - Older config files are migrated automatically
    - Adjust settings in Settings (⚙️) if needed
    - If GreenLuma reports version 1.7.9, you will see a one-time prompt asking whether it should be treated as
      1.7.9 or 1.8.0. This only happens once, and can be changed later in Settings if needed

2. **Installing or Updating GreenLuma** (optional)
    - In Settings, Advanced tab, browse for a GreenLuma zip file
    - Pick Normal or Stealth mode and click "Install / Update"
    - Works for a fresh install or refreshing an existing one, using the GreenLuma Directory set on the General tab
    - Turn on "Check GreenLuma Forum for Updates" in Settings to get notified when a newer release is posted

3. **Finding Games**
    - Type a game name or App ID into the search box
    - Add a Steam API key in Settings to get the full, current Steam catalog. Without a key, search only uses the
      list bundled with the app
    - Click the + button to add games to your current profile

4. **Managing Profiles**
    - Select a profile from the dropdown menu
    - Create new profiles with the + button
    - Add games with +, remove with the delete button
    - Each profile is saved automatically

5. **Launching GreenLuma**
    - Click "Generate AppList" to write files to your GreenLuma folder
    - Click "Launch GreenLuma" and the app will close Steam and start GreenLuma
    - Enable stealth mode in settings for discreet injection

6. **Managing Plugins** (optional)
    - Open the Plugins menu to import a plugin file
    - Enable, disable, or remove plugins from the same menu

## Requirements

- Windows 10/11
- .NET 10.0 or higher
- Steam installed
- GreenLuma installed

## Special Thanks

Shoutout to [BlueAmulet's GreenLuma-2025-Manager](https://github.com/BlueAmulet/GreenLuma-2025-Manager) for inspiration.

## Contributing

Found a bug? Want to add a feature? Pull requests are always welcome! Check [CONTRIBUTING.md](CONTRIBUTING.md) for
guidelines.

## License

GNU Affero General Public License v3.0 - See [LICENSE](LICENSE) for details.

## Disclaimer

This is an educational project. Use it responsibly and at your own risk. We are not responsible if something
goes wrong.

## Author

Built with ☕ by [3vil3vo](https://github.com/3vil3vo)

## Need Help?

Ran into an issue or have an idea? [Open an issue](../../issues) and let's fix it!
