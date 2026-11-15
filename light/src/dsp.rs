#![allow(dead_code)]

use std::f32::consts::PI;

pub struct BiquadHighPassFilter {
    b0: f32,
    b1: f32,
    b2: f32,
    a1: f32,
    a2: f32,
    x1: f32,
    x2: f32,
    y1: f32,
    y2: f32,
}

impl BiquadHighPassFilter {
    pub fn new(cutoff_frequency: f32, sample_rate: f32) -> Self {
        let mut filter = Self {
            b0: 1.0,
            b1: 0.0,
            b2: 0.0,
            a1: 0.0,
            a2: 0.0,
            x1: 0.0,
            x2: 0.0,
            y1: 0.0,
            y2: 0.0,
        };
        filter.configure(cutoff_frequency, sample_rate);
        filter
    }

    pub fn configure(&mut self, cutoff_frequency: f32, sample_rate: f32) {
        let cutoff = cutoff_frequency.clamp(20.0, 500.0);
        let sr = sample_rate.max(8000.0);

        let w0 = 2.0 * PI * cutoff / sr;
        let cos_w0 = w0.cos();
        let sin_w0 = w0.sin();
        let alpha = sin_w0 / (2.0 * 0.70710678);

        let a0 = 1.0 + alpha;
        self.b0 = ((1.0 + cos_w0) / 2.0) / a0;
        self.b1 = (-(1.0 + cos_w0)) / a0;
        self.b2 = ((1.0 + cos_w0) / 2.0) / a0;
        self.a1 = (-2.0 * cos_w0) / a0;
        self.a2 = (1.0 - alpha) / a0;
    }

    pub fn reset(&mut self) {
        self.x1 = 0.0;
        self.x2 = 0.0;
        self.y1 = 0.0;
        self.y2 = 0.0;
    }

    #[inline(always)]
    pub fn process(&mut self, input: f32) -> f32 {
        let output = self.b0 * input + self.b1 * self.x1 + self.b2 * self.x2 - self.a1 * self.y1 - self.a2 * self.y2;
        self.x2 = self.x1;
        self.x1 = input;
        self.y2 = self.y1;
        self.y1 = output;
        output
    }
}

pub struct NoiseGate {
    threshold_db: f32,
    hysteresis_db: f32,
    attack_coeff: f32,
    release_coeff: f32,
    hold_samples: usize,
    hold_counter: usize,
    current_gain: f32,
    envelope: f32,
    is_open: bool,
}

impl NoiseGate {
    pub fn new(threshold_db: f32, attack_ms: f32, release_ms: f32, hold_ms: f32, sample_rate: f32) -> Self {
        let mut gate = Self {
            threshold_db: -42.0,
            hysteresis_db: 2.5,
            attack_coeff: 0.0,
            release_coeff: 0.0,
            hold_samples: 0,
            hold_counter: 0,
            current_gain: 0.0,
            envelope: 0.0,
            is_open: false,
        };
        gate.configure(threshold_db, attack_ms, release_ms, hold_ms, sample_rate);
        gate
    }

    pub fn configure(&mut self, threshold_db: f32, attack_ms: f32, release_ms: f32, hold_ms: f32, sample_rate: f32) {
        self.threshold_db = threshold_db.clamp(-80.0, -10.0);
        let attack = attack_ms.max(0.5);
        let release = release_ms.max(10.0);
        let hold = hold_ms.max(0.0);
        let sr = sample_rate.max(8000.0);

        self.attack_coeff = (-1.0 / (attack * 0.001 * sr)).exp();
        self.release_coeff = (-1.0 / (release * 0.001 * sr)).exp();
        self.hold_samples = (hold * 0.001 * sr) as usize;
    }

    pub fn reset(&mut self) {
        self.current_gain = 0.0;
        self.envelope = 0.0;
        self.hold_counter = 0;
        self.is_open = false;
    }

    #[inline(always)]
    pub fn compute_gain(&mut self, detector_sample: f32) -> f32 {
        let abs_in = detector_sample.abs();
        const DETECTOR_ALPHA: f32 = 0.05;
        self.envelope = (1.0 - DETECTOR_ALPHA) * self.envelope + DETECTOR_ALPHA * abs_in;
        let env_db = 8.6858896 * self.envelope.max(1e-6).ln();

        let close_threshold_db = self.threshold_db - self.hysteresis_db;
        if env_db >= self.threshold_db {
            self.is_open = true;
            self.hold_counter = self.hold_samples;
        } else if env_db < close_threshold_db {
            if self.hold_counter > 0 {
                self.hold_counter -= 1;
                self.is_open = true;
            } else {
                self.is_open = false;
            }
        }

        let target_gain = if self.is_open { 1.0 } else { 0.0 };
        if target_gain > self.current_gain {
            self.current_gain = self.attack_coeff * self.current_gain + (1.0 - self.attack_coeff) * target_gain;
        } else {
            self.current_gain = self.release_coeff * self.current_gain + (1.0 - self.release_coeff) * target_gain;
        }

        if self.current_gain < 0.0001 {
            self.current_gain = 0.0;
        }
        if self.current_gain > 0.9999 {
            self.current_gain = 1.0;
        }
        self.current_gain
    }
}

pub struct DynamicCompressor {
    threshold_db: f32,
    ratio: f32,
    knee_db: f32,
    attack_coeff: f32,
    release_coeff: f32,
    makeup_factor: f32,
    detector_envelope: f32,
    gain_reduction_envelope: f32,
}

impl DynamicCompressor {
    pub fn new(threshold_db: f32, ratio: f32, attack_ms: f32, release_ms: f32, makeup_gain_db: f32, knee_db: f32, sample_rate: f32) -> Self {
        let mut comp = Self {
            threshold_db: -20.0,
            ratio: 4.0,
            knee_db: 4.0,
            attack_coeff: 0.0,
            release_coeff: 0.0,
            makeup_factor: 1.0,
            detector_envelope: 0.0,
            gain_reduction_envelope: 0.0,
        };
        comp.configure(threshold_db, ratio, attack_ms, release_ms, makeup_gain_db, knee_db, sample_rate);
        comp
    }

    pub fn configure(&mut self, threshold_db: f32, ratio: f32, attack_ms: f32, release_ms: f32, makeup_gain_db: f32, knee_db: f32, sample_rate: f32) {
        self.threshold_db = threshold_db.clamp(-60.0, 0.0);
        self.ratio = ratio.max(1.0);
        self.knee_db = knee_db.max(0.1);
        let attack = attack_ms.max(0.5);
        let release = release_ms.max(5.0);
        let makeup = makeup_gain_db.clamp(0.0, 24.0);
        let sr = sample_rate.max(8000.0);

        self.attack_coeff = (-1.0 / (attack * 0.001 * sr)).exp();
        self.release_coeff = (-1.0 / (release * 0.001 * sr)).exp();
        self.makeup_factor = 10.0f32.powf(makeup / 20.0);
    }

    pub fn reset(&mut self) {
        self.detector_envelope = 0.0;
        self.gain_reduction_envelope = 0.0;
    }

    #[inline(always)]
    pub fn compute_gain(&mut self, detector_sample: f32) -> f32 {
        let abs_in = detector_sample.abs();
        const DETECTOR_ALPHA: f32 = 0.04;
        self.detector_envelope = (1.0 - DETECTOR_ALPHA) * self.detector_envelope + DETECTOR_ALPHA * abs_in;
        let input_db = 8.6858896 * self.detector_envelope.max(1e-6).ln();

        let half_knee = self.knee_db * 0.5;
        let gr_db = if input_db <= self.threshold_db - half_knee {
            0.0
        } else if input_db > self.threshold_db + half_knee {
            (input_db - self.threshold_db) * (1.0 - 1.0 / self.ratio)
        } else {
            let delta = input_db - self.threshold_db + half_knee;
            ((delta * delta) / (2.0 * self.knee_db)) * (1.0 - 1.0 / self.ratio)
        };

        if gr_db > self.gain_reduction_envelope {
            self.gain_reduction_envelope = self.attack_coeff * self.gain_reduction_envelope + (1.0 - self.attack_coeff) * gr_db;
        } else {
            self.gain_reduction_envelope = self.release_coeff * self.gain_reduction_envelope + (1.0 - self.release_coeff) * gr_db;
        }

        (-self.gain_reduction_envelope * 0.11512925).exp() * self.makeup_factor
    }
}

pub struct FastLimiter {
    ceiling_linear: f32,
    release_coeff: f32,
    gain_envelope: f32,
}

impl FastLimiter {
    pub fn new(ceiling_db: f32, release_ms: f32, sample_rate: f32) -> Self {
        let mut lim = Self {
            ceiling_linear: 0.89125,
            release_coeff: 0.0,
            gain_envelope: 1.0,
        };
        lim.configure(ceiling_db, release_ms, sample_rate);
        lim
    }

    pub fn configure(&mut self, ceiling_db: f32, release_ms: f32, sample_rate: f32) {
        let c_db = ceiling_db.clamp(-6.0, 0.0);
        self.ceiling_linear = 10.0f32.powf(c_db / 20.0);
        let release = release_ms.max(5.0);
        let sr = sample_rate.max(8000.0);
        self.release_coeff = (-1.0 / (release * 0.001 * sr)).exp();
    }

    pub fn reset(&mut self) {
        self.gain_envelope = 1.0;
    }

    #[inline(always)]
    pub fn process(&mut self, input: f32) -> f32 {
        let abs_in = input.abs();
        if abs_in > self.ceiling_linear {
            let target_gain = self.ceiling_linear / abs_in;
            if target_gain < self.gain_envelope {
                self.gain_envelope = target_gain;
            }
        } else {
            self.gain_envelope = self.release_coeff * self.gain_envelope + (1.0 - self.release_coeff);
        }

        let mut limited = input * self.gain_envelope;
        let soft_threshold = self.ceiling_linear * 0.95;
        if limited.abs() > soft_threshold {
            let sign = limited.signum();
            let excess = (limited.abs() - soft_threshold) / (self.ceiling_linear - soft_threshold);
            let excess_tanh = excess.tanh();
            limited = sign * (soft_threshold + excess_tanh * (self.ceiling_linear - soft_threshold));
        }

        limited.clamp(-1.0, 1.0)
    }
}

pub struct AudioChain {
    pub hp_filters: [BiquadHighPassFilter; 2],
    pub gate: NoiseGate,
    pub compressor: DynamicCompressor,
    pub limiters: [FastLimiter; 2],
    pub sample_rate: f32,
}

impl AudioChain {
    pub fn new(sample_rate: f32) -> Self {
        Self {
            hp_filters: [
                BiquadHighPassFilter::new(80.0, sample_rate),
                BiquadHighPassFilter::new(80.0, sample_rate),
            ],
            gate: NoiseGate::new(-42.0, 4.0, 120.0, 60.0, sample_rate),
            compressor: DynamicCompressor::new(-20.0, 4.0, 10.0, 150.0, 5.0, 4.0, sample_rate),
            limiters: [
                FastLimiter::new(-1.0, 40.0, sample_rate),
                FastLimiter::new(-1.0, 40.0, sample_rate),
            ],
            sample_rate,
        }
    }

    pub fn process_block(
        &mut self,
        buffer: &mut [f32],
        channels: usize,
        bypass: bool,
        meme_mode: bool,
        is_muted: bool,
        hp_enabled: bool,
        gate_enabled: bool,
        comp_enabled: bool,
        lim_enabled: bool,
    ) {
        if is_muted {
            buffer.fill(0.0);
            return;
        }

        if bypass {
            return;
        }

        let num_channels = channels.max(1);
        let frame_count = buffer.len() / num_channels;

        for frame in 0..frame_count {
            let base_idx = frame * num_channels;

            // 1. Calculate raw mono
            let mut raw_mono = 0.0f32;
            for ch in 0..num_channels {
                raw_mono += buffer[base_idx + ch];
            }
            raw_mono /= num_channels as f32;

            if meme_mode {
                let mut processed = raw_mono;
                if hp_enabled {
                    processed = self.hp_filters[0].process(processed);
                }
                if gate_enabled {
                    processed = self.gate.compute_gain(processed) * processed;
                }
                if comp_enabled {
                    processed = self.compressor.compute_gain(processed) * processed;
                }
                if lim_enabled {
                    processed = self.limiters[0].process(processed);
                }

                let mut ratio = if raw_mono.abs() > 1e-6 {
                    processed / raw_mono
                } else {
                    0.0
                };
                if ratio.is_nan() || ratio.is_infinite() {
                    ratio = 1.0;
                }

                for ch in 0..num_channels {
                    let mut ch_sample = buffer[base_idx + ch];
                    if hp_enabled {
                        ch_sample = self.hp_filters[0].process(ch_sample);
                    }
                    buffer[base_idx + ch] = (ch_sample * ratio).clamp(-1.0, 1.0);
                }
                continue;
            }

            // Clean Studio Mode
            if hp_enabled {
                for ch in 0..num_channels {
                    let filter_idx = ch.min(self.hp_filters.len() - 1);
                    buffer[base_idx + ch] = self.hp_filters[filter_idx].process(buffer[base_idx + ch]);
                }
            }

            let mut filtered_mono = 0.0f32;
            for ch in 0..num_channels {
                filtered_mono += buffer[base_idx + ch];
            }
            filtered_mono /= num_channels as f32;

            let gate_gain = if gate_enabled {
                self.gate.compute_gain(filtered_mono)
            } else {
                1.0
            };

            let comp_gain = if comp_enabled {
                self.compressor.compute_gain(filtered_mono)
            } else {
                1.0
            };

            let total_gain = gate_gain * comp_gain;

            for ch in 0..num_channels {
                let mut sample = buffer[base_idx + ch] * total_gain;
                if lim_enabled {
                    let lim_idx = ch.min(self.limiters.len() - 1);
                    sample = self.limiters[lim_idx].process(sample);
                }
                buffer[base_idx + ch] = sample.clamp(-1.0, 1.0);
            }
        }
    }
}
