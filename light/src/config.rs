use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct HotkeyConfig {
    pub enabled: bool,
    pub ctrl: bool,
    pub shift: bool,
    pub alt: bool,
    pub key: String,
}

impl Default for HotkeyConfig {
    fn default() -> Self {
        Self {
            enabled: true,
            ctrl: true,
            shift: true,
            alt: false,
            key: "B".to_string(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct AppConfig {
    #[serde(default)]
    pub input_device_id: Option<String>,
    #[serde(default)]
    pub input_device_name: Option<String>,
    #[serde(default)]
    pub output_device_id: Option<String>,
    #[serde(default)]
    pub output_device_name: Option<String>,

    #[serde(default = "default_true")]
    pub high_pass_enabled: bool,
    #[serde(default = "default_high_pass_cutoff")]
    pub high_pass_cutoff_hz: f32,

    #[serde(default = "default_true")]
    pub gate_enabled: bool,
    #[serde(default = "default_gate_threshold")]
    pub gate_threshold_db: f32,
    #[serde(default = "default_gate_attack")]
    pub gate_attack_ms: f32,
    #[serde(default = "default_gate_release")]
    pub gate_release_ms: f32,
    #[serde(default = "default_gate_hold")]
    pub gate_hold_ms: f32,

    #[serde(default = "default_true")]
    pub compressor_enabled: bool,
    #[serde(default = "default_comp_threshold")]
    pub compressor_threshold_db: f32,
    #[serde(default = "default_comp_ratio")]
    pub compressor_ratio: f32,
    #[serde(default = "default_comp_attack")]
    pub compressor_attack_ms: f32,
    #[serde(default = "default_comp_release")]
    pub compressor_release_ms: f32,
    #[serde(default = "default_comp_makeup")]
    pub compressor_makeup_gain_db: f32,
    #[serde(default = "default_comp_knee")]
    pub compressor_knee_db: f32,

    #[serde(default = "default_true")]
    pub limiter_enabled: bool,
    #[serde(default = "default_limiter_ceiling")]
    pub limiter_ceiling_db: f32,
    #[serde(default = "default_limiter_release")]
    pub limiter_release_ms: f32,

    #[serde(default)]
    pub bypass_all: bool,
    #[serde(default)]
    pub destroyed_mic_meme_mode: bool,
    #[serde(default)]
    pub is_muted: bool,

    #[serde(default = "default_true")]
    pub hotkeys_enabled: bool,
    #[serde(default = "default_hotkey_bypass")]
    pub hotkey_bypass: HotkeyConfig,
    #[serde(default = "default_hotkey_meme")]
    pub hotkey_meme: HotkeyConfig,
    #[serde(default = "default_hotkey_mute")]
    pub hotkey_mute: HotkeyConfig,

    #[serde(default = "default_language")]
    pub language: Option<String>,
}

fn default_true() -> bool { true }
fn default_high_pass_cutoff() -> f32 { 80.0 }
fn default_gate_threshold() -> f32 { -42.0 }
fn default_gate_attack() -> f32 { 4.0 }
fn default_gate_release() -> f32 { 120.0 }
fn default_gate_hold() -> f32 { 60.0 }
fn default_comp_threshold() -> f32 { -20.0 }
fn default_comp_ratio() -> f32 { 4.0 }
fn default_comp_attack() -> f32 { 10.0 }
fn default_comp_release() -> f32 { 150.0 }
fn default_comp_makeup() -> f32 { 5.0 }
fn default_comp_knee() -> f32 { 4.0 }
fn default_limiter_ceiling() -> f32 { -1.0 }
fn default_limiter_release() -> f32 { 40.0 }
fn default_language() -> Option<String> { Some("ru".to_string()) }

fn default_hotkey_bypass() -> HotkeyConfig {
    HotkeyConfig { enabled: true, ctrl: true, shift: true, alt: false, key: "B".to_string() }
}
fn default_hotkey_meme() -> HotkeyConfig {
    HotkeyConfig { enabled: true, ctrl: true, shift: true, alt: false, key: "M".to_string() }
}
fn default_hotkey_mute() -> HotkeyConfig {
    HotkeyConfig { enabled: true, ctrl: true, shift: true, alt: false, key: "X".to_string() }
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            input_device_id: None,
            input_device_name: None,
            output_device_id: None,
            output_device_name: None,
            high_pass_enabled: default_true(),
            high_pass_cutoff_hz: default_high_pass_cutoff(),
            gate_enabled: default_true(),
            gate_threshold_db: default_gate_threshold(),
            gate_attack_ms: default_gate_attack(),
            gate_release_ms: default_gate_release(),
            gate_hold_ms: default_gate_hold(),
            compressor_enabled: default_true(),
            compressor_threshold_db: default_comp_threshold(),
            compressor_ratio: default_comp_ratio(),
            compressor_attack_ms: default_comp_attack(),
            compressor_release_ms: default_comp_release(),
            compressor_makeup_gain_db: default_comp_makeup(),
            compressor_knee_db: default_comp_knee(),
            limiter_enabled: default_true(),
            limiter_ceiling_db: default_limiter_ceiling(),
            limiter_release_ms: default_limiter_release(),
            bypass_all: false,
            destroyed_mic_meme_mode: false,
            is_muted: false,
            hotkeys_enabled: true,
            hotkey_bypass: default_hotkey_bypass(),
            hotkey_meme: default_hotkey_meme(),
            hotkey_mute: default_hotkey_mute(),
            language: default_language(),
        }
    }
}

impl AppConfig {
    pub fn config_path() -> PathBuf {
        // 1. Check shared %APPDATA%\SimpleMicCompressor\config.json
        if let Ok(appdata) = std::env::var("APPDATA") {
            let appdata_p = PathBuf::from(&appdata).join("SimpleMicCompressor").join("config.json");
            if appdata_p.exists() {
                return appdata_p;
            }
        }

        // 2. Check local mic_compressor_config.json next to exe
        if let Ok(exe_p) = std::env::current_exe() {
            if let Some(parent) = exe_p.parent() {
                let p = parent.join("mic_compressor_config.json");
                if p.exists() {
                    return p;
                }
            }
        }

        // 3. Check current directory
        let local_p = PathBuf::from("mic_compressor_config.json");
        if local_p.exists() {
            return local_p;
        }

        // Default: prefer %APPDATA%\SimpleMicCompressor\config.json
        if let Ok(appdata) = std::env::var("APPDATA") {
            let appdata_dir = PathBuf::from(appdata).join("SimpleMicCompressor");
            let _ = fs::create_dir_all(&appdata_dir);
            return appdata_dir.join("config.json");
        }

        local_p
    }

    pub fn load() -> Self {
        let path = Self::config_path();
        if path.exists() {
            if let Ok(content) = fs::read_to_string(&path) {
                if let Ok(cfg) = serde_json::from_str::<AppConfig>(&content) {
                    return cfg;
                }
            }
        }
        let default_cfg = Self::default();
        let _ = default_cfg.save();
        default_cfg
    }

    pub fn save(&self) -> std::io::Result<()> {
        let path = Self::config_path();
        if let Some(parent) = path.parent() {
            let _ = fs::create_dir_all(parent);
        }
        let json = serde_json::to_string_pretty(self).unwrap_or_default();
        fs::write(path, json)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_match_documented_values() {
        let cfg = AppConfig::default();
        assert!(cfg.high_pass_enabled);
        assert_eq!(cfg.high_pass_cutoff_hz, 80.0);
        assert_eq!(cfg.gate_threshold_db, -42.0);
        assert_eq!(cfg.gate_hold_ms, 60.0);
        assert_eq!(cfg.compressor_ratio, 4.0);
        assert_eq!(cfg.compressor_knee_db, 4.0);
        assert_eq!(cfg.limiter_ceiling_db, -1.0);
        assert!(cfg.hotkeys_enabled);
    }

    #[test]
    fn parses_pascal_case_schema_from_development_md() {
        let json = r#"{
          "InputDeviceId": null,
          "OutputDeviceId": null,
          "HighPassEnabled": true,
          "HighPassCutoffHz": 80.0,
          "GateEnabled": true,
          "GateThresholdDb": -42.0,
          "GateAttackMs": 4.0,
          "GateReleaseMs": 120.0,
          "GateHoldMs": 60.0,
          "CompressorEnabled": true,
          "CompressorThresholdDb": -20.0,
          "CompressorRatio": 4.0,
          "CompressorAttackMs": 10.0,
          "CompressorReleaseMs": 150.0,
          "CompressorMakeupGainDb": 5.0,
          "CompressorKneeDb": 4.0,
          "LimiterEnabled": true,
          "LimiterCeilingDb": -1.0,
          "LimiterReleaseMs": 40.0,
          "BypassAll": false,
          "DestroyedMicMemeMode": false,
          "IsMuted": false,
          "HotkeysEnabled": true,
          "HotkeyBypass": { "Enabled": true, "Ctrl": true, "Shift": true, "Alt": false, "Key": "B" },
          "HotkeyMeme": { "Enabled": true, "Ctrl": true, "Shift": true, "Alt": false, "Key": "M" },
          "HotkeyMute": { "Enabled": true, "Ctrl": true, "Shift": true, "Alt": false, "Key": "X" },
          "Language": "ru",
          "CustomProfiles": []
        }"#;

        let cfg: AppConfig = serde_json::from_str(json).expect("documented schema must parse");
        assert_eq!(cfg.compressor_ratio, 4.0);
        assert_eq!(cfg.hotkey_bypass.key, "B");
        assert_eq!(cfg.language.as_deref(), Some("ru"));
    }

    #[test]
    fn ignores_gui_only_fields() {
        let json = r#"{ "MonitorEnabled": true, "MonitorVolume": 0.5, "MonitorDeviceId": "x", "AutoStartEngine": true }"#;
        let cfg: AppConfig = serde_json::from_str(json).expect("unknown fields must be ignored");
        assert!(cfg.high_pass_enabled);
        assert!(cfg.input_device_id.is_none());
    }

    #[test]
    fn serializes_back_to_pascal_case() {
        let json = serde_json::to_string(&AppConfig::default()).expect("must serialize");
        assert!(json.contains("\"HighPassEnabled\""), "unexpected casing: {json}");
        assert!(json.contains("\"CompressorRatio\""));
    }
}
