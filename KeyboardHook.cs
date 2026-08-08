using System.Runtime.InteropServices;

namespace SupremeStadiumSoundSelector;

internal sealed class KeyboardHook
{
    public event Action<string>? KeyCombo;

    IntPtr _hookHandle = IntPtr.Zero;
    Native.LowLevelKeyboardProc? _proc;
    Thread? _thread;
    uint _threadId;
    readonly HashSet<uint> _pressed = new();

    public void Start()
    {
        _thread = new Thread(() =>
        {
            _threadId = Native.GetCurrentThreadId();
            _proc = HookCallback;
            _hookHandle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);

            while (Native.GetMessage(out Native.MSG msg, IntPtr.Zero, 0, 0) != 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }

            if (_hookHandle != IntPtr.Zero)
                Native.UnhookWindowsHookEx(_hookHandle);
        });
        _thread.IsBackground = true;
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        if (_threadId != 0)
            Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            uint vk = data.vkCode;

            bool isDown = wParam == (IntPtr)Native.WM_KEYDOWN || wParam == (IntPtr)Native.WM_SYSKEYDOWN;
            bool isUp = wParam == (IntPtr)Native.WM_KEYUP || wParam == (IntPtr)Native.WM_SYSKEYUP;

            if (vk >= Native.VK_NUMPAD0 && vk <= Native.VK_NUMPAD9)
            {
                if (isDown && _pressed.Add(vk))
                {
                    int digit = (int)(vk - Native.VK_NUMPAD0);
                    string modifiers = "";
                    if ((Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0) modifiers += "Ctrl+";
                    if ((Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0) modifiers += "Shift+";
                    if ((Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0) modifiers += "Alt+";
                    KeyCombo?.Invoke($"{modifiers}Numpad{digit}");
                }
                else if (isUp)
                {
                    _pressed.Remove(vk);
                }
            }
        }
        return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
