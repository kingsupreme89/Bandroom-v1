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

    /// <summary>True while the pregame tunnel-walk chevron marker is on screen -- the pair of
    /// white chevron shapes flanking the center bowl/rivalry badge in the pregame scorebug, e.g.
    /// the Rose Bowl badge screenshot that calibrated ScorebugPreset.CollegeFootball27's
    /// ChevronMarkerFx* crop. Owner-confirmed present on every game's pregame scorebug, not just
    /// bowl games, so it's a team-neutral, matchup-neutral signal -- unlike IsPregameReady's panel
    /// colors, this needed no color-independence caveat since it's read by brightness only (see
    /// GameWatcher.SamplePregameEntranceFromWindow), same technique as the possession underline.
    /// This fires slightly BEFORE GameStateEventHelper's existing quarter/down heuristic can (the
    /// walkout happens before the first snap makes Down/Quarter readable), so
    /// GameStateEventHelper's "Other: Pregame Take the Field" block ORs this in as an earlier,
    /// more direct alternative to that heuristic rather than being a separate event/evaluator --
    /// see that class's _didFirePregame guard, which covers both signals with one fire-once flag.</summary>
    public bool IsPregameEntranceMarker { get; init; }

    /// <summary>True while CFB27's "EA SPORTS COLLEGE FOOTBALL 27" flag/title card is on screen --
    /// the black waving-flag graphic shown at the very start of the pregame broadcast sequence,
    /// before the chevron tunnel-walk (IsPregameEntranceMarker) and before the READY screen
    /// (IsPregameReady). Team-neutral by construction: the flag graphic and its text are
    /// identical for every matchup, no per-team colors involved, so (unlike IsPregameReady) this
    /// needs no color-independence caveat -- see the "teamrunout" WatchedRegion in GameWatcher.cs
    /// and RunOutHelper.cs, which fires "Other: Team Run Out" off this flag's edge, distinct from
    /// and NOT a replacement for the chevron -- the chevron stays dedicated to "Other: Pregame
    /// Take the Field".</summary>
    public bool IsTeamRunOut { get; init; }

    /// <summary>True while the play clock box shows a counting number (pre-snap); false while it
    /// shows literal "--" (from the snap through the live play and any dead-ball overlay, until
    /// the ribbon is ready for the next snap). See the "playclock" WatchedRegion in GameWatcher.cs
    /// for the calibration and FirstDownOnFirstDownHelper.cs for why this edge exists: it's the
    /// only OCR-noise-resistant way to know "a real play just happened" when Down/YardsToGo alone
    /// can't distinguish a first-down-on-first-down (Down never changes) from ordinary mid-drive
    /// stillness.</summary>
    public bool IsPlayClockCounting { get; init; }
}
