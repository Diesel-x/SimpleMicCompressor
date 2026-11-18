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

pub fn start_audio_engine(cfg: &AppConfig) -> Result<AudioEngineHandle, String> {
    let host = cpal::default_host();

    // 1. Find input device
    let input_device = if let Some(ref name) = cfg.input_device_id {
        host.input_devices()
            .map_err(|e| e.to_string())?
            .find(|d| d.name().map(|n| n.contains(name)).unwrap_or(false))
            .or_else(|| host.default_input_device())
    } else {
        host.default_input_device()
    }.ok_or("No input audio device found")?;

    // 2. Find output device (prefer "CABLE Input" for VB-Audio Virtual Cable)
    let output_device = if let Some(ref name) = cfg.output_device_id {
        host.output_devices()
            .map_err(|e| e.to_string())?
            .find(|d| d.name().map(|n| n.contains(name)).unwrap_or(false))
            .or_else(|| host.default_output_device())
    } else {
        host.output_devices()
            .map_err(|e| e.to_string())?
            .find(|d| d.name().map(|n| n.contains("CABLE Input")).unwrap_or(false))
            .or_else(|| host.default_output_device())
    }.ok_or("No output audio device found")?;

    let in_config = input_device.default_input_config().map_err(|e| e.to_string())?;
    let out_config = output_device.default_output_config().map_err(|e| e.to_string())?;

    let in_sample_rate = in_config.sample_rate().0 as f32;
    let in_channels = in_config.channels() as usize;

    // Create DSP chain
    let mut chain = AudioChain::new(in_sample_rate);
    chain.hp_filters[0].configure(cfg.high_pass_cutoff_hz, in_sample_rate);
    chain.hp_filters[1].configure(cfg.high_pass_cutoff_hz, in_sample_rate);
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
    chain.limiters[0].configure(cfg.limiter_ceiling_db, cfg.limiter_release_ms, in_sample_rate);
    chain.limiters[1].configure(cfg.limiter_ceiling_db, cfg.limiter_release_ms, in_sample_rate);

    let hp_enabled = cfg.high_pass_enabled;
    let gate_enabled = cfg.gate_enabled;
    let comp_enabled = cfg.compressor_enabled;
    let lim_enabled = cfg.limiter_enabled;

    let bypass = Arc::new(AtomicBool::new(cfg.bypass_all));
    let meme_mode = Arc::new(AtomicBool::new(cfg.destroyed_mic_meme_mode));
    let is_muted = Arc::new(AtomicBool::new(cfg.is_muted));

    let bypass_in = bypass.clone();
    let meme_in = meme_mode.clone();
    let mute_in = is_muted.clone();

    let out_sample_rate = out_config.sample_rate().0 as f32;
    let out_channels = out_config.channels() as usize;

    // Ring buffer: 1.5 seconds capacity for minimal latency and jitter absorption
    let buffer_capacity = (out_sample_rate as usize * out_channels) * 3 / 2;
    let ring_buffer = HeapRb::<f32>::new(buffer_capacity.max(4096));
    let (mut producer, mut consumer) = ring_buffer.split();

    // Input stream callback
    let in_sample_format = in_config.sample_format();
    let in_stream_cfg: StreamConfig = in_config.into();

    let mut scratch_buffer = Vec::<f32>::with_capacity(2048);

    let err_fn = |err| eprintln!("Audio stream error: {err}");

    let in_stream = match in_sample_format {
        SampleFormat::F32 => {
            input_device.build_input_stream(
                &in_stream_cfg,
                move |data: &[f32], _: &cpal::InputCallbackInfo| {
                    scratch_buffer.clear();
                    scratch_buffer.extend_from_slice(data);

                    let b = bypass_in.load(Ordering::Relaxed);
                    let m = meme_in.load(Ordering::Relaxed);
                    let muted = mute_in.load(Ordering::Relaxed);

                    chain.process_block(
                        &mut scratch_buffer,
                        in_channels,
                        b,
                        m,
                        muted,
                        hp_enabled,
                        gate_enabled,
                        comp_enabled,
                        lim_enabled,
                    );

                    let in_frames = scratch_buffer.len() / in_channels;
                    let rate_ratio = in_sample_rate / out_sample_rate;
                    let out_frames = ((in_frames as f32 * out_sample_rate / in_sample_rate).round() as usize).max(1);

                    for out_frame in 0..out_frames {
                        let in_pos = out_frame as f32 * rate_ratio;
                        let in_frame0 = (in_pos as usize).min(in_frames.saturating_sub(1));
                        let in_frame1 = (in_frame0 + 1).min(in_frames.saturating_sub(1));
                        let frac = in_pos - in_frame0 as f32;

                        let l0 = scratch_buffer[in_frame0 * in_channels];
                        let l1 = scratch_buffer[in_frame1 * in_channels];
                        let left = l0 + frac * (l1 - l0);

                        let right = if in_channels >= 2 {
                            let r0 = scratch_buffer[in_frame0 * in_channels + 1];
                            let r1 = scratch_buffer[in_frame1 * in_channels + 1];
                            r0 + frac * (r1 - r0)
                        } else {
                            left
                        };

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
                },
                err_fn,
                None,
            ).map_err(|e| e.to_string())?
        }
        SampleFormat::I16 => {
            input_device.build_input_stream(
                &in_stream_cfg,
                move |data: &[i16], _: &cpal::InputCallbackInfo| {
                    scratch_buffer.clear();
                    for &s in data {
                        scratch_buffer.push(s as f32 / 32768.0);
                    }

                    let b = bypass_in.load(Ordering::Relaxed);
                    let m = meme_in.load(Ordering::Relaxed);
                    let muted = mute_in.load(Ordering::Relaxed);

                    chain.process_block(
                        &mut scratch_buffer,
                        in_channels,
                        b,
                        m,
                        muted,
                        hp_enabled,
                        gate_enabled,
                        comp_enabled,
                        lim_enabled,
                    );

                    let in_frames = scratch_buffer.len() / in_channels;
                    let rate_ratio = in_sample_rate / out_sample_rate;
                    let out_frames = ((in_frames as f32 * out_sample_rate / in_sample_rate).round() as usize).max(1);

                    for out_frame in 0..out_frames {
                        let in_pos = out_frame as f32 * rate_ratio;
                        let in_frame0 = (in_pos as usize).min(in_frames.saturating_sub(1));
                        let in_frame1 = (in_frame0 + 1).min(in_frames.saturating_sub(1));
                        let frac = in_pos - in_frame0 as f32;

                        let l0 = scratch_buffer[in_frame0 * in_channels];
                        let l1 = scratch_buffer[in_frame1 * in_channels];
                        let left = l0 + frac * (l1 - l0);

                        let right = if in_channels >= 2 {
                            let r0 = scratch_buffer[in_frame0 * in_channels + 1];
                            let r1 = scratch_buffer[in_frame1 * in_channels + 1];
                            r0 + frac * (r1 - r0)
                        } else {
                            left
                        };

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
                },
                err_fn,
                None,
            ).map_err(|e| e.to_string())?
        }
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
