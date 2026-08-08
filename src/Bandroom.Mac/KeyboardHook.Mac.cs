using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Bandroom.Mac;

/// <summary>
/// macOS global keyboard hook using Carbon Event Manager.
/// Requires Accessibility permission (System Preferences > Privacy > Accessibility).
/// Mirrors the Windows KeyboardHook.cs API (KeyCombo event).
/// </summary>
internal sealed class KeyboardHook
{
    public event Action<string>? KeyCombo;

    private static readonly Dictionary<int, string> NumPadMap = new()
    {
        [82] = "numpad0", [83] = "numpad1", [84] = "numpad2", [85] = "numpad3",
        [86] = "numpad4", [87] = "numpad5", [88] = "numpad6", [89] = "numpad7",
        [91] = "numpad8", [92] = "numpad9",
    };

    // Carbon/CGEvent interop
    private const string CarbonLib = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [DllImport(CarbonLib)]
    private static extern IntPtr CGEventSourceCreate(int sourceStateID);

    [DllImport(CarbonLib)]
    private static extern IntPtr CGEventTapCreate(
        int tapPoint, int place, int options, ulong eventsOfInterest,
        CGEventTapCallback callback, IntPtr userInfo);

    [DllImport(CarbonLib)]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CarbonLib)]
    private static extern void CFRunLoopAddSource(
        IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CarbonLib)]
    private static extern void CFRunLoopRun();

    [DllImport(CarbonLib)]
    private static extern void CGEventTapEnable(IntPtr tap, bool enable);

    [DllImport(CarbonLib)]
    private static extern long CGEventGetIntegerValueField(IntPtr eventRef, int field);

    private delegate IntPtr CGEventTapCallback(
        IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo);

    private const int kCGEventKeyDown = 10;
    private const int kCGKeyboardEventKeycode = 9;
    private const int kCGSessionEventTap = 0;
    private const int kCGHeadInsertEventTap = 0;
    private const int kCGEventTapOptionListenOnly = 1;
    private const ulong kCGEventMaskForKeyDown = 1UL << kCGEventKeyDown;

    private IntPtr _eventTap;
    private bool _running;

    public void Start()
    {
        if (_running) return;
        _running = true;

        // Run on background thread so the run loop doesn't block
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using var source = new CGEventSource(kCGSessionEventTap);
                _eventTap = CGEventTapCreate(
                    kCGSessionEventTap, kCGHeadInsertEventTap,
                    kCGEventTapOptionListenOnly,
                    kCGEventMaskForKeyDown,
                    OnKeyEvent, IntPtr.Zero);

                if (_eventTap == IntPtr.Zero)
                {
                    Console.Error.WriteLine("[KeyboardHook.Mac] Event tap creation failed. Does the app have Accessibility permission?");
                    return;
                }

                var runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
                CFRunLoopAddSource(CFRunLoopGetCurrent(), runLoopSource, kCFRunLoopCommonModes());
                CGEventTapEnable(_eventTap, true);

                CFRunLoopRun();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[KeyboardHook.Mac] Error: {ex.Message}");
            }
        });
    }

    private IntPtr OnKeyEvent(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo)
    {
        if (type == kCGEventKeyDown)
        {
            long keyCode = CGEventGetIntegerValueField(eventRef, kCGKeyboardEventKeycode);
            if (NumPadMap.TryGetValue((int)keyCode, out var combo))
            {
                KeyCombo?.Invoke(combo);
            }
        }
        return eventRef;
    }

    public void Stop()
    {
        _running = false;
        if (_eventTap != IntPtr.Zero)
        {
            CGEventTapEnable(_eventTap, false);
        }
    }

    // Additional Carbon interop needed for event tap run loop source
    [DllImport(CarbonLib)]
    private static extern IntPtr CFMachPortCreateRunLoopSource(
        IntPtr allocator, IntPtr port, int order);

    private static IntPtr kCFRunLoopCommonModes() => 
        CFStringGetCStringPtr(CFSTR("kCFRunLoopCommonModes"), 0);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFSTR(string s);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringGetCStringPtr(IntPtr theString, int encoding);

    // CGEventSourceCreate wrapper class
    private sealed class CGEventSource : IDisposable
    {
        private readonly IntPtr _handle;
        public CGEventSource(int stateID) => _handle = CGEventSourceCreate(stateID);
        public void Dispose() { if (_handle != IntPtr.Zero) CFRelease(_handle); }
    }

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}