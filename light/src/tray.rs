#![allow(static_mut_refs)]

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use windows_sys::Win32::Foundation::*;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::*;
use windows_sys::Win32::UI::Shell::*;
use windows_sys::Win32::UI::WindowsAndMessaging::*;

const WM_TRAY_CALLBACK: u32 = WM_APP + 100;

const ID_TITLE: usize = 1000;
const ID_AUTHOR: usize = 1001;
const ID_TOGGLE_BYPASS: usize = 1002;
const ID_TOGGLE_MEME: usize = 1003;
const ID_TOGGLE_MUTE: usize = 1004;
const ID_OPEN_CONFIG: usize = 1005;
const ID_OPEN_GITHUB: usize = 1006;
const ID_EXIT: usize = 1007;

const HOTKEY_BYPASS_ID: i32 = 2001;
const HOTKEY_MEME_ID: i32 = 2002;
const HOTKEY_MUTE_ID: i32 = 2003;

#[allow(static_mut_refs)]
static mut GLOBAL_BYPASS: Option<Arc<AtomicBool>> = None;
#[allow(static_mut_refs)]
static mut GLOBAL_MEME: Option<Arc<AtomicBool>> = None;
#[allow(static_mut_refs)]
static mut GLOBAL_MUTE: Option<Arc<AtomicBool>> = None;
static mut GLOBAL_HWND: HWND = 0;
static mut GLOBAL_HOOK: HHOOK = 0;

unsafe extern "system" fn keyboard_hook_proc(n_code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if n_code >= 0 && (wparam as u32 == WM_KEYDOWN || wparam as u32 == WM_SYSKEYDOWN) {
        let kbd = *(lparam as *const KBDLLHOOKSTRUCT);
        let vk = kbd.vkCode;
        let is_ctrl = (GetAsyncKeyState(VK_CONTROL as i32) as u16 & 0x8000) != 0;
        let is_shift = (GetAsyncKeyState(VK_SHIFT as i32) as u16 & 0x8000) != 0;
        let is_alt = (GetAsyncKeyState(VK_MENU as i32) as u16 & 0x8000) != 0;

        if is_ctrl && is_shift && !is_alt {
            if vk == 'B' as u32 {
                if let Some(ref b) = GLOBAL_BYPASS {
                    let cur = b.load(Ordering::Relaxed);
                    b.store(!cur, Ordering::Relaxed);
                }
            } else if vk == 'M' as u32 {
                if let Some(ref m) = GLOBAL_MEME {
                    let cur = m.load(Ordering::Relaxed);
                    m.store(!cur, Ordering::Relaxed);
                }
            } else if vk == 'X' as u32 {
                if let Some(ref mute) = GLOBAL_MUTE {
                    let cur = mute.load(Ordering::Relaxed);
                    mute.store(!cur, Ordering::Relaxed);
                    if GLOBAL_HWND != 0 {
                        show_tray_notification(
                            GLOBAL_HWND,
                            "SimpleMicCompressor (Light)",
                            if !cur { "🔇 МИКРОФОН ЗАГЛУШЕН" } else { "🎙️ Микрофон активен" },
                        );
                    }
                }
            }
        }
    }
    CallNextHookEx(GLOBAL_HOOK, n_code, wparam, lparam)
}

fn to_wide(s: &str) -> Vec<u16> {
    use std::os::windows::ffi::OsStrExt;
    std::ffi::OsStr::new(s).encode_wide().chain(std::iter::once(0)).collect()
}

unsafe extern "system" fn wndproc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match msg {
        WM_TRAY_CALLBACK => {
            let event = lparam as u32;
            if event == WM_RBUTTONUP || event == WM_CONTEXTMENU {
                show_context_menu(hwnd);
            } else if event == WM_LBUTTONDBLCLK {
                open_config_file();
            }
            0
        }
        WM_COMMAND => {
            let cmd_id = (wparam & 0xFFFF) as usize;
            match cmd_id {
                ID_TOGGLE_BYPASS => {
                    if let Some(ref b) = GLOBAL_BYPASS {
                        let cur = b.load(Ordering::Relaxed);
                        b.store(!cur, Ordering::Relaxed);
                    }
                }
                ID_TOGGLE_MEME => {
                    if let Some(ref m) = GLOBAL_MEME {
                        let cur = m.load(Ordering::Relaxed);
                        m.store(!cur, Ordering::Relaxed);
                    }
                }
                ID_TOGGLE_MUTE => {
                    if let Some(ref mute) = GLOBAL_MUTE {
                        let cur = mute.load(Ordering::Relaxed);
                        mute.store(!cur, Ordering::Relaxed);
                    }
                }
                ID_OPEN_CONFIG => {
                    open_config_file();
                }
                ID_OPEN_GITHUB => {
                    open_github_page();
                }
                ID_EXIT => {
                    PostQuitMessage(0);
                }
                _ => {}
            }
            0
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            0
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

unsafe fn show_context_menu(hwnd: HWND) {
    let menu = CreatePopupMenu();
    if menu == 0 {
        return;
    }

    let title_w = to_wide("SimpleMicCompressor (Light) v1.3");
    AppendMenuW(menu, MF_STRING | MF_DISABLED, ID_TITLE, title_w.as_ptr());

    let author_w = to_wide("Author: Diesel-X");
    AppendMenuW(menu, MF_STRING | MF_DISABLED, ID_AUTHOR, author_w.as_ptr());

    AppendMenuW(menu, MF_SEPARATOR, 0, std::ptr::null());

    let is_bypass = GLOBAL_BYPASS.as_ref().map(|b| b.load(Ordering::Relaxed)).unwrap_or(false);
    let bypass_text = to_wide(if is_bypass { "[✓] Байпас обработки (Bypass: ON)" } else { "[   ] Байпас обработки (Bypass: OFF)" });
    AppendMenuW(menu, MF_STRING | if is_bypass { MF_CHECKED } else { MF_UNCHECKED }, ID_TOGGLE_BYPASS, bypass_text.as_ptr());

    let is_meme = GLOBAL_MEME.as_ref().map(|m| m.load(Ordering::Relaxed)).unwrap_or(false);
    let meme_text = to_wide(if is_meme { "[✓] Режим «Перегруз» (Meme Mode: ON)" } else { "[   ] Режим «Перегруз» (Meme Mode: OFF)" });
    AppendMenuW(menu, MF_STRING | if is_meme { MF_CHECKED } else { MF_UNCHECKED }, ID_TOGGLE_MEME, meme_text.as_ptr());

    let is_muted = GLOBAL_MUTE.as_ref().map(|m| m.load(Ordering::Relaxed)).unwrap_or(false);
    let mute_text = to_wide(if is_muted { "[✓] Заглушить микрофон (Muted)" } else { "[   ] Заглушить микрофон (Active)" });
    AppendMenuW(menu, MF_STRING | if is_muted { MF_CHECKED } else { MF_UNCHECKED }, ID_TOGGLE_MUTE, mute_text.as_ptr());

    AppendMenuW(menu, MF_SEPARATOR, 0, std::ptr::null());

    let config_text = to_wide("Открыть файл настроек (config.json)");
    AppendMenuW(menu, MF_STRING, ID_OPEN_CONFIG, config_text.as_ptr());

    let github_text = to_wide("GitHub: Diesel-x/SimpleMicCompressor");
    AppendMenuW(menu, MF_STRING, ID_OPEN_GITHUB, github_text.as_ptr());

    AppendMenuW(menu, MF_SEPARATOR, 0, std::ptr::null());

    let exit_text = to_wide("Выход (Exit)");
    AppendMenuW(menu, MF_STRING, ID_EXIT, exit_text.as_ptr());

    let mut pt = POINT { x: 0, y: 0 };
    GetCursorPos(&mut pt);
    SetForegroundWindow(hwnd);
    TrackPopupMenu(menu, TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd, std::ptr::null());
    DestroyMenu(menu);
}

fn open_config_file() {
    let p = crate::config::AppConfig::config_path();
    let _ = std::process::Command::new("notepad.exe")
        .arg(p)
        .spawn();
}

fn open_github_page() {
    let _ = std::process::Command::new("cmd")
        .args(["/c", "start", "https://github.com/Diesel-x/SimpleMicCompressor"])
        .spawn();
}

unsafe fn show_tray_notification(hwnd: HWND, title: &str, msg: &str) {
    let mut nid: NOTIFYICONDATAW = std::mem::zeroed();
    nid.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
    nid.hWnd = hwnd;
    nid.uID = 1;
    nid.uFlags = NIF_INFO;

    let title_w = to_wide(title);
    let msg_w = to_wide(msg);

    for (i, &c) in title_w.iter().enumerate().take(63) {
        nid.szInfoTitle[i] = c;
    }
    for (i, &c) in msg_w.iter().enumerate().take(255) {
        nid.szInfo[i] = c;
    }
    nid.dwInfoFlags = 0x00000001; // NIIF_INFO
    Shell_NotifyIconW(NIM_MODIFY, &nid);
}

pub fn run_tray_loop(
    bypass: Arc<AtomicBool>,
    meme_mode: Arc<AtomicBool>,
    is_muted: Arc<AtomicBool>,
    config: &crate::config::AppConfig,
) -> Result<(), String> {
    unsafe {
        GLOBAL_BYPASS = Some(bypass);
        GLOBAL_MEME = Some(meme_mode);
        GLOBAL_MUTE = Some(is_muted);

        let class_name = to_wide("SimpleMicCompressorLightClass");
        let hinstance = windows_sys::Win32::System::LibraryLoader::GetModuleHandleW(std::ptr::null());

        let icon = LoadIconW(0, IDI_APPLICATION);

        let wnd_class = WNDCLASSEXW {
            cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
            style: 0,
            lpfnWndProc: Some(wndproc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: hinstance,
            hIcon: icon,
            hCursor: 0,
            hbrBackground: 0,
            lpszMenuName: std::ptr::null(),
            lpszClassName: class_name.as_ptr(),
            hIconSm: icon,
        };

        RegisterClassExW(&wnd_class);

        let hwnd = CreateWindowExW(
            0,
            class_name.as_ptr(),
            to_wide("SimpleMicCompressor Light").as_ptr(),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            hinstance,
            std::ptr::null(),
        );

        if hwnd == 0 {
            return Err("Failed to create message window".to_string());
        }

        GLOBAL_HWND = hwnd;

        // Register low-level hook exclusively to avoid double-triggering with RegisterHotKey
        if config.hotkeys_enabled {
            GLOBAL_HOOK = SetWindowsHookExW(WH_KEYBOARD_LL, Some(keyboard_hook_proc), 0, 0);
        }

        // Add System Tray Icon
        let mut nid: NOTIFYICONDATAW = std::mem::zeroed();
        nid.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
        nid.hWnd = hwnd;
        nid.uID = 1;
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_INFO;
        nid.uCallbackMessage = WM_TRAY_CALLBACK;
        nid.hIcon = icon;

        let tip = to_wide("SimpleMicCompressor (Light) v1.3 by Diesel-X");
        for (i, &c) in tip.iter().enumerate().take(127) {
            nid.szTip[i] = c;
        }

        let balloon_title = to_wide("SimpleMicCompressor Light активен");
        for (i, &c) in balloon_title.iter().enumerate().take(63) {
            nid.szInfoTitle[i] = c;
        }

        let balloon_msg = to_wide("Обработка звука запущена в фоне (~3 МБ ОЗУ, 0% CPU).\nГорячие клавиши: Ctrl+Shift+B (Байпас), M (Мем), X (Мут).");
        for (i, &c) in balloon_msg.iter().enumerate().take(255) {
            nid.szInfo[i] = c;
        }
        nid.dwInfoFlags = 0x00000001; // NIIF_INFO

        Shell_NotifyIconW(NIM_ADD, &nid);

        // Message pump
        let mut msg: MSG = std::mem::zeroed();
        while GetMessageW(&mut msg, 0, 0, 0) > 0 {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        // Clean up
        if GLOBAL_HOOK != 0 {
            UnhookWindowsHookEx(GLOBAL_HOOK);
            GLOBAL_HOOK = 0;
        }
        UnregisterHotKey(hwnd, HOTKEY_BYPASS_ID);
        UnregisterHotKey(hwnd, HOTKEY_MEME_ID);
        UnregisterHotKey(hwnd, HOTKEY_MUTE_ID);
        Shell_NotifyIconW(NIM_DELETE, &nid);
        DestroyWindow(hwnd);

        Ok(())
    }
}
