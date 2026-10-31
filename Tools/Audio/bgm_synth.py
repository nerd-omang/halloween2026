import sys
import wave

import numpy as np

# Usage: python bgm_synth.py <output.wav>
# Every component is a whole number of cycles per loop, or circular, so the last sample flows into the first.

SR = 22050
LOOP = 72.0  # seconds
N = int(SR * LOOP)
rng = np.random.default_rng(11)
t = np.arange(N) / SR


def snap(f):
    # Round to a frequency that completes a whole number of cycles in one loop.
    return round(f * LOOP) / LOOP


def pad(f, amp, lfo_cycles, lfo_depth):
    lfo = 0.5 + 0.5 * np.sin(2 * np.pi * lfo_cycles * t / LOOP)
    return amp * (1 - lfo_depth + lfo_depth * lfo) * np.sin(2 * np.pi * snap(f) * t)


def wind(cutoff=300.0):
    # Circular lowpass on noise (FFT), so the loop closes without a seam.
    x = rng.standard_normal(N)
    spectrum = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(N, 1 / SR)
    spectrum[freqs > cutoff] = 0
    y = np.fft.irfft(spectrum, N)
    y /= np.max(np.abs(y))
    swell = 0.5 + 0.5 * np.sin(2 * np.pi * t / LOOP + 1.3)
    return y * swell


SCALE = [220.0, 261.63, 293.66, 329.63, 392.0, 440.0, 523.25]  # A minor pentatonic-ish


def bell(f, length=4.0, tau=1.1):
    n = int(SR * length)
    tt = np.arange(n) / SR
    return (np.sin(2 * np.pi * f * tt) + 0.25 * np.sin(2 * np.pi * 2 * f * tt)) * np.exp(-tt / tau)


def add_circular(buf, start, x):
    idx = (np.arange(len(x)) + start) % N
    buf[idx] += x


def sparse_bells():
    buf = np.zeros(N)
    cursor = rng.uniform(1.0, 3.0)
    while cursor < LOOP:
        f = SCALE[rng.integers(len(SCALE))]
        add_circular(buf, int(cursor * SR), bell(f) * 0.28)
        cursor += rng.uniform(4.0, 8.0)
    return buf


def crush(x, levels=256):
    return np.round(x * levels) / levels


def write_wav(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


drone = (
    pad(110.0, 0.22, 2, 0.5)
    + pad(164.81, 0.12, 3, 0.6)
    + pad(82.41, 0.14, 1, 0.3)
)
mix = 0.7 * drone + 0.5 * wind() + sparse_bells()
mix = crush(mix / np.max(np.abs(mix)) * 0.4)
write_wav(sys.argv[1], mix)
print(f"loop {LOOP:.0f}s, samples {N}, peak {np.max(np.abs(mix)):.2f}")
