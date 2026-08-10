using System.Runtime.InteropServices;

namespace SupremeStadiumSoundSelector;

/// <summary>Sound Booth item #15: a subtle Xbox/PlayStation(XInput-compatible)-controller
/// vibration pulse during a close, late game -- final 2:00 of the 4th quarter or any point in
/// overtime, with the score within 7 points either way. This is a genuinely new capability
/// (haptics, not audio) added on top of the original Sound Booth audio spec, per an explicit
/// owner request. Windows-only (XInput); no-op anywhere the P/Invoke call fails (no controller
/// plugged in, non-Windows, etc.) -- haptics are a bonus, never something that should be able
/// to crash or block real game-event audio.
///
/// Note: this codebase's PlaySnapshot (Bandroom.Core) has no explicit "is this overtime" flag
/// -- Quarter is read straight off the scorebug's on-screen quarter text. Overtime is inferred
/// here as Quarter >= 5, the common convention for how these OCR quarter reads behave once
/// regulation ends; if CFB27's overtime scorebug ever reads differently, this is the one place
/// to correct that assumption.</summary>
internal static class ControllerRumbleService
{
    public static bool Enabled = false;

    const int CloseGameMarginPoints = 7;
    const int FinalTwoMinutesSeconds = 120;
    const int RumbleWindowMinQuarter = 4;
    const int OvertimeQuarter = 5;

    const float PulseIntervalSeconds = 4.0f;
    const int PulseDurationMs = 350;
    const ushort PulseStrength = 22000; // out of 65535 -- "subtle," not a jolt

    static bool _loopStarted;
    static DateTime _lastPulseUtc = DateTime.MinValue;

    /// <summary>Starts (once) a background poll of the live game snapshot. Safe to call
    /// repeatedly -- only the first call does anything.</summary>
    public static void Start(GameWatcher watcher)
    {
        if (_loopStarted) return;
        _loopStarted = true;

        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    if (Enabled)
                    {
                        var snap = watcher.LastSnapshot;
                        bool overtimeOrLateClose =
                            snap.Quarter >= OvertimeQuarter ||
                            (snap.Quarter >= RumbleWindowMinQuarter && snap.TimeRemainingSeconds <= FinalTwoMinutesSeconds);
                        bool closeScore = Math.Abs(snap.HomeScore - snap.AwayScore) <= CloseGameMarginPoints;

                        if (overtimeOrLateClose && closeScore &&
                            (DateTime.UtcNow - _lastPulseUtc).TotalSeconds >= PulseIntervalSeconds)
                        {
                            _lastPulseUtc = DateTime.UtcNow;
                            _ = PulseAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashLog.Write("ControllerRumbleService poll tick failed", ex);
                }
                await Task.Delay(500);
            }
        });
    }

    static async Task PulseAsync()
    {
        try
        {
            int pad = FindConnectedPad();
            if (pad < 0) return;
            SetVibration(pad, PulseStrength, PulseStrength);
            await Task.Delay(PulseDurationMs);
            SetVibration(pad, 0, 0);
        }
        catch (Exception ex)
        {
            CrashLog.Write("ControllerRumbleService pulse failed", ex);
        }
    }

    static int FindConnectedPad()
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                var state = new XINPUT_STATE();
                if (XInputGetState(i, ref state) == 0) return i; // ERROR_SUCCESS
            }
            catch (DllNotFoundException) { return -1; }
            catch (EntryPointNotFoundException) { return -1; }
        }
        return -1;
    }

    static void SetVibration(int pad, ushort leftMotor, ushort rightMotor)
    {
        var vibration = new XINPUT_VIBRATION { wLeftMotorSpeed = leftMotor, wRightMotorSpeed = rightMotor };
        try { XInputSetState(pad, ref vibration); }
        catch (DllNotFoundException) { /* no XInput on this system -- silently skip */ }
        catch (EntryPointNotFoundException) { /* older xinput without this export -- skip */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_VIBRATION
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    static extern int XInputSetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState);
}
