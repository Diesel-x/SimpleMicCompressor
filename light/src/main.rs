#![windows_subsystem = "windows"]

mod audio;
mod config;
mod dsp;
mod tray;

use config::AppConfig;

fn main() {
    // Single-instance check
    unsafe {
        use std::os::windows::ffi::OsStrExt;
        use windows_sys::Win32::Foundation::{GetLastError, ERROR_ALREADY_EXISTS};

        extern "system" {
            fn CreateMutexW(
                lpMutexAttributes: *const std::ffi::c_void,
                bInitialOwner: i32,
                lpName: *const u16,
            ) -> windows_sys::Win32::Foundation::HANDLE;
        }

        let mutex_name: Vec<u16> = std::ffi::OsStr::new("SimpleMicCompressor_SingleInstance_Mutex_2026")
            .encode_wide()
            .chain(std::iter::once(0))
            .collect();

        let _mutex = CreateMutexW(std::ptr::null(), 1, mutex_name.as_ptr());
        if GetLastError() == ERROR_ALREADY_EXISTS {
            return;
        }
    }

    let cfg = AppConfig::load();

    // Start audio engine
    let engine = match audio::start_audio_engine(&cfg) {
        Ok(e) => e,
        Err(err) => {
            unsafe {
                use std::os::windows::ffi::OsStrExt;
                use windows_sys::Win32::UI::WindowsAndMessaging::*;

                let title: Vec<u16> = std::ffi::OsStr::new("SimpleMicCompressor (Light) - Ошибка запуска")
                    .encode_wide()
                    .chain(std::iter::once(0))
                    .collect();
                let msg: Vec<u16> = std::ffi::OsStr::new(&format!(
                    "Не удалось инициализировать аудиоустройства:\n{}\n\nУбедитесь, что микрофон подключен и установлен SMC virtual cable (VB-Audio Virtual Cable).",
                    err
                ))
                .encode_wide()
                .chain(std::iter::once(0))
                .collect();
                MessageBoxW(0, msg.as_ptr(), title.as_ptr(), MB_OK | MB_ICONERROR);
            }
            return;
        }
    };

    // Run tray icon and global hotkeys event loop (blocks until exit)
    let _ = tray::run_tray_loop(
        engine.controls.bypass.clone(),
        engine.controls.meme_mode.clone(),
        engine.controls.is_muted.clone(),
        &cfg,
    );
}
