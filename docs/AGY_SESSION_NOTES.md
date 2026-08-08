# Bandroom (BandAudioHook) - AGY Session Notes

## Project Overview
- **Location:** `D:\Claude\Projects\tools\BandAudioHook`
- **Application Name:** Bandroom ("Supreme's Stadium Sound Selector")
- **Target Framework:** .NET 10 (`net10.0-windows10.0.19041.0` WinForms + WebView2)
- **Frontend UI:** HTML / CSS / JS inside `wwwroot/` rendered via Microsoft WebView2
- **Audio Engine:** NAudio (sound triggers, volume ducking, reverb, audio player)
- **Key Services:**
  - `GameWatcher.cs`: Game memory / event watcher
  - `WebBridge.cs` & `WebMainForm.cs`: C# <-> JS interop bridge
  - `ConfigStore.cs` & `ConfigProfileManager.cs`: User configuration & profile management
  - `AudioPlayer.cs` & `AudioDuckingController.cs`: Audio playback and dynamic volume ducking
  - `ProfileSyncService.cs` & `GoogleAuthService.cs`: Account & profile sync
  - `MarketplaceDownloadService.cs`: Online marketplace sound presets
  - Clowd.Squirrel: Auto-updater and release distribution

## Conversation & Workflow Setup
- All project source code, assets, scripts, and build artifacts reside directly in `D:\Claude\Projects\tools\BandAudioHook`.
- AGY manages and updates all code files, builds, and scripts directly on `D:`.
