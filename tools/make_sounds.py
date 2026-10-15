#!/usr/bin/env python3
"""Synthesises AVAPhoenix's sound effects and tunes into ../src/AVAPhoenix/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices. The title tune is the opening of Beethoven's Fur Elise (public domain); the rest are
new. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPhoenix", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ship's gun, and the birds'.
    write("Fire.wav", mix(tone(0.13, 2200, 380, "square", release=1.0), tone(0.05, 4000, 2000, "noise", release=1.0, gain=0.25)), 0.26)
    write("BirdFire.wav", tone(0.08, 700, 1300, "pulse", release=0.8), 0.18)
    # Birds hit: a small one pops; a great one squawks and bursts.
    write("FlitterHit.wav", mix(tone(0.22, 2600, 300, "noise", release=0.9), tone(0.18, 900, 2400, "tri", release=0.7, gain=0.6)), 0.38)
    write("EmberHit.wav", cat(mix(tone(0.25, 1100, 500, "saw", release=0.6, vibrato=0.1, vib_rate=30), tone(0.3, 3000, 600, "noise", release=0.8, gain=0.7)),
                              tone(0.35, 500, 90, "square", release=0.9, gain=0.7)), 0.45)
    write("WingChip.wav", cat(tone(0.03, 5000, 3000, "noise", release=0.8), tone(0.07, 1500, 2100, "tri", release=0.6)), 0.3)
    # The mothership: its hull thunks, its band pings, and when the Warden falls the whole thing goes up.
    write("HullChip.wav", mix(tone(0.07, 320, 140, "square", release=1.0), tone(0.06, 1200, 400, "noise", release=1.0, gain=0.6)), 0.3)
    write("BandChip.wav", tone(0.09, 2600, 2300, "tri", release=0.8), 0.26)
    write("WardenHit.wav", mix(tone(2.2, 4000, 60, "noise", release=0.6), tone(2.2, 160, 30, "saw", release=0.5, gain=0.8),
                               cat(*[tone(0.18, f, f * 0.5, "square", release=0.6, gain=0.5) for f in [900, 760, 640, 540, 450, 380, 320, 270]])), 0.55)
    write("Shield.wav", tone(1.4, 260, 900, "sine", attack=0.02, release=0.3, vibrato=0.06, vib_rate=24), 0.36)
    write("Hatch.wav", cat(tone(0.05, 4500, 2000, "noise", release=0.7), tone(0.3, 600, 1500, "saw", release=0.6, vibrato=0.12, vib_rate=26)), 0.3)
    # The dive: a long falling whistle.
    write("Swoop.wav", tone(0.55, 1900, 420, "tri", release=0.4, vibrato=0.03, vib_rate=14), 0.24)
    write("Death.wav", cat(mix(tone(0.7, 3500, 120, "noise", release=0.6), tone(0.7, 500, 50, "saw", release=0.6, gain=0.7)),
                           *[tone(0.15, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4", "E4"]]), 0.5)
    write("WaveStart.wav", tune(170, [("A4", 0.5), ("C5", 0.5), ("E5", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("A5", 1.5)],
                                [("A2", 1), ("E3", 1), ("A2", 1.5)]), 0.42)
    write("Clear.wav", tune(180, [("E5", 0.5), ("A5", 0.5), ("C6", 0.5), ("B5", 0.5), ("C6", 0.5), ("E6", 2)], [("A2", 1), ("E3", 1), ("A2", 2)]), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["A5", "C#6", "E6", "A6", "E6", "A6"]]), 0.4)

    # The mothership's throb: a low engine pulsing under a faint whine. Loops.
    n = int(RATE * 4)
    throb = []
    for i in range(n):
        t = i / RATE
        pulse = 0.55 + 0.45 * math.sin(2 * math.pi * 2 * t) ** 2
        throb.append(pulse * (math.sin(2 * math.pi * 55 * t) * 0.6 + math.sin(2 * math.pi * 110.5 * t) * 0.25)
                     + 0.06 * math.sin(2 * math.pi * (880 + 20 * math.sin(2 * math.pi * 0.5 * t)) * t))
    write("Music.wav", throb, 0.3)

    # The title: the opening of Fur Elise (Beethoven, public domain), on a music-box voice.
    lead = [("E5", 1), ("D#5", 1),
            ("E5", 1), ("D#5", 1), ("E5", 1), ("B4", 1), ("D5", 1), ("C5", 1),
            ("A4", 3), ("C4", 1), ("E4", 1), ("A4", 1),
            ("B4", 3), ("E4", 1), ("G#4", 1), ("B4", 1),
            ("C5", 3), ("E4", 1), ("E5", 1), ("D#5", 1),
            ("E5", 1), ("D#5", 1), ("E5", 1), ("B4", 1), ("D5", 1), ("C5", 1),
            ("A4", 3), ("C4", 1), ("E4", 1), ("A4", 1),
            ("B4", 3), ("E4", 1), ("C5", 1), ("B4", 1),
            ("A4", 6)]
    bass = [("-", 2), ("-", 6),
            ("A2", 1), ("E3", 1), ("A3", 1), ("-", 3),
            ("E2", 1), ("E3", 1), ("G#3", 1), ("-", 3),
            ("A2", 1), ("E3", 1), ("A3", 1), ("-", 3),
            ("-", 6),
            ("A2", 1), ("E3", 1), ("A3", 1), ("-", 3),
            ("E2", 1), ("E3", 1), ("G#3", 1), ("-", 3),
            ("A2", 6)]
    write("TitleTune.wav", cat(tune(220, lead, bass, kind="tri"), silence(1.0)), 0.45)
    write("StartTune.wav", tune(160, [("E4", 0.5), ("A4", 0.5), ("C5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 0.5), ("A5", 2)], [("A2", 1), ("E2", 1), ("A2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(76, [("E5", 1), ("C5", 1), ("B4", 1), ("A4", 1), ("G#4", 1), ("A4", 3)], [("A3", 2), ("F3", 2), ("E3", 2), ("A2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
