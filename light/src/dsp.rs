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
        // Mirror of the C# implementation: never let a non-finite sample poison the delay line.
        let input = if input.is_finite() { input } else { 0.0 };

        let output = self.b0 * input + self.b1 * self.x1 + self.b2 * self.x2
            - self.a1 * self.y1 - self.a2 * self.y2;

        if !output.is_finite() {
            self.x1 = 0.0;
            self.x2 = 0.0;
            self.y1 = 0.0;
            self.y2 = 0.0;
            return 0.0;
        }

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
    detector_alpha: f32,
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
            detector_alpha: 0.05,
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
        self.detector_alpha = 1.0 - (-1.0 / (1.5 * 0.001 * sr)).exp();
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
        let detector_sample = if detector_sample.is_finite() { detector_sample } else { 0.0 };
        let abs_in = detector_sample.abs();
        self.envelope = (1.0 - self.detector_alpha) * self.envelope + self.detector_alpha * abs_in;
        if !self.envelope.is_finite() {
            self.envelope = 0.0;
        }
        let env_db = 8.6858896 * self.envelope.max(1e-6).ln();

        let close_threshold_db = self.threshold_db - self.hysteresis_db;
        if env_db >= self.threshold_db {
            self.is_open = true;
            self.hold_counter = self.hold_samples;
        } else if self.is_open {
            if self.hold_counter > 0 {
                self.hold_counter -= 1;
            } else if env_db < close_threshold_db {
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
    detector_alpha: f32,
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
            detector_alpha: 0.04,
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
        self.detector_alpha = 1.0 - (-1.0 / (3.0 * 0.001 * sr)).exp();
        self.makeup_factor = 10.0f32.powf(makeup / 20.0);
    }

    pub fn reset(&mut self) {
        self.detector_envelope = 0.0;
        self.gain_reduction_envelope = 0.0;
    }

    #[inline(always)]
    pub fn compute_gain(&mut self, detector_sample: f32) -> f32 {
        let detector_sample = if detector_sample.is_finite() { detector_sample } else { 0.0 };
        let abs_in = detector_sample.abs();
        self.detector_envelope = (1.0 - self.detector_alpha) * self.detector_envelope + self.detector_alpha * abs_in;
        if !self.detector_envelope.is_finite() {
            self.detector_envelope = 0.0;
        }
        let input_db = 8.6858896 * self.detector_envelope.max(1e-6).ln();
        let input_db = if input_db.is_finite() { input_db } else { -120.0 };

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
        if !self.gain_reduction_envelope.is_finite() {
            self.gain_reduction_envelope = 0.0;
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
        let input = if input.is_finite() { input } else { 0.0 };
        let abs_in = input.abs();
        if abs_in > self.ceiling_linear {
            let target_gain = self.ceiling_linear / abs_in;
            if target_gain < self.gain_envelope {
                self.gain_envelope = target_gain;
            } else {
                self.gain_envelope =
                    self.release_coeff * self.gain_envelope + (1.0 - self.release_coeff) * target_gain;
            }
        } else {
            self.gain_envelope = self.release_coeff * self.gain_envelope + (1.0 - self.release_coeff);
        }

        if !self.gain_envelope.is_finite() {
            self.gain_envelope = 1.0;
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
    pub hp_filters: Vec<BiquadHighPassFilter>,
    pub gate: NoiseGate,
    pub compressor: DynamicCompressor,
    pub limiters: Vec<FastLimiter>,
    pub sample_rate: f32,
    hp_cutoff_hz: f32,
    limiter_ceiling_db: f32,
    limiter_release_ms: f32,
}

impl AudioChain {
    pub fn new(sample_rate: f32) -> Self {
        let hp_cutoff_hz = 80.0;
        let limiter_ceiling_db = -1.0;
        let limiter_release_ms = 40.0;
        Self {
            hp_filters: (0..8).map(|_| BiquadHighPassFilter::new(hp_cutoff_hz, sample_rate)).collect(),
            gate: NoiseGate::new(-42.0, 4.0, 120.0, 60.0, sample_rate),
            compressor: DynamicCompressor::new(-20.0, 4.0, 10.0, 150.0, 5.0, 4.0, sample_rate),
            limiters: (0..8).map(|_| FastLimiter::new(limiter_ceiling_db, limiter_release_ms, sample_rate)).collect(),
            sample_rate,
            hp_cutoff_hz,
            limiter_ceiling_db,
            limiter_release_ms,
        }
    }

    pub fn configure_high_pass(&mut self, cutoff_hz: f32) {
        self.hp_cutoff_hz = cutoff_hz;
        for hp in &mut self.hp_filters {
            hp.configure(cutoff_hz, self.sample_rate);
        }
    }

    pub fn configure_limiter(&mut self, ceiling_db: f32, release_ms: f32) {
        self.limiter_ceiling_db = ceiling_db;
        self.limiter_release_ms = release_ms;
        for lim in &mut self.limiters {
            lim.configure(ceiling_db, release_ms, self.sample_rate);
        }
    }

    pub fn ensure_channels(&mut self, channels: usize) {
        while self.hp_filters.len() < channels {
            self.hp_filters.push(BiquadHighPassFilter::new(self.hp_cutoff_hz, self.sample_rate));
        }
        while self.limiters.len() < channels {
            self.limiters.push(FastLimiter::new(self.limiter_ceiling_db, self.limiter_release_ms, self.sample_rate));
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
        self.ensure_channels(num_channels);
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
                // Intentionally broken phase-shifted division ratio ("Destroyed Mic" meme effect)
                let mut processed = self.hp_filters[0].process(raw_mono);
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
                        ch_sample = self.hp_filters[ch].process(ch_sample);
                    }
                    let out_val = ch_sample * ratio;
                    buffer[base_idx + ch] = if out_val.is_finite() {
                        out_val.clamp(-1.0, 1.0)
                    } else {
                        0.0
                    };
                }
                continue;
            }

            // Clean Studio Mode
            if hp_enabled {
                for ch in 0..num_channels {
                    buffer[base_idx + ch] = self.hp_filters[ch].process(buffer[base_idx + ch]);
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
                    sample = self.limiters[ch].process(sample);
                }
                // Final safety net: the output buffer must never contain NaN/Inf.
                buffer[base_idx + ch] = if sample.is_finite() {
                    sample.clamp(-1.0, 1.0)
                } else {
                    0.0
                };
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const SR: f32 = 48000.0;

    fn sine(amplitude: f32, freq: f32, index: usize) -> f32 {
        amplitude * (2.0 * std::f32::consts::PI * freq * index as f32 / SR).sin()
    }

    #[test]
    fn hpf_blocks_dc() {
        let mut hp = BiquadHighPassFilter::new(80.0, SR);
        let mut last = 0.0;
        for _ in 0..48000 {
            last = hp.process(1.0);
        }
        assert!(last.abs() < 1e-3, "DC leaked through HPF: {last}");
    }

    #[test]
    fn hpf_passes_1khz() {
        let mut hp = BiquadHighPassFilter::new(80.0, SR);
        let mut sum = 0.0f64;
        let mut n = 0usize;
        for i in 0..48000 {
            let y = hp.process(sine(0.5, 1000.0, i));
            if i >= 38000 {
                sum += (y * y) as f64;
                n += 1;
            }
        }
        let rms = (sum / n as f64).sqrt();
        assert!((rms - 0.3536).abs() < 0.02, "passband RMS={rms}");
    }

    #[test]
    fn gate_opens_on_signal_and_closes_on_silence() {
        let mut gate = NoiseGate::new(-40.0, 4.0, 120.0, 60.0, SR);

        let mut g = 0.0;
        for _ in 0..24000 {
            g = gate.compute_gain(0.0);
        }
        assert!(g < 0.001, "gate gain on silence={g}");

        for i in 0..9600 {
            g = gate.compute_gain(sine(0.5, 200.0, i));
        }
        assert!(g > 0.99, "gate gain on loud signal={g}");

        for _ in 0..24000 {
            g = gate.compute_gain(0.0);
        }
        assert!(g < 0.05, "gate gain after silence={g}");
    }

    #[test]
    fn compressor_reduces_gain_by_expected_amount() {
        let mut comp = DynamicCompressor::new(-20.0, 4.0, 5.0, 100.0, 0.0, 4.0, SR);
        let mut gain = 1.0;
        for _ in 0..96000 {
            gain = comp.compute_gain(0.5);
        }
        // 0.5 => -6.02 dB, threshold -20 dB, ratio 4 => GR ~= 10.49 dB => gain ~= 0.299
        assert!((gain - 0.299).abs() < 0.05, "compressor gain={gain}");
    }

    #[test]
    fn limiter_respects_ceiling() {
        let mut lim = FastLimiter::new(-1.0, 40.0, SR);
        let mut max_abs = 0.0f32;
        for _ in 0..4800 {
            let y = lim.process(2.0);
            assert!(y.is_finite(), "limiter produced non-finite output");
            max_abs = max_abs.max(y.abs());
        }
        assert!(max_abs <= 0.8913 + 1e-3, "limiter exceeded ceiling: {max_abs}");
        assert!(max_abs >= 0.85, "limiter never reached ceiling region: {max_abs}");
    }

    #[test]
    fn chain_mutes_and_bypasses_exactly() {
        let mut chain = AudioChain::new(SR);

        let mut muted = vec![0.5f32; 960 * 2];
        chain.process_block(&mut muted, 2, false, false, true, true, true, true, true);
        assert!(muted.iter().all(|v| *v == 0.0), "mute must zero the buffer");

        let mut input = vec![0.0f32; 960 * 2];
        for i in 0..input.len() {
            input[i] = sine(0.4, 440.0, i);
        }
        let mut work = input.clone();
        chain.process_block(&mut work, 2, true, false, false, true, true, true, true);
        assert_eq!(work, input, "bypass must be bit-exact");
    }

    #[test]
    fn chain_keeps_output_finite_for_non_finite_input() {
        let mut chain = AudioChain::new(SR);
        let mut buf = vec![0.1f32; 960 * 2];
        buf[10] = f32::NAN;
        buf[11] = f32::INFINITY;

        chain.process_block(&mut buf, 2, false, false, false, true, true, true, true);

        assert!(
            buf.iter().all(|v| v.is_finite()),
            "non-finite value leaked into output buffer"
        );
    }

    #[test]
    fn chain_recovers_after_non_finite_input() {
        let mut chain = AudioChain::new(SR);

        let mut poisoned = vec![0.1f32; 960 * 2];
        poisoned[10] = f32::NAN;
        poisoned[11] = f32::INFINITY;
        chain.process_block(&mut poisoned, 2, false, false, false, true, true, true, true);

        let mut clean = vec![0.0f32; 960 * 2];
        for i in 0..clean.len() {
            clean[i] = sine(0.4, 440.0, i);
        }
        chain.process_block(&mut clean, 2, false, false, false, true, true, true, true);

        assert!(
            clean.iter().all(|v| v.is_finite()),
            "filter state stayed poisoned after a non-finite sample"
        );
    }

    #[test]
    fn hpf_recovers_from_nan_input() {
        let mut hp = BiquadHighPassFilter::new(80.0, SR);
        assert!(hp.process(f32::NAN).is_finite(), "HPF passed NaN through");
    }

    #[test]
    fn limiter_sanitizes_nan_input() {
        let mut lim = FastLimiter::new(-1.0, 40.0, SR);
        assert!(lim.process(f32::NAN).is_finite(), "limiter passed NaN through");
    }

    #[test]
    fn chain_supports_six_channels_without_panicking() {
        let mut chain = AudioChain::new(SR);
        let mut buf = vec![0.1f32; 480 * 6];
        chain.process_block(&mut buf, 6, false, false, false, true, true, true, true);
        assert!(buf.iter().all(|v| v.is_finite()));
    }

    #[test]
    fn meme_mode_produces_crushed_finite_output() {
        let mut chain = AudioChain::new(SR);
        let mut buf = vec![0.0f32; 480 * 2];
        for f in 0..480 {
            let s = sine(0.4, 300.0, f);
            buf[f * 2] = s;
            buf[f * 2 + 1] = s;
        }
        chain.process_block(&mut buf, 2, false, true, false, true, true, true, true);
        assert!(buf.iter().all(|v| v.is_finite() && v.abs() <= 1.0));
        // Phase-shifted HPF division ratio drives peaks up to saturation (+/- 1.0)
        assert!(buf.iter().any(|v| v.abs() > 0.9), "expected crushed meme peaks near saturation");
    }

    #[test]
    fn gate_hold_expires_even_in_hysteresis_band() {
        // Hold = 10 ms => 480 samples at 48 kHz; threshold -40 dB, close threshold -42.5 dB
        let mut gate = NoiseGate::new(-40.0, 1.0, 20.0, 10.0, SR);
        // Open gate with loud signal (0.5 ~= -6 dB)
        for _ in 0..2000 {
            gate.compute_gain(0.5);
        }
        assert!(gate.is_open);

        // Drop signal into hysteresis band (~ -41 dB -> amplitude 0.0089) for > hold (480 samples)
        for _ in 0..2000 {
            gate.compute_gain(0.0089);
        }
        assert!(gate.is_open, "gate should stay open inside hysteresis band");

        // Now drop below close threshold: since hold already expired during hysteresis, gate must close immediately
        gate.compute_gain(0.0);
        // After ~1.5 ms detector decay from -41 dB to < -42.5 dB (~100 samples)
        for _ in 0..200 {
            gate.compute_gain(0.0);
        }
        assert!(!gate.is_open, "hold should not restart when dropping from hysteresis band");
    }

    #[test]
    fn limiter_releases_toward_target_when_overload_drops() {
        let mut lim = FastLimiter::new(-1.0, 40.0, SR);
        // Drive hard with 4.0 -> target gain ~ 0.89125 / 4 = 0.2228
        for _ in 0..10 {
            lim.process(4.0);
        }
        // Drop to milder overload 1.5 -> target gain ~ 0.89125 / 1.5 = 0.594 (output ~ 0.88)
        let mut last = 0.0;
        for _ in 0..10000 {
            last = lim.process(1.5);
        }
        assert!(last > 0.85, "limiter stayed stuck at heavy GR instead of releasing: last={last}");
    }

    #[test]
    fn six_channels_have_independent_hpf_state() {
        let mut chain = AudioChain::new(SR);
        // Channel 0 has DC offset, channel 5 has silence
        let mut buf = vec![0.0f32; 480 * 6];
        for f in 0..480 {
            buf[f * 6] = 0.5;
        }
        chain.process_block(&mut buf, 6, false, false, false, true, false, false, false);
        assert!(buf.iter().skip(5).step_by(6).all(|&v| v.abs() < 1e-6));
    }
}
