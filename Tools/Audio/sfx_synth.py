import os
import sys
import wave

import numpy as np

SR = 22050
OUT = sys.argv[1]
rng = np.random.default_rng(7)


def phase(f):
    return 2 * np.pi * np.cumsum(f) / SR


def square(p):
    return np.sign(np.sin(p))



def sine(p):
    return np.sin(p)


def tri(p):
    return 2 / np.pi * np.arcsin(np.sin(p))


def env(n, a=0.004, r=0.03):
    e = np.ones(n)
    na = min(n, int(a * SR))
    nr = min(n, int(r * SR))
    if na > 0:
        e[:na] = np.linspace(0, 1, na)
    if nr > 0:
        e[n - nr:] *= np.linspace(1, 0, nr)
    return e


def sweep(f0, f1, d, wave=square, a=0.004, r=0.03, vib=0.0, vib_rate=0.0, curve=1.0):
    n = int(SR * d)
    x = np.linspace(0, 1, n) ** curve
    f = f0 + (f1 - f0) * x
    if vib:
        f = f * (1 + vib * np.sin(2 * np.pi * vib_rate * np.arange(n) / SR))
    return wave(phase(f)) * env(n, a, r)


def tone(freq, d, wave=square, a=0.002, r=0.03, vib=0.0, vib_rate=0.0):
    return sweep(freq, freq, d, wave, a, r, vib, vib_rate)


def noise(d, a=0.002, r=0.05):
    n = int(SR * d)
    return rng.uniform(-1, 1, n) * env(n, a, r)


def smooth(x, k):
    return np.convolve(x, np.ones(k) / k, mode="same")


def gap(d):
    return np.zeros(int(SR * d))


def seq(*parts):
    return np.concatenate(parts)


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def crush(x, levels=10):
    return np.round(x * levels) / levels


def finish(x, peak=0.85):
    x = crush(x)
    m = np.max(np.abs(x))
    return x / m * peak if m > 0 else x


def sounds():
    s = {}

    # Sparkly up-arpeggio: a candy appearing out of nowhere.
    s["candy_appear"] = seq(
        tone(523, 0.05, sine),
        tone(659, 0.05, sine),
        tone(784, 0.09, sine, a=0.004, r=0.08),
    )

    # Short rising blip used as a combo layer; pitch is raised in the game as the streak grows.
    s["combo_up"] = seq(
        tone(784, 0.05, tri),
        tone(988, 0.05, tri),
        tone(1175, 0.12, tri, r=0.09, vib=0.01, vib_rate=10),
    )

    # Swoosh: filtered noise plus a short rising whistle.
    swish_len = 0.24
    n = int(SR * swish_len)
    swoosh = smooth(noise(swish_len, a=0.03, r=0.12), 5) * np.sin(np.linspace(0, np.pi, n)) ** 2
    s["candy_toss"] = mix(swoosh * 1.2, sweep(500, 1400, swish_len, sine, a=0.02, r=0.1) * 0.35)

    # Plop-bonk when the candy lands.
    s["candy_land"] = mix(
        sweep(220, 120, 0.12, sine, a=0.003, r=0.07),
        noise(0.008, a=0.0005, r=0.008) * 0.25,
    )

    # Happy arpeggio ending on a held, vibrating top note.
    s["answer_correct"] = seq(
        tone(523, 0.07), tone(659, 0.07), tone(784, 0.07),
        tone(1046, 0.22, r=0.12, vib=0.015, vib_rate=8),
    )

    # Comedic "wah-wah" sliding down with a wobble.
    s["answer_wrong"] = seq(
        sweep(440, 220, 0.2, square, r=0.05, vib=0.08, vib_rate=9),
        gap(0.02),
        sweep(330, 165, 0.3, tri, r=0.12, vib=0.1, vib_rate=7),
    )

    # Glass shatter: noise burst, a falling whistle, and a few tinkles.
    s["heart_lost"] = seq(
        mix(noise(0.12, a=0.001, r=0.1), sweep(2000, 700, 0.12, square, a=0.001, r=0.08)),
        tone(2400, 0.05, a=0.001, r=0.04),
        tone(1800, 0.06, a=0.001, r=0.05),
        tone(2100, 0.1, a=0.001, r=0.08),
    )

    # Cartoon thwack pushing a visitor away.
    s["visitor_shoo"] = mix(
        sweep(190, 60, 0.18, sine, a=0.001, r=0.12),
        noise(0.035, a=0.0005, r=0.03) * 0.7,
    )

    # Rising wobbly "wooo" while the palm charges.
    s["blast_charge"] = sweep(180, 480, 0.45, sine, a=0.03, r=0.12, vib=0.03, vib_rate=8, curve=1.4)

    # Boo-whoosh: a falling pitch with a breath of noise.
    fire_len = 0.36
    breath = smooth(noise(fire_len, a=0.01, r=0.15), 6) * 0.3
    s["blast_fire"] = mix(sweep(600, 150, fire_len, sine, a=0.01, r=0.15, vib=0.03, vib_rate=10), breath)

    # Ghost sheet flutter: a sine "ooo" that rises, then falls.
    s["sheet_blowoff"] = seq(
        sweep(330, 620, 0.22, sine, a=0.04, r=0.02, vib=0.04, vib_rate=6),
        sweep(620, 260, 0.34, sine, a=0.01, r=0.12, vib=0.04, vib_rate=6),
    )

    # Boing: a rubbery spring-like down-sweep with a fast wobble.
    s["visitor_bounce"] = sweep(560, 190, 0.16, sine, a=0.002, r=0.07, vib=0.18, vib_rate=28)

    # Quick "pon" for a flung prop.
    s["prop_pop"] = sweep(900, 400, 0.07, square, a=0.001, r=0.03)

    # Spooky descending minor phrase: da-da-da-DUM.
    s["game_over"] = seq(
        tone(392, 0.2, r=0.05, vib=0.01, vib_rate=5), gap(0.03),
        tone(311, 0.2, r=0.05, vib=0.01, vib_rate=5), gap(0.03),
        tone(294, 0.2, r=0.05, vib=0.01, vib_rate=5), gap(0.03),
        tone(262, 0.6, r=0.3, vib=0.02, vib_rate=5),
    )
    return s


def write_wav(path, x):
    pcm = np.clip(x, -1, 1)
    pcm = (pcm * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


os.makedirs(OUT, exist_ok=True)
only = set(sys.argv[2:])
for name, x in sounds().items():
    if only and name not in only:
        continue
    x = finish(x)
    write_wav(os.path.join(OUT, name + ".wav"), x)
    print(f"{name}: {len(x) / SR:.2f}s")
