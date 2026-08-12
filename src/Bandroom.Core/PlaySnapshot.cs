namespace Bandroom.Core;

public sealed class PlaySnapshot
{
    public int Down { get; init; }
    public int YardsToGo { get; init; }
    public int YardLine { get; init; }
    public int HomeScore { get; init; }
    public int AwayScore { get; init; }
    public int Quarter { get; init; }
    public int TimeRemainingSeconds { get; init; }
    public int AwayTimeoutsRemaining { get; init; }
    public int HomeTimeoutsRemaining { get; init; }
    public bool BigGame { get; init; }
    public bool PossessionAway { get; init; }
    public bool IsKickoff { get; init; }
    public bool IsPAT { get; init; }
    public bool IsPenaltyOnOffense { get; init; }
    public bool IsPenaltyOnDefense { get; init; }
    public bool IsTouchdown { get; init; }
    public bool IsTurnover { get; init; }
    public bool IsNoPuntReturn { get; init; }

    /// <summary>True while the scorebug's own "Time Out" text (GameWatcher's "situation" region,
    /// same slot that cycles through KICKOFF/PAT GOOD/TOUCHDOWN/etc) is on screen. Added 2026-08-11
    /// (owner report: a real timeout with plenty of time left never fired any cue) -- TimeoutHelper
    /// used to gate purely on TimeRemainingSeconds &lt;= 240 regardless of whether a timeout was
    /// actually visible, which silently ate every timeout called outside the 2-minute-drill window.
    /// Now the actual visual signal, not a clock heuristic.</summary>
    public bool IsTimeout { get; init; }

    /// <summary>True while the "FIELD GOAL" full-screen banner (GameWatcher's "banner" region)
    /// is on screen -- fires for BOTH made and missed attempts (the banner text itself doesn't
    /// distinguish the two). FieldGoalMissedHelper uses the true->false transition of this flag
    /// combined with no score change to detect a miss, since IsPAT never gets set for an attempt
    /// that fails (see STATE_MACHINE_ANALYSIS.md Discrepancy #6 -- the OCR situation region only
    /// ever maps success text like "PAT GOOD").</summary>
    public bool IsFieldGoalAttempt { get; init; }

    /// <summary>True while CFB27's pregame team-intro/"READY" screen is on screen (the screen
    /// shown right before kickoff where both teams' ratings badges and a center READY prompt
    /// are displayed). Detection must be team-color-independent -- see PregameHelper.cs and the
    /// "pregameready" WatchedRegion in GameWatcher.cs for why this can never be color-matched
    /// (that screen's panel colors are per-matchup team colors, e.g. red/blue for Ohio State/
    /// Michigan vs different colors for any other pairing).</summary>
    public bool IsPregameReady { get; init; }
}
