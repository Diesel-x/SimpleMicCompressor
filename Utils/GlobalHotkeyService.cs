using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MicCompressor.Models;

namespace MicCompressor.Utils;

public sealed class GlobalHotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int HotkeyIdBypass = 101;
    public const int HotkeyIdMeme = 102;
    public const int HotkeyIdMute = 103;

    private IntPtr _hWnd = IntPtr.Zero;
    private readonly List<int> _registeredIds = new();

    public void Initialize(IntPtr hWnd)
    {
        _hWnd = hWnd;
    }

    public void UpdateHotkeys(AudioSettings settings)
    {
        UnregisterAll();

        if (_hWnd == IntPtr.Zero || !settings.HotkeysEnabled) return;

        if (settings.HotkeyBypass.Enabled)
        {
            RegisterSingle(HotkeyIdBypass, settings.HotkeyBypass);
        }

        if (settings.HotkeyMeme.Enabled)
        {
            RegisterSingle(HotkeyIdMeme, settings.HotkeyMeme);
        }

        if (settings.HotkeyMute.Enabled)
        {
            RegisterSingle(HotkeyIdMute, settings.HotkeyMute);
        }
    }

    private bool RegisterSingle(int id, HotkeySetting hotkey)
    {
        uint vk = hotkey.GetVirtualKeyCode();
        uint mods = hotkey.GetModifiers();
        if (vk == 0) return false;

        bool success = RegisterHotKey(_hWnd, id, mods, vk);
        if (success)
        {
            _registeredIds.Add(id);
        }
        return success;
    }

    public void UnregisterAll()
    {
        if (_hWnd == IntPtr.Zero) return;
        foreach (int id in _registeredIds)
        {
            UnregisterHotKey(_hWnd, id);
        }
        _registeredIds.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
    }
}
