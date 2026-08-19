namespace Bandroom.Core;

/// <summary>Numeric-only subset of PlaySnapshot the reader is trusted to supply (see
/// GameStateNormalizer's own doc comment for why event flags are excluded). GameWatcher overlays
/// these onto the PlaySnapshot it's already building from OCR for the tick, leaving every boolean
/// event flag (IsTouchdown/IsKickoff/etc) sourced from OCR exactly as before.
///
/// RELIABILITY FIX 2026-08-14: YardsToGo/YardLine/HomeScore/AwayScore are -1 when this specific
/// field has NEVER resolved from the reader this game (RAM's own per-field locator -- see
/// RamReaderValidator's provenance check -- can fail to lock SOME fields even while others, like
/// team names/clock, resolve fine; the Session 72 handoff doc's "awayTimeouts/homeTimeouts...
/// successfulReads:0" symptom is this same failure mode, just already handled correctly for
/// Timeouts via the same -1 sentinel below). Down/Quarter keep their existing "0 = never resolved"
/// convention (a real down/quarter is always >= 1, so 0 was already an unambiguous sentinel; no
/// change needed there). GameWatcher.RouteEngineTick MUST check these for &gt;= 0 (not truthiness)
/// before letting a reader value override the OCR-derived one -- see HavePossession below for why
/// a bool field needed its own separate flag instead of a sentinel.</summary>
public readonly record struct ReaderNumericSnapshot(
    int Down,
    int YardsToGo,
    int YardLine,
    int HomeScore,
    int AwayScore,
    int Quarter,
    int TimeRemainingSeconds,
    int AwayTimeoutsRemaining,
    int HomeTimeoutsRemaining,
    bool PossessionAway,
    // RELIABILITY FIX 2026-08-14: PossessionAway itself can't carry an "unresolved" sentinel the
    // way the int fields can (it's a plain bool, and "false" is indistinguishable from a real
    // home-has-the-ball read) -- so this used to silently default to false ("home") the instant
    // the RAM/screen reader connected at ALL, even on a game where possession specifically never
    // resolved (e.g. RAM locked team names/clock/score but never the possession bit). GameWatcher
    // used to trust that false unconditionally the moment ANY reader field connected, completely
    // discarding its own OCR-derived _lastPossession (color-sampled) for the rest of the game.
    // This flag lets GameWatcher tell "reader genuinely reports home" from "reader has never once
    // resolved possession" and fall back to OCR for the latter, same as every int field above.
    bool HavePossession,
    // 2026-08-14: PlayClock was being validated by RamReaderValidator and then just discarded --
    // PlaySnapshot.IsPlayClockCounting stayed 100% OCR-derived even with RAM connected. -1 sentinel,
    // same convention as every other int field above.
    int PlayClock,
    // 2026-08-14: carries the reader's own v1.4.9+ ram.freshness block through so GameWatcher can
    // replace its RAM-vs-OCR staleness guess with the reader's own ground truth (see
    // ScoreboardReaderFreshness.CoreBlockRecentlyChanged). Null on older readers or before anything
    // has resolved -- GameWatcher must treat null the same as "no freshness data," never as "stale."
    ScoreboardReaderFreshness? Freshness,
    // Passed straight through, NOT stickied like everything else in this record -- the reader
    // itself already time-windows this (null before/after the ~10s-to-45s announcement window),
    // so holding a stale "offense"/"defense" sticky would keep a penalty flag lit long after the
    // announcement ends. See ScoreboardReaderState.PenaltySide's doc comment.
    string? PenaltySide);

/// <summary>Maps Coffee's reader JSON (ScoreboardReaderState) into the numeric fields of a
/// PlaySnapshot -- most notably YardLine from `game.ballOn`, previously hardcoded to 0 in
/// GameWatcher.RouteEngineTick (dead code disabling every red-zone/field-position evaluator).
///
/// Mirrors GameWatcher's own "commit on confirmed value only" sticky-cache discipline (see
/// GameWatcher's _lastKnownAwayScore/_lastKnownDown/etc field comments): the reader's JSON file
/// is polled independently of its own write cadence, so a read can land mid-write or on a brief
/// blank/zeroed snapshot (e.g. between plays, or the instant a new game's file is first created).
/// A blank/null field must never read as "value dropped to 0/none" -- it must hold the last
/// confirmed value, exactly like a blank OCR tick does. Reset() must be called at the same
/// points GameWatcher.Start() resets its own sticky fields (new Start Watching / new game).</summary>
public sealed class GameStateNormalizer
{
    int _lastDown;
    // RELIABILITY FIX 2026-08-14: was 0-initialized, indistinguishable from "reader said 0" (a
    // real, common value -- 0-0 at kickoff, own goal line, goal-to-go distance). -1 can never be
    // a real value for any of these four (scores/yards/yard-line are all >= 0 by the >= 0 guards
    // below), so it's a safe "this field has never resolved" sentinel -- same convention already
    // used for _lastAwayTimeoutsRemaining/_lastHomeTimeoutsRemaining right below, just extended to
    // the fields that didn't have it. See ReaderNumericSnapshot's doc comment for the GameWatcher
    // bug this closes (score/yardage silently stomped to 0 when RAM connected but hadn't resolved
    // that specific field yet).
    int _lastYardsToGo = -1;
    int _lastYardLine = -1;
    int _lastHomeScore = -1;
    int _lastAwayScore = -1;
    int _lastQuarter;
    int _lastTimeRemainingSeconds;
    int _lastAwayTimeoutsRemaining = -1;
    int _lastHomeTimeoutsRemaining = -1;
    bool _lastPossessionAway;
    bool _havePossession;
    int _lastPlayClock = -1;

    public void Reset()
    {
        _lastDown = 0;
        _lastYardsToGo = -1;
        _lastYardLine = -1;
        _lastHomeScore = -1;
        _lastAwayScore = -1;
        _lastQuarter = 0;
        _lastTimeRemainingSeconds = 0;
        _lastAwayTimeoutsRemaining = -1;
        _lastHomeTimeoutsRemaining = -1;
        _lastPossessionAway = false;
        _havePossession = false;
        _lastPlayClock = -1;
    }

    /// <summary>Returns null only when nothing usable has ever been read yet (state is null, or
    /// meta.visible is explicitly false -- the reader itself saying "no scorebug on screen right
    /// now", which is a real "not connected to a game" signal, not a value to hold sticky over).
    /// Any other partial/null field falls back to the last confirmed value.</summary>
    public ReaderNumericSnapshot? Normalize(ScoreboardReaderState? state)
    {
        if (state == null) return null;
        if (state.Meta?.Visible == false) return null;

        var game = state.Game;
        var away = state.Away;
        var home = state.Home;

        if (game?.Down is int down && down is >= 1 and <= 4)
            _lastDown = down;

        // 2026-08-19: game.Distance (the raw numeric-or-"Goal"-text field) is documented as
        // "may be null during specials" -- goal-to-go is exactly such a special, so the raw
        // distance field itself can't be trusted to carry "Goal" text the way TryParseDistance
        // expects. game.DownDistance (the reader's own COMPOSED display string, e.g. "1st & Goal")
        // is the reliable source for this case -- checked as a fallback only when Distance itself
        // didn't resolve, so it never overrides a real numeric distance reading. Without this,
        // YardsToGo just went stale (held whatever it was before the goal-to-go snap) instead of
        // resolving to 0, which is why 1st/2nd/3rd-down-on-goal-to-go plays weren't classified
        // correctly by the down helpers that key off YardsToGo.
        if (TryParseDistance(game?.Distance, out int distance))
            _lastYardsToGo = distance;
        else if (!string.IsNullOrEmpty(game?.DownDistance) && game.DownDistance.Contains("goal", StringComparison.OrdinalIgnoreCase))
            _lastYardsToGo = 0;

        if (game?.BallOn is int ballOn && ballOn is >= 0 and <= 100)
            _lastYardLine = ballOn;

        if (away?.Score is int awayScore && awayScore >= 0)
            _lastAwayScore = awayScore;
        if (home?.Score is int homeScore && homeScore >= 0)
            _lastHomeScore = homeScore;

        if (TryParseQuarter(game?.Quarter, out int quarter))
            _lastQuarter = quarter;

        if (TryParseClock(game?.Clock, out int seconds))
            _lastTimeRemainingSeconds = seconds;

        if (away?.Timeouts is int awayTo && awayTo >= 0)
            _lastAwayTimeoutsRemaining = awayTo;
        if (home?.Timeouts is int homeTo && homeTo >= 0)
            _lastHomeTimeoutsRemaining = homeTo;

        if (game?.PlayClock is int playClock && playClock >= 0)
            _lastPlayClock = playClock;

        // "none" (no possession read yet, e.g. pregame) intentionally does NOT update the sticky
        // value -- same "don't let a blank/unknown read fabricate a delta" rule as everything else
        // here. Team-level `possession` bools are a fallback for when `game.possession` itself is
        // missing/unrecognized but a team object still reports it directly.
        if (game?.Possession == "away") { _lastPossessionAway = true; _havePossession = true; }
        else if (game?.Possession == "home") { _lastPossessionAway = false; _havePossession = true; }
        else if (away?.Possession == true) { _lastPossessionAway = true; _havePossession = true; }
        else if (home?.Possession == true) { _lastPossessionAway = false; _havePossession = true; }

        return new ReaderNumericSnapshot(
            Down: _lastDown,
            YardsToGo: _lastYardsToGo,
            YardLine: _lastYardLine,
            HomeScore: _lastHomeScore,
            AwayScore: _lastAwayScore,
            Quarter: _lastQuarter,
            TimeRemainingSeconds: _lastTimeRemainingSeconds,
            AwayTimeoutsRemaining: _lastAwayTimeoutsRemaining,
            HomeTimeoutsRemaining: _lastHomeTimeoutsRemaining,
            PossessionAway: _havePossession && _lastPossessionAway,
            HavePossession: _havePossession,
            PlayClock: _lastPlayClock,
            Freshness: state.Freshness,
            PenaltySide: state.PenaltySide);
    }

    /// <summary>Reader sends distance either as a plain number ("7") or goal-line text
    /// ("Goal"/"goal-to-go") -- goal-to-go has no fixed yards-to-go number of its own, it's
    /// implicitly however many yards remain to the end zone, which YardLine already conveys, so
    /// it normalizes to 0 rather than a guess.</summary>
    public static bool TryParseDistance(string? raw, out int distance)
    {
        distance = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (int.TryParse(raw, out distance)) return true;
        if (raw.Contains("goal", StringComparison.OrdinalIgnoreCase)) { distance = 0; return true; }
        return false;
    }

    /// <summary>Accepts either a bare number ("1") or ordinal-ish text ("1st"/"Q1") -- reader and
    /// OCR paths have historically differed on this, so both are tolerated here.</summary>
    public static bool TryParseQuarter(string? raw, out int quarter)
    {
        quarter = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string digits = new(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out quarter) && quarter > 0;
    }

    public static bool TryParseClock(string? raw, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int minutes) || !int.TryParse(parts[1], out int secs)) return false;
        seconds = minutes * 60 + secs;
        return true;
    }
}
