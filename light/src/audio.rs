use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};
use cpal::{SampleFormat, Stream, StreamConfig};
use ringbuf::HeapRb;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use crate::config::AppConfig;
use crate::dsp::AudioChain;

pub struct AudioControls {
    pub bypass: Arc<AtomicBool>,
    pub meme_mode: Arc<AtomicBool>,
    pub is_muted: Arc<AtomicBool>,
}

pub struct AudioEngineHandle {
    #[allow(dead_code)]
    pub in_stream: Stream,
    #[allow(dead_code)]
    pub out_stream: Stream,
    pub controls: AudioControls,
}

struct InputProcessorState {
    chain: AudioChain,
    scratch_buffer: Vec<f32>,
    prev_frame: Vec<f32>,
    has_prev_frame: bool,
    resample_phase: f32,
    in_channels: usize,
    in_sample_rate: f32,
    out_channels: usize,
    out_sample_rate: f32,
    bypass: Arc<AtomicBool>,
    meme_mode: Arc<AtomicBool>,
    is_muted: Arc<AtomicBool>,
    hp_enabled: bool,
    gate_enabled: bool,
    comp_enabled: bool,
    lim_enabled: bool,
}

impl InputProcessorState {
    fn process_and_push(&mut self, producer: &mut ringbuf::HeapProducer<f32>) {
        if self.scratch_buffer.is_empty() || self.in_channels == 0 {
            return;
        }

        let b = self.bypass.load(Ordering::Relaxed);
        let m = self.meme_mode.load(Ordering::Relaxed);
        let muted = self.is_muted.load(Ordering::Relaxed);

        self.chain.process_block(
            &mut self.scratch_buffer,
            self.in_channels,
            b,
            m,
            muted,
            self.hp_enabled,
            self.gate_enabled,
            self.comp_enabled,
            self.lim_enabled,
        );

        let in_frames = self.scratch_buffer.len() / self.in_channels;
        if in_frames == 0 {
            return;
        }

        resample_and_push(
            &self.scratch_buffer,
            in_frames,
            self.in_channels,
            self.in_sample_rate,
            self.out_channels,
            self.out_sample_rate,
            &mut self.prev_frame,
            &mut self.has_prev_frame,
            &mut self.resample_phase,
            producer,
        );
    }
}

#[allow(clippy::too_many_arguments)]
fn resample_and_push(
    samples: &[f32],
    in_frames: usize,
    in_channels: usize,
    in_sample_rate: f32,
    out_channels: usize,
    out_sample_rate: f32,
    prev_frame: &mut [f32],
    has_prev_frame: &mut bool,
    resample_phase: &mut f32,
    producer: &mut ringbuf::HeapProducer<f32>,
) {
    if in_frames == 0 || in_channels == 0 || out_channels == 0 {
        return;
    }

    if (in_sample_rate - out_sample_rate).abs() < 0.5 {
        for frame in 0..in_frames {
            let base = frame * in_channels;
            let left = samples[base];
            let right = if in_channels >= 2 { samples[base + 1] } else { left };
            push_output_frame(producer, out_channels, left, right);
        }
        let last_base = (in_frames - 1) * in_channels;
        let copy_ch = in_channels.min(prev_frame.len());
        prev_frame[..copy_ch].copy_from_slice(&samples[last_base..last_base + copy_ch]);
        *has_prev_frame = true;
        *resample_phase = 0.0;
        return;
    }

    let rate_ratio = (in_sample_rate as f64) / (out_sample_rate as f64);
    let mut in_pos = *resample_phase as f64;
    if !*has_prev_frame && in_pos < 0.0 {
        in_pos = 0.0;
    }
    let limit = (in_frames as f64) - 1.0;

    while in_pos < limit {
        let floor_pos = in_pos.floor();
        let in_frame0 = floor_pos as isize;
        let frac = (in_pos - floor_pos) as f32;

        let (l0, r0) = if in_frame0 < 0 {
            let l = prev_frame.first().copied().unwrap_or(0.0);
            let r = if in_channels >= 2 { prev_frame.get(1).copied().unwrap_or(l) } else { l };
            (l, r)
        } else {
            let f0 = (in_frame0 as usize).min(in_frames - 1);
            let l = samples[f0 * in_channels];
            let r = if in_channels >= 2 { samples[f0 * in_channels + 1] } else { l };
            (l, r)
        };

        let f1 = ((in_frame0 + 1).max(0) as usize).min(in_frames - 1);
        let l1 = samples[f1 * in_channels];
        let r1 = if in_channels >= 2 { samples[f1 * in_channels + 1] } else { l1 };

        let left = l0 + frac * (l1 - l0);
        let right = r0 + frac * (r1 - r0);
        push_output_frame(producer, out_channels, left, right);

        in_pos += rate_ratio;
    }

    let last_base = (in_frames - 1) * in_channels;
    let copy_ch = in_channels.min(prev_frame.len());
    prev_frame[..copy_ch].copy_from_slice(&samples[last_base..last_base + copy_ch]);
    *has_prev_frame = true;

    let next_phase = (in_pos - in_frames as f64) as f32;
    *resample_phase = if next_phase.is_finite() && next_phase >= -1.0 && next_phase < rate_ratio as f32 {
        next_phase
    } else {
        0.0
    };
}

#[inline(always)]
fn push_output_frame(producer: &mut ringbuf::HeapProducer<f32>, out_channels: usize, left: f32, right: f32) {
    if out_channels == 1 {
        let _ = producer.push(left);
    } else {
        let _ = producer.push(left);
        let _ = producer.push(right);
        for _ in 2..out_channels {
            let _ = producer.push(0.0);
        }
    }
}

pub fn start_audio_engine(cfg: &AppConfig) -> Result<AudioEngineHandle, String> {
    let host = cpal::default_host();

    let match_device = |dev: &cpal::Device, id_opt: &Option<String>, name_opt: &Option<String>| -> bool {
        if let Ok(dev_name) = dev.name() {
            let dev_name_lower = dev_name.to_lowercase();
            if let Some(ref target) = name_opt {
                if !target.is_empty() && (dev_name.contains(target) || dev_name_lower.contains(&target.to_lowercase())) {
                    return true;
                }
            }
            if let Some(ref target) = id_opt {
                if !target.is_empty() && (dev_name.contains(target) || dev_name_lower.contains(&target.to_lowercase())) {
                    return true;
                }
            }
        }
        false
    };

    // 1. Find input device
    let input_device = host
        .input_devices()
        .map_err(|e| e.to_string())?
        .find(|d| match_device(d, &cfg.input_device_id, &cfg.input_device_name))
        .or_else(|| host.default_input_device())
        .ok_or("No input audio device found")?;

    // 2. Find output device (prefer configured output, then "SMC virtual cable" / "CABLE Input", then default)
    let output_device = host
        .output_devices()
        .map_err(|e| e.to_string())?
        .find(|d| match_device(d, &cfg.output_device_id, &cfg.output_device_name))
        .or_else(|| {
            host.output_devices().ok().and_then(|mut devs| {
                devs.find(|d| {
                    d.name().map(|n| {
                        let nl = n.to_lowercase();
                        nl.contains("smc virtual") || nl.contains("cable input") || nl.contains("vb-audio")
                    }).unwrap_or(false)
                })
            })
        })
        .or_else(|| host.default_output_device())
        .ok_or("No output audio device found")?;

    let in_config = input_device.default_input_config().map_err(|e| e.to_string())?;
    let out_config = output_device.default_output_config().map_err(|e| e.to_string())?;

    let in_sample_rate = in_config.sample_rate().0 as f32;
    let in_channels = in_config.channels() as usize;

    // Create DSP chain
    let mut chain = AudioChain::new(in_sample_rate);
    chain.ensure_channels(in_channels);
    chain.configure_high_pass(cfg.high_pass_cutoff_hz);
    chain.gate.configure(
        cfg.gate_threshold_db,
        cfg.gate_attack_ms,
        cfg.gate_release_ms,
        cfg.gate_hold_ms,
        in_sample_rate,
    );
    chain.compressor.configure(
        cfg.compressor_threshold_db,
        cfg.compressor_ratio,
        cfg.compressor_attack_ms,
        cfg.compressor_release_ms,
        cfg.compressor_makeup_gain_db,
        cfg.compressor_knee_db,
        in_sample_rate,
    );
    chain.configure_limiter(cfg.limiter_ceiling_db, cfg.limiter_release_ms);

    let bypass = Arc::new(AtomicBool::new(cfg.bypass_all));
    let meme_mode = Arc::new(AtomicBool::new(cfg.destroyed_mic_meme_mode));
    let is_muted = Arc::new(AtomicBool::new(cfg.is_muted));

    let out_sample_rate = out_config.sample_rate().0 as f32;
    let out_channels = out_config.channels() as usize;

    // Ring buffer: ~150 ms capacity (aligned to out_channels) to bound maximum latency
    let raw_capacity = ((out_sample_rate * 0.15) as usize).max(1024) * out_channels.max(1);
    let buffer_capacity = raw_capacity.max(4096 * out_channels.max(1));
    let ring_buffer = HeapRb::<f32>::new(buffer_capacity);
    let (mut producer, mut consumer) = ring_buffer.split();

    let mut state = InputProcessorState {
        chain,
        scratch_buffer: Vec::with_capacity(2048),
        prev_frame: vec![0.0; in_channels.max(2)],
        has_prev_frame: false,
        resample_phase: 0.0,
        in_channels,
        in_sample_rate,
        out_channels,
        out_sample_rate,
        bypass: bypass.clone(),
        meme_mode: meme_mode.clone(),
        is_muted: is_muted.clone(),
        hp_enabled: cfg.high_pass_enabled,
        gate_enabled: cfg.gate_enabled,
        comp_enabled: cfg.compressor_enabled,
        lim_enabled: cfg.limiter_enabled,
    };

    // Input stream callback
    let in_sample_format = in_config.sample_format();
    let in_stream_cfg: StreamConfig = in_config.into();

    let err_fn = |err| eprintln!("Audio stream error: {err}");

    let in_stream = match in_sample_format {
        SampleFormat::F32 => input_device
            .build_input_stream(
                &in_stream_cfg,
                move |data: &[f32], _: &cpal::InputCallbackInfo| {
                    state.scratch_buffer.clear();
                    state.scratch_buffer.extend_from_slice(data);
                    state.process_and_push(&mut producer);
                },
                err_fn,
                None,
            )
            .map_err(|e| e.to_string())?,
        SampleFormat::I16 => input_device
            .build_input_stream(
                &in_stream_cfg,
                move |data: &[i16], _: &cpal::InputCallbackInfo| {
                    state.scratch_buffer.clear();
                    for &s in data {
                        state.scratch_buffer.push(s as f32 / 32768.0);
                    }
                    state.process_and_push(&mut producer);
                },
                err_fn,
                None,
            )
            .map_err(|e| e.to_string())?,
        _ => return Err(format!("Unsupported input sample format: {:?}", in_sample_format)),
    };

    // Output stream callback
    let out_sample_format = out_config.sample_format();
    let out_stream_cfg: StreamConfig = out_config.into();

    let out_stream = match out_sample_format {
        SampleFormat::F32 => {
            output_device.build_output_stream(
                &out_stream_cfg,
                move |data: &mut [f32], _: &cpal::OutputCallbackInfo| {
                    for sample in data.iter_mut() {
                        *sample = consumer.pop().unwrap_or(0.0);
                    }
                },
                err_fn,
                None,
            ).map_err(|e| e.to_string())?
        }
        SampleFormat::I16 => {
            output_device.build_output_stream(
                &out_stream_cfg,
                move |data: &mut [i16], _: &cpal::OutputCallbackInfo| {
                    for sample in data.iter_mut() {
                        let f = consumer.pop().unwrap_or(0.0);
                        *sample = (f * 32767.0).clamp(-32768.0, 32767.0) as i16;
                    }
                },
                err_fn,
                None,
            ).map_err(|e| e.to_string())?
        }
        _ => return Err(format!("Unsupported output sample format: {:?}", out_sample_format)),
    };

    in_stream.play().map_err(|e| e.to_string())?;
    out_stream.play().map_err(|e| e.to_string())?;

    Ok(AudioEngineHandle {
        in_stream,
        out_stream,
        controls: AudioControls {
            bypass,
            meme_mode,
            is_muted,
        },
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn resampler_is_continuous_across_block_boundaries() {
        // Linear ramp at 44.1 kHz upsampled to 48 kHz across two consecutive blocks
        let rb = HeapRb::<f32>::new(4096);
        let (mut prod, mut cons) = rb.split();
        let mut prev_frame = vec![0.0f32; 2];
        let mut has_prev = false;
        let mut phase = 0.0f32;

        let block1: Vec<f32> = (0..441).map(|i| i as f32 / 1000.0).collect();
        let block2: Vec<f32> = (441..882).map(|i| i as f32 / 1000.0).collect();

        resample_and_push(&block1, 441, 1, 44100.0, 1, 48000.0, &mut prev_frame, &mut has_prev, &mut phase, &mut prod);
        resample_and_push(&block2, 441, 1, 44100.0, 1, 48000.0, &mut prev_frame, &mut has_prev, &mut phase, &mut prod);

        let mut out = Vec::new();
        while let Some(s) = cons.pop() {
            out.push(s);
        }

        assert!(out.len() >= 958 && out.len() <= 961, "unexpected output frame count: {}", out.len());
        // On a monotonic ramp, every consecutive resampled sample must be strictly increasing with near-constant step
        let expected_step = 44100.0 / 48000.0 / 1000.0;
        for i in 1..out.len() {
            let diff = out[i] - out[i - 1];
            assert!(
                (diff - expected_step).abs() < 1e-5,
                "discontinuity at sample {i}: diff={diff} expected={expected_step}"
            );
        }
    }
}
