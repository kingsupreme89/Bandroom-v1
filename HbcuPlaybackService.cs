namespace SupremeStadiumSoundSelector;

/// <summary>Drives HBCU mode's continuous "both bands trade off playing full songs all game"
/// playback: a per-side shuffled queue built from that team's Team Pot (ConfigStore.GetHbcuPot --
/// an unlimited, freely add/remove song list, each entry carrying its own whistle/speed/PA-
/// effect/fade settings just like a TriggerEntry), falling back to the team's downloaded pack
/// (ConfigStore.GetPackFilesForSchool, wrapped in default-settings HbcuPotSong records) if the
/// pot is still empty. Sides strictly alternate turn-by-turn on one timer chain -- only one band
/// is ever actually sounding at a time, same as a real game -- auto-advancing to the next
/// shuffled track 20s after the current one ends (AdvanceGap), same gap used after the Opening
/// Kickoff song itself (PostKickoffGap).
///
/// Runout ("Other: Pregame Take the Field"), Touchdown, Kickoff, and Pregame Ready are NOT queued
/// here -- WebMainForm keeps routing those through the normal EventRouter/TriggerEntry path even
/// in HBCU mode:
///  - Runout/Ready/Kickoff pause the whole chain briefly (Pause/Resume) so they don't race
///    whichever cue is assigned -- if nothing was actually assigned, FireEventForSide's normal
///    "unassigned" no-op means Resume just picks the shuffle back up like nothing happened.
///  - Touchdown (OnTouchdown): REWORKED 2026-08-14 -- whatever's currently playing (either side,
///    including the scoring band's own track) fades out, the TD cue plays clean, then the
///    scoring band gets one bonus song from their own pot before normal alternating rotation
///    resumes unaffected (the bonus doesn't consume that side's next real turn).</summary>
internal sealed class HbcuPlaybackService : IDisposable
{
    /// <summary>Gap between one track ending and the next starting -- owner request 2026-08-14:
    /// same 20s gap as PostKickoffGap (below), applied uniformly to every transition in the
    /// shuffle chain, not just the one right after Opening Kickoff. Was 3s.</summary>
    static readonly TimeSpan AdvanceGap = TimeSpan.FromSeconds(20);

    /// <summary>FIXED 2026-08-14 (owner report, live: dashboard showed "200 sec" until the next
    /// track -- DefaultAssumedDuration(180s) + AdvanceGap(20s) exactly). Team Pot songs are
    /// commonly short hype clips added straight from the Clipper without ever going through the
    /// "preview/analyze" flow that writes a .meta.json sidecar (see AudioTrackMetadataStore),
    /// so this fallback was hit constantly -- and blindly assuming every uncached pot song is a
    /// full 3-minute track badly overshot the real (often ~10-30s) clip length. Reads the file's
    /// REAL duration directly via NAudio the same way WebMainForm.ResolveEventSongDuration
    /// already does for event cues, only falling back to the 3-minute guess if the file is
    /// missing or genuinely unreadable.</summary>
    static readonly TimeSpan DefaultAssumedDuration = TimeSpan.FromMinutes(3);

    static TimeSpan ResolveSongDuration(ConfigStore.HbcuPotSong song)
    {
        var meta = AudioTrackMetadataStore.Load(song.FilePath);
        if (meta?.DurationSeconds is > 0) return TimeSpan.FromSeconds(meta.DurationSeconds.Value);
        try { using var reader = new NAudio.Wave.AudioFileReader(song.FilePath); return reader.TotalTime; }
        catch { return DefaultAssumedDuration; }
    }

    readonly Action<string /*side*/, ConfigStore.HbcuPotSong> _playForSide;
    readonly string _homeTeam;
    readonly string _awayTeam;
    /// <summary>Added 2026-08-14 (owner request: explicit "use Generic pack" picker for a team
    /// with no HBCU pot/pack of its own, typically the opponent in an HBCU matchup) -- when set,
    /// Refill looks up the pot/pack under the "Generic" sentinel team name instead of this side's
    /// real team name. _homeTeam/_awayTeam themselves are left untouched so Status/the dashboard
    /// still display the real matchup, not "Generic" -- only the pot/pack LOOKUP key changes.</summary>
    readonly bool _homeUseGenericPack;
    readonly bool _awayUseGenericPack;
    const string GenericPackTeamName = "Generic";

    readonly Queue<ConfigStore.HbcuPotSong> _homeQueue = new();
    readonly Queue<ConfigStore.HbcuPotSong> _awayQueue = new();
    readonly Random _rng = new();

    /// <summary>Owner request 2026-08-15: once a full cycle of a side's own Team Pot has played
    /// through, the next Refill switches that side over to the shared Generic pack for a round
    /// instead of immediately re-shuffling the same handful of team songs -- alternates own/
    /// Generic every time that side's queue empties (see Refill), so a small pot doesn't loop
    /// itself into the ground over a whole game.</summary>
    bool _homeUseGenericNextRefill;
    bool _awayUseGenericNextRefill;

    string _nextTurn = "home";
    string? _currentlyPlayingSide;
    string? _currentSongName;
    System.Threading.Timer? _advanceTimer;
    bool _paused;
    bool _running;

    /// <summary>UTC time the currently-armed timer (start delay or advance-to-next-track) fires
    /// -- null when nothing's pending (e.g. paused, or queues empty with nothing to schedule).
    /// Purely for the dashboard's countdown display, doesn't drive any playback decision itself.</summary>
    DateTime? _nextTimerUtc;

    public readonly record struct Status(
        bool Running, bool Paused, bool StartPending, double? SecondsUntilNext,
        string? CurrentlyPlayingSide, string? CurrentSongName,
        int HomeQueueCount, int AwayQueueCount, string HomeTeam, string AwayTeam);

    /// <summary>Snapshot for the "\" hotkey dashboard -- read-only, doesn't touch any playback
    /// state. SecondsUntilNext counts down to whichever timer's actually armed (kickoff-delay
    /// start, or the next track's auto-advance); null if paused/stopped/nothing scheduled.</summary>
    public Status GetStatus() => new(
        _running, _paused, _startTimer != null,
        _nextTimerUtc is DateTime t ? Math.Max(0, (t - DateTime.UtcNow).TotalSeconds) : null,
        _currentlyPlayingSide, _currentSongName,
        _homeQueue.Count, _awayQueue.Count, _homeTeam, _awayTeam);

    public HbcuPlaybackService(string homeTeam, string awayTeam, Action<string, ConfigStore.HbcuPotSong> playForSide,
        bool homeUseGenericPack = false, bool awayUseGenericPack = false)
    {
        _homeTeam = homeTeam;
        _awayTeam = awayTeam;
        _homeUseGenericPack = homeUseGenericPack;
        _awayUseGenericPack = awayUseGenericPack;
        _playForSide = playForSide;
    }

    /// <summary>Gap after the Kickoff SONG ITSELF finishes, before the pot's first shuffled track
    /// starts -- owner request 2026-08-14 (revised from an earlier version of this that measured
    /// the 20s from the Kickoff EVENT firing instead, which cut the shuffle in too early whenever
    /// the assigned kickoff song ran longer than 20s).</summary>
    public static readonly TimeSpan PostKickoffGap = TimeSpan.FromSeconds(20);

    System.Threading.Timer? _startTimer;

    /// <param name="kickoffSongDuration">How long the Kickoff event's own assigned song plays for
    /// (TimeSpan.Zero if nothing's assigned/no duration metadata) -- the shuffle's first track
    /// starts PostKickoffGap after that song ends, not PostKickoffGap after this call.</param>
    public void Start(TimeSpan kickoffSongDuration = default)
    {
        // TEMP DIAGNOSTIC 2026-08-18 (owner report: "hub isn't auto starting after opening
        // kickoff event") -- this method had zero logging, so there was no way to tell whether
        // it was never being CALLED at all (e.g. Opening Kickoff not detected/routed, or
        // _hbcuPlayback null at that moment), called but no-op'd by the _running/_startTimer
        // guard below, or called and genuinely armed a timer that then silently never fired.
        // Logs which of those happened every time. Remove once the cause is found.
        CrashLog.Write("HbcuPlaybackService.Start called", new Exception(
            $"kickoffSongDuration={kickoffSongDuration.TotalSeconds:F1}s alreadyRunning={_running} startTimerAlreadyArmed={_startTimer != null}"));
        if (_running || _startTimer != null) return;
        var delay = kickoffSongDuration + PostKickoffGap;
        _nextTimerUtc = DateTime.UtcNow + delay;
        _startTimer = new System.Threading.Timer(_ =>
        {
            CrashLog.Write("HbcuPlaybackService.Start timer fired", new Exception("starting first pot turn now"));
            _startTimer?.Dispose();
            _startTimer = null;
            _running = true;
            _nextTurn = "home";
            PlayNext();
        }, null, delay, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>Dashboard "Start" button (owner request 2026-08-14) -- manual restart, distinct
    /// from Start() above: clears BOTH queues so the next Refill does a fresh Fisher-Yates shuffle
    /// of the whole pot rather than continuing wherever the old queues left off (no "already
    /// played this game" memory carries over), and begins immediately -- no PostKickoffGap delay,
    /// since there's no kickoff song to wait out here, just an owner pressing a button.</summary>
    public void Restart()
    {
        // TEMP DIAGNOSTIC 2026-08-17 (owner report: pot "keeps skipping over a track then
        // restarting") -- this is the ONE call in the whole class that wipes both queues and
        // starts completely over, so if it's firing when nobody pressed the dashboard's Start
        // button, that's the "restarting" half of the report. Logs a stack trace so a stray/
        // unexpected caller is identifiable, not just "Restart happened." Remove once the cause
        // is found.
        CrashLog.Write("HbcuPlaybackService.Restart called", new Exception(Environment.StackTrace));
        Stop();
        _homeQueue.Clear();
        _awayQueue.Clear();
        _running = true;
        _nextTurn = "home";
        PlayNext();
    }

    /// <summary>FIXED 2026-08-14 (owner report, live: dashboard pills "make all the songs play
    /// and don't really stop and start properly"). This used to only reset internal scheduling
    /// state -- it never actually silenced whatever was CURRENTLY sounding through the speakers.
    /// So Stop-then-Start (Restart calls Stop() first) left the old song still audibly playing
    /// while a brand new one started on top of it, and repeated presses could stack several
    /// overlapping tracks with nothing ever really stopping. Now fades out both channels for
    /// real, same fade AudioPlayer.FadeOutChannel already uses for a touchdown interrupt (quick,
    /// not instant-cut, so it doesn't sound like a hard glitch).</summary>
    public void Stop()
    {
        _running = false;
        _paused = false;
        _kickoffFallbackActive = false;
        _homeUseGenericNextRefill = false;
        _awayUseGenericNextRefill = false;
        _currentlyPlayingSide = null;
        _currentSongName = null;
        _nextTimerUtc = null;
        _startTimer?.Dispose();
        _startTimer = null;
        _advanceTimer?.Dispose();
        _advanceTimer = null;
        _touchdownTimer?.Dispose();
        _touchdownTimer = null;
        AudioPlayer.FadeOutChannel("home", 0.4);
        AudioPlayer.FadeOutChannel("away", 0.4);
    }

    /// <summary>Called around a Runout/Ready/Kickoff fire so this service doesn't race that cue by
    /// auto-advancing underneath it. Deliberately does NOT touch whatever's currently sounding --
    /// those internal callers want the in-progress pot song to keep playing underneath the
    /// interrupting cue, only the scheduler needs to stand down. See ManualPause for the dashboard
    /// button's own, different "actually go silent" behavior.</summary>
    public void Pause()
    {
        _paused = true;
        _nextTimerUtc = null; // ScheduleAdvance's timer is still armed underneath, but PlayNext no-ops on it while paused -- don't show a misleading countdown
    }

    /// <summary>Dashboard "Pause" button (owner report 2026-08-17: pressing it left whatever song
    /// was already playing still audibly running -- plain Pause() above is deliberately silent-only
    /// scheduling freeze, built for brief internal races that WANT the current cue to keep playing.
    /// A person pressing a Pause button expects actual silence, not just "nothing new will start."
    /// Also fixes the reported "Resume immediately started a second song on top of the first" --
    /// that happened because the first song was still genuinely playing (this method wasn't
    /// silencing it) when Resume's PlayNext() started the next side's turn on its own channel.</summary>
    public void ManualPause()
    {
        Pause();
        if (_currentlyPlayingSide != null) AudioPlayer.FadeOutChannel(_currentlyPlayingSide, 0.4);
    }

    /// <summary>Dashboard "Next" button (owner request 2026-08-17). Cuts whatever's currently
    /// playing and immediately advances to the next side's turn -- same strict home/away
    /// alternation and Refill logic PlayNext always uses, just skipping the wait for AdvanceGap.
    /// Clears _paused so pressing Next while manually paused also resumes the chain, rather than
    /// silently no-op'ing (PlayNext returns immediately if still paused).</summary>
    public void SkipCurrent()
    {
        if (!_running) return;
        _advanceTimer?.Dispose();
        _advanceTimer = null;
        if (_currentlyPlayingSide != null) AudioPlayer.FadeOutChannel(_currentlyPlayingSide, 0.15);
        _paused = false;
        PlayNext();
    }

    /// <summary>Resumes shuffle a beat after the interrupting cue finishes (or immediately picks
    /// the pool back up if no cue was actually assigned for that event). FIXED 2026-08-14 (owner
    /// report: dashboard pills causing overlapping/stacked playback): a rapid double-click of the
    /// Resume pill used to call PlayNext() TWICE in a row -- since PlayNext alternates sides on
    /// every call, the second call would immediately start the OTHER side's song on top of the
    /// first one that had just started, with nothing to stop it (different channel). Guarding on
    /// `_paused` makes a redundant Resume (already running, not actually paused) a clean no-op --
    /// every legitimate internal caller only ever calls this while genuinely paused, so this
    /// doesn't change any of their real behavior.</summary>
    public void Resume()
    {
        if (!_running || !_paused || _kickoffFallbackActive) return;
        _paused = false;
        PlayNext();
    }

    /// <summary>True from PlayKickoffFallback() until its scheduled Resume() fires -- same
    /// "block external Resume calls mid-sequence" purpose as _tdSequenceActive.</summary>
    bool _kickoffFallbackActive;

    System.Threading.Timer? _touchdownTimer;

    /// <summary>Owner request 2026-08-15: a post-touchdown Kickoff with no song assigned for the
    /// scoring side used to leave the chain paused with nothing playing (WebMainForm's blind 8s
    /// pause-then-resume, tuned for an actual Kickoff cue, just expired into silence). Grabs one
    /// song from the scoring side's own pot instead -- same dequeue/refill logic PlayTouchdownBonus
    /// uses -- and resumes normal alternating rotation AdvanceGap (20s) after it ends, same as
    /// every other track transition in the shuffle chain. No-ops (straight to Resume) if that
    /// side's pot is empty too.</summary>
    public void PlayKickoffFallback(string side)
    {
        if (!_running) return;
        Pause();
        _kickoffFallbackActive = true;
        _touchdownTimer?.Dispose();

        var queue = side == "home" ? _homeQueue : _awayQueue;
        if (queue.Count == 0) Refill(side, queue);
        if (queue.Count == 0) { _kickoffFallbackActive = false; Resume(); return; }

        _currentlyPlayingSide = side;
        var song = queue.Dequeue();
        _currentSongName = Path.GetFileNameWithoutExtension(song.FilePath);
        _playForSide(side, song);

        var delay = ResolveSongDuration(song) + AdvanceGap;
        _nextTimerUtc = DateTime.UtcNow + delay;
        _touchdownTimer = new System.Threading.Timer(_ => { _kickoffFallbackActive = false; Resume(); }, null, delay, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>SIMPLIFIED 2026-08-17 (owner spec: "if team scores then the pot stops and only
    /// continues 20 sec after kickoff song has finished") -- previously this fed the scoring side
    /// one bonus pot song between the TD cue and the Kickoff cue, then auto-resumed on its own
    /// timer; that raced/overlapped with WebMainForm's OWN pause-then-resume around the ensuing
    /// Kickoff cue (which already waits kickoffSongDuration + PostKickoffGap, see its
    /// kickoffFireSide handling), and didn't match what the owner actually wants: score cue (if
    /// any), then Kickoff cue, nothing else in between, pot picks back up 20s after the Kickoff
    /// song ends. So this now does exactly one thing: fades out whatever's currently playing
    /// (including the scoring side's own track) so a following score cue plays clean, then pauses
    /// and STAYS paused -- WebMainForm's Kickoff-cue handling (or, if nothing's assigned there,
    /// PlayKickoffFallback below) is what actually calls Resume() once the kickoff moment has
    /// fully played out. Renamed from OnTouchdown (owner clarification 2026-08-17: a field goal
    /// needs this exact same pot-stops-then-kickoff-resumes treatment even though it has no
    /// Touchdown cue of its own to play first -- HBCU mode has no per-event card for Field Goal
    /// Made at all, so WebMainForm just silences the pot and lets the following Kickoff carry it,
    /// same as a touchdown minus the TD cue).</summary>
    public void PauseForScore()
    {
        if (_currentlyPlayingSide != null)
            AudioPlayer.FadeOutChannel(_currentlyPlayingSide);

        if (!_running) return;
        Pause();
    }

    void PlayNext()
    {
        if (!_running || _paused) return;

        string side = _nextTurn;
        _nextTurn = _nextTurn == "home" ? "away" : "home";
        _currentlyPlayingSide = side;

        var queue = side == "home" ? _homeQueue : _awayQueue;
        if (queue.Count == 0) Refill(side, queue);
        // FIXED 2026-08-14 (owner report: "keep them shuffling no matter if they've played or
        // not" -- a team with an empty pot/pack used to sit fully idle for the whole 3-minute
        // DefaultAssumedDuration before even trying again. Retrying after just AdvanceGap (20s,
        // same cadence as everything else) means a pot that gets songs added mid-game -- or the
        // OTHER side's turn, which still proceeds normally -- picks back up quickly instead of
        // this side effectively freezing the rotation for minutes at a time.
        if (queue.Count == 0) { _currentSongName = null; ScheduleAdvance(AdvanceGap); return; }

        var song = queue.Dequeue();
        _currentSongName = Path.GetFileNameWithoutExtension(song.FilePath);
        _playForSide(side, song);

        var resolvedDuration = ResolveSongDuration(song);
        // TEMP DIAGNOSTIC 2026-08-17 (owner report: pot "keeps skipping over a track then
        // restarting" -- no exceptions ever showed up in crash.log for this, so whatever's
        // happening is a scheduling issue, not a crash. Logging every advance decision (which
        // side, which song, how long ResolveSongDuration thinks it is, and the resulting
        // ScheduleAdvance delay) so the next occurrence leaves a real trace: a resolved duration
        // that's way too short would explain a track getting cut off early (StopChannel on the
        // OTHER side's next-turn song would then audibly cut this one short, reading as "skipped"),
        // and any call landing here while queue.Count was already 0 before Refill ran would help
        // spot a queue-exhaustion loop that reads as "restarting." Remove once the cause is found.
        CrashLog.Write("HbcuPlaybackService.PlayNext diagnostic", new Exception(
            $"side={side} song={_currentSongName} resolvedDurationSec={resolvedDuration.TotalSeconds:F1} " +
            $"queueCountAfterDequeue={queue.Count} nextTurn={_nextTurn} homeQueue={_homeQueue.Count} awayQueue={_awayQueue.Count}"));

        ScheduleAdvance(resolvedDuration + AdvanceGap);
    }

    void ScheduleAdvance(TimeSpan delay)
    {
        _advanceTimer?.Dispose();
        _nextTimerUtc = DateTime.UtcNow + delay;
        _advanceTimer = new System.Threading.Timer(_ => PlayNext(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>Pot/pack lookup for one team name -- own-pot first, own-downloaded-pack second
    /// (same two-tier lookup Refill always did), factored out so Refill can walk it for more than
    /// one team name in a row.</summary>
    static List<ConfigStore.HbcuPotSong> PoolForTeam(string team)
    {
        var pool = ConfigStore.GetHbcuPot(team)
            .GroupBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Where(s => File.Exists(s.FilePath)).ToList();
        if (pool.Count > 0) return pool;

        // Fallback pack files never went through the normal per-event assignment path (see
        // WebMainForm.NormalizeAssignmentInBackground), so they'd otherwise be un-normalized
        // and stick out as louder/quieter than everything else -- normalize on the way in,
        // same target/cache LoudnessNormalizationService already uses for real assignments.
        return ConfigStore.GetPackFilesForSchool(team)
            .Where(File.Exists)
            .Select(f => new ConfigStore.HbcuPotSong { FilePath = LoudnessNormalizationService.NormalizeToTarget(f, LoudnessKind.Song) })
            .ToList();
    }

    void Refill(string side, Queue<ConfigStore.HbcuPotSong> queue)
    {
        bool useGeneric = side == "home" ? _homeUseGenericPack : _awayUseGenericPack;
        string team = side == "home" ? _homeTeam : _awayTeam;
        string otherTeam = side == "home" ? _awayTeam : _homeTeam;

        // Owner request 2026-08-15: a side with no pot/pack of its own (typically the HBCU-
        // opponent side, e.g. an FBS team with nothing HBCU-specific assigned) used to just sit
        // silent all game unless someone remembered to flip "Use Generic Pack" on it by hand.
        // Same "vs" idea as the explicit Generic toggle already had (side-specific team-name
        // lookup, see _homeUseGenericPack/_awayUseGenericPack above) -- now automatic: an explicit
        // "Use Generic Pack" toggle still goes straight to Generic (owner's forced override stays
        // absolute), but otherwise an empty side first borrows from the OTHER side's pot/pack
        // (playing your own team's songs beats silence) before finally falling back to Generic.
        List<ConfigStore.HbcuPotSong> pool;
        if (useGeneric)
        {
            pool = PoolForTeam(GenericPackTeamName);
        }
        else
        {
            var ownPool = PoolForTeam(team);
            if (ownPool.Count == 0)
            {
                pool = PoolForTeam(otherTeam);
                if (pool.Count == 0) pool = PoolForTeam(GenericPackTeamName);
            }
            else
            {
                // Alternate own pot / Generic pack every time this side's queue empties (see
                // _homeUseGenericNextRefill's doc comment) -- only when Generic actually has
                // songs to alternate with, otherwise just keep looping the team's own pot.
                var genericPool = PoolForTeam(GenericPackTeamName);
                bool wantGeneric = side == "home" ? _homeUseGenericNextRefill : _awayUseGenericNextRefill;
                pool = (wantGeneric && genericPool.Count > 0) ? genericPool : ownPool;

                bool usedGeneric = ReferenceEquals(pool, genericPool);
                if (side == "home") _homeUseGenericNextRefill = !usedGeneric;
                else _awayUseGenericNextRefill = !usedGeneric;
            }
        }

        // Fisher-Yates -- avoids repeating a track until the whole pool has played once.
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        foreach (var s in pool) queue.Enqueue(s);
    }

    public void Dispose() => Stop();
}
