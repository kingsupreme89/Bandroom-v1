namespace SupremeStadiumSoundSelector;

/// <summary>Sidechain "ducking" gain calculator -- turned into a real, wired-in effect for
/// the Sound Booth (previously a self-contained state machine that was never instantiated
/// anywhere and never actually touched output volume). Not tied to any specific player:
/// every currently-playing clip's own fade-poll loop in AudioPlayer.Play() reads
/// GetGainMultiplier() each tick and multiplies it into that clip's own volume, so no clip
/// needs a reference to any other clip. OnHighPriorityEventFired() is called for
/// Touchdown/Turnover/Safety-class events; the shared gain dips to DuckLevel with a fast
/// (~20ms) attack and eases back to 1.0 with a slower (~300ms) release once the duck
/// window elapses.</summary>
internal static class AudioDuckingController
{
    public static bool Enabled = false;
    const float DuckLevel = 0.4f;
    const float DuckWindowSeconds = 2.0f;
    const float AttackPerSecond = 1f / 0.02f;   // reach target in ~20ms
    const float ReleasePerSecond = 1f / 0.30f;  // ease back over ~300ms

    static volatile float _current = 1f;
    static DateTime _duckUntilUtc = DateTime.MinValue;
    static DateTime _lastTick = DateTime.UtcNow;
    static readonly object _lock = new();
    static bool _loopStarted;

    /// <summary>Call when a high-priority event (Touchdown/Turnover/Safety) fires. Ducks
    /// every other currently-playing clip down to 40% for ~2 seconds, then eases back.
    /// Extends (never shortens) a single shared deadline rather than each call scheduling its
    /// own independent "un-duck after 2s" timer -- with independent timers, an earlier event's
    /// timer could fire and reset the gain to 1.0 while a LATER, overlapping event's 2-second
    /// window was still supposed to be active (e.g. Touchdown at t=0, Turnover at t=1: the first
    /// timer firing at t=2 would prematurely end the duck a full second early). EnsureLoop's own
    /// persistent tick is what decides attack-vs-release each frame by comparing to this deadline,
    /// so there's only ever one source of truth for "should we still be ducked."</summary>
    public static void OnHighPriorityEventFired()
    {
        if (!Enabled) return;
        lock (_lock)
        {
            var newDeadline = DateTime.UtcNow.AddSeconds(DuckWindowSeconds);
            if (newDeadline > _duckUntilUtc) _duckUntilUtc = newDeadline;
        }
        EnsureLoop();
    }

    static void EnsureLoop()
    {
        lock (_lock)
        {
            if (_loopStarted) return;
            _loopStarted = true;
        }
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    float dt = (float)(now - _lastTick).TotalSeconds;
                    _lastTick = now;
                    float target = now < _duckUntilUtc ? DuckLevel : 1f;
                    float rate = target < _current ? AttackPerSecond : ReleasePerSecond;
                    float step = rate * dt;
                    _current = target < _current
                        ? Math.Max(target, _current - step)
                        : Math.Min(target, _current + step);
                }
                catch (Exception ex)
                {
                    // This loop runs for the entire process lifetime once started -- an unhandled
                    // exception here would silently kill it (unobserved task fault) and leave
                    // _current frozen wherever it was, potentially stuck mid-duck (e.g. every clip
                    // permanently capped at 40% volume) for the rest of the session with no
                    // recovery. Log and keep ticking rather than let one bad tick end the loop.
                    CrashLog.Write("AudioDuckingController loop tick failed", ex);
                }
                await Task.Delay(15);
            }
        });
    }

    /// <summary>Current shared duck gain (1.0 = no ducking). Multiply into any clip's own
    /// volume each poll tick -- returns 1.0 unconditionally when ducking is disabled.</summary>
    public static float GetGainMultiplier() => Enabled ? _current : 1f;
}
