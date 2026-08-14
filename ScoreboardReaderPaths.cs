namespace SupremeStadiumSoundSelector;

/// <summary>Resolves reader-related paths. Two different things live here, kept in one file since
/// both are "where does the reader's stuff live": (1) Coffee's separate, optionally-installed
/// Electron app's `live-scoreboard.json`/theme gallery -- pure filesystem discovery, BANDroom
/// never bundles that whole app, never exposes its settings UI; (2) the bundled RAM reader exe's
/// own working files (2026-08-13 owner-approved absorption -- see the RAM section below), which
/// BANDroom now ships and owns directly. All discovery is best-effort and MUST fail soft: every
/// method returns null on "couldn't find it" rather than throwing, and callers (ScoreboardReaderHost
/// / WebBridge status) treat that as "reader not available," falling back to BANDroom's existing
/// OCR path exactly as it already works today.
///
/// NOTE: the reader's real Electron `productName` (used for its %APPDATA% folder name in
/// "app-data" mode -- see `_scratch_reader\extracted\_src\src__user-data-location.js`) was never
/// confirmed against a live install; the candidate list below is a best-effort guess from the
/// package.json `name` field and the release's own naming. Needs live-install verification.</summary>
internal static class ScoreboardReaderPaths
{
    const string PortableMarkerFileName = "portable-data.json";
    const string FolderLocalUserDataDirName = "UserData";
    const string LiveScoreboardFileName = "live-scoreboard.json";

    static readonly string[] AppDataProductNameCandidates =
    {
        "cfb27-scoreboard-overlay-version-1",
        "CFB27 Scoreboard Overlay",
        "Scorebug Overlay App",
        "CFB27-Scoreboard-Overlay",
    };

    /// <summary>Portable install locations to probe for the `portable-data.json` marker (folder-
    /// local `UserData\` mode) -- Program Files/LocalAppData\Programs are where a typical
    /// Electron-Builder "portable" or per-user install would land it.</summary>
    static IEnumerable<string> PortableInstallDirCandidates()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] names = { "CFB27 Scoreboard Overlay", "Scorebug Overlay App", "cfb27-scoreboard-overlay" };
        foreach (string name in names)
        {
            yield return Path.Combine(localAppData, "Programs", name);
            yield return Path.Combine(programFiles, name);
        }
    }

    /// <summary>Mirrors `resolveUserDataLocation` in the reader's own `user-data-location.js`:
    /// folder-local `UserData\` wins if a `portable-data.json` marker sits next to the install,
    /// otherwise falls back to the `%APPDATA%\<productName>` layout.</summary>
    public static string? ResolveUserDataDirectory()
    {
        foreach (string dir in PortableInstallDirCandidates())
        {
            try
            {
                if (File.Exists(Path.Combine(dir, PortableMarkerFileName)))
                    return Path.Combine(dir, FolderLocalUserDataDirName);
            }
            catch { /* inaccessible/malformed path -- keep probing */ }
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (string productName in AppDataProductNameCandidates)
        {
            string candidate = Path.Combine(appData, productName);
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }

    public static string? ResolveLiveScoreboardJsonPath()
    {
        string? userData = ResolveUserDataDirectory();
        return userData == null ? null : Path.Combine(userData, LiveScoreboardFileName);
    }

    public static string? ResolveThemeLibraryJsonPath()
    {
        string? userData = ResolveUserDataDirectory();
        return userData == null ? null : Path.Combine(userData, "theme-library", "library.json");
    }

    // --- Bundled RAM reader (2026-08-13 owner-approved absorption) ---
    // BANDroom ships its own copy of Coffee's CollegeFB27RamReader.exe (Assets\ScoreboardReader\,
    // see BandAudioHook.csproj) rather than searching for Coffee's separate Electron install --
    // no more "search Program Files for the whole app" (that was the old
    // ResolveReaderExecutablePath, removed). The exe is standalone/self-contained (zero DLL
    // deps) and reads its ram-live-profile.json sidecar from its own directory, so both files
    // just need to ship side by side, exactly as extracted.

    /// <summary>Null only if the build somehow shipped without the bundled exe/profile (should
    /// never happen in a normal build -- BandAudioHook.csproj's Assets\**\* glob always copies
    /// them) -- ScoreboardReaderHost treats null as "RAM reader unavailable," fails soft to OCR.</summary>
    public static string? ResolveBundledRamReaderExecutablePath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "ScoreboardReader", "CollegeFB27RamReader.exe");
        return File.Exists(path) ? path : null;
    }

    public static string? ResolveBundledRamReaderProfilePath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "ScoreboardReader", "ram-live-profile.json");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Bundled copy of Coffee's scorebug theme-library (2026-08-13, same "ship our own
    /// copy" treatment as the RAM reader exe above -- owner wants Coffee's Corner fully
    /// self-contained, "all the bugs he made are in," with zero dependency on his separate reader
    /// being installed at all). The 5 themes shipped at the time of bundling (FOX 2021/2025,
    /// ESPN 2020, NBC 2024, NBC 2024 Monochrome) always show even on a totally fresh BANDroom
    /// install. Null only if the build somehow shipped without it (shouldn't happen -- same
    /// Assets\**\* glob as the exe); GetScorebugThemeGalleryFromWeb treats null as "no bundled
    /// themes," not an error.</summary>
    public static string? ResolveBundledThemeLibraryJsonPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "ScoreboardReader", "theme-library", "library.json");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Working folder for the reader's `--service` files -- BANDroom's own UserData, not
    /// Coffee's Electron `userData` (dataExportRootPath() in his main.js), since BANDroom now owns
    /// this process directly. ScoreboardReaderHost passes `<dir>\ram-reader-status.json` as the
    /// statusPath CLI arg; the reader's actual scoreboard payload is inferred (from main.js's own
    /// `ramLiveDataPath()`/`ramReaderStatusPath()` always sharing one directory) to land as a
    /// sibling `live-game-data.json` in this SAME folder -- never confirmed against a live run
    /// since the exe is closed-source, but strongly implied by Coffee's own source layout.</summary>
    public static string ResolveRamReaderDataDirectory() =>
        Path.Combine(ConfigStore.UserDataRoot, "ScoreboardReader");
}
