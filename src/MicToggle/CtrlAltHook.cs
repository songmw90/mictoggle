using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MicToggle;

internal sealed class CtrlAltHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _proc;
    private readonly Func<IntPtr, int, IntPtr, IntPtr, IntPtr> _callNextHook;
    private readonly HashSet<Keys> _pressedKeys = [];
    private IntPtr _hookId;
    private bool _chordActive;

    public CtrlAltHook() : this(CallNextHookEx)
    {
    }

    internal CtrlAltHook(Func<IntPtr, int, IntPtr, IntPtr, IntPtr> callNextHook)
    {
        _callNextHook = callNextHook ?? throw new ArgumentNullException(nameof(callNextHook));
        _proc = HookCallback;
    }

    public event EventHandler? Pressed;
    public event EventHandler? Released;

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        _hookId = SetHook(_proc);
        if (_hookId == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install keyboard hook.");
        }
    }

    public void Dispose()
    {
        if (_hookId == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        return SetWindowsHookEx(
            WH_KEYBOARD_LL,
            proc,
            GetModuleHandle(currentModule?.ModuleName),
            0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var key = (Keys)Marshal.ReadInt32(lParam);
            if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
            {
                UpdateKeyState(key, true);
            }
            else if (message == WM_KEYUP || message == WM_SYSKEYUP)
            {
                UpdateKeyState(key, false);
            }
        }

        return _callNextHook(_hookId, nCode, wParam, lParam);
    }

    private void UpdateKeyState(Keys key, bool isDown)
    {
        if (isDown)
        {
            _pressedKeys.Add(key);
        }
        else
        {
            _pressedKeys.Remove(key);
        }

        var chordPressed =
            (_pressedKeys.Contains(Keys.LControlKey) || _pressedKeys.Contains(Keys.ControlKey)) &&
            (_pressedKeys.Contains(Keys.LMenu) || _pressedKeys.Contains(Keys.RMenu) || _pressedKeys.Contains(Keys.Menu));

        if (chordPressed && !_chordActive)
        {
            _chordActive = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (!chordPressed && _chordActive)
        {
            _chordActive = false;
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
