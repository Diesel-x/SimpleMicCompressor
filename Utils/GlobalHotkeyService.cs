using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MicCompressor.Models;

namespace MicCompressor.Utils;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12; // Alt

    public const int HotkeyIdBypass = 101;
    public const int HotkeyIdMeme = 102;
    public const int HotkeyIdMute = 103;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private volatile AudioSettings? _settings;
    private readonly Action<int>? _onHotkeyTriggered;
    private readonly HashSet<int> _downKeys = new();

    public GlobalHotkeyService(Action<int> onHotkeyTriggered)
    {
        _onHotkeyTriggered = onHotkeyTriggered;
        _proc = HookCallback;
    }

    public void Start(AudioSettings settings)
    {
        _settings = settings;
        if (_hookId == IntPtr.Zero)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
            if (hMod == IntPtr.Zero)
            {
                hMod = GetModuleHandle(null);
            }
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
        }
    }

    public void UpdateHotkeys(AudioSettings settings)
    {
        _settings = settings;
        lock (_downKeys)
        {
            _downKeys.Clear();
        }
    }

    private static bool IsConfiguredHotkeyVk(AudioSettings settings, int vkCode)
    {
        uint uVk = (uint)vkCode;
        return (settings.HotkeyBypass.Enabled && settings.HotkeyBypass.GetVirtualKeyCode() == uVk)
            || (settings.HotkeyMeme.Enabled && settings.HotkeyMeme.GetVirtualKeyCode() == uVk)
            || (settings.HotkeyMute.Enabled && settings.HotkeyMute.GetVirtualKeyCode() == uVk);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var settings = _settings;
        if (nCode >= 0 && settings != null && settings.HotkeysEnabled)
        {
            int msg = wParam.ToInt32();
            int vkCode = Marshal.ReadInt32(lParam);

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                // Fast-path: ignore all keys that are not bound to any enabled hotkey (zero P/Invoke, zero lock)
                if (IsConfiguredHotkeyVk(settings, vkCode))
                {
                    bool isNewPress;
                    lock (_downKeys)
                    {
                        _downKeys.RemoveWhere(k => (GetAsyncKeyState(k) & 0x8000) == 0);
                        isNewPress = _downKeys.Add(vkCode);
                    }

                    if (isNewPress)
                    {
                        bool isCtrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                        bool isShift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                        bool isAlt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                        CheckAndTrigger(HotkeyIdBypass, settings.HotkeyBypass, vkCode, isCtrl, isShift, isAlt);
                        CheckAndTrigger(HotkeyIdMeme, settings.HotkeyMeme, vkCode, isCtrl, isShift, isAlt);
                        CheckAndTrigger(HotkeyIdMute, settings.HotkeyMute, vkCode, isCtrl, isShift, isAlt);
                    }
                }
            }
            else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && IsConfiguredHotkeyVk(settings, vkCode))
            {
                lock (_downKeys)
                {
                    _downKeys.Remove(vkCode);
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void CheckAndTrigger(int id, HotkeySetting hotkey, int vkCode, bool isCtrl, bool isShift, bool isAlt)
    {
        if (!hotkey.Enabled) return;
        uint targetVk = hotkey.GetVirtualKeyCode();
        if (targetVk == 0 || (uint)vkCode != targetVk) return;

        if (hotkey.Ctrl != isCtrl) return;
        if (hotkey.Shift != isShift) return;
        if (hotkey.Alt != isAlt) return;

        // Key matched! Trigger action on UI Dispatcher thread
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _onHotkeyTriggered?.Invoke(id);
        }));
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
