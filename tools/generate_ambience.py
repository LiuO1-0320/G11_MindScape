"""Original synthetic ambience, not field recordings or therapeutic audio.
Run with Python + numpy. Periodic filtering produces seamless loop boundaries.
"""
from pathlib import Path
import wave
import numpy as np

root = Path(__file__).resolve().parents[1] / 'MindScape/Assets/Resources/MindScapeAmbience'
root.mkdir(parents=True, exist_ok=True)
rate, seconds = 22050, 24
n = rate * seconds
f = np.fft.rfftfreq(n, 1 / rate)
for i, (name, cutoff) in enumerate([('SoftRain', 2200), ('QuietStream', 900), ('GentleAir', 1400)]):
    rng = np.random.default_rng(5140 + i)
    spectrum = np.fft.rfft(rng.normal(size=n))
    filt = (f / (f + 65)) ** 2 / (1 + (f / cutoff) ** 4)
    signal = np.fft.irfft(spectrum * filt, n=n)
    signal *= .24 / max(np.max(np.abs(signal)), .001)
    pcm = (signal * 32767).astype('<i2')
    with wave.open(str(root / (name + '.wav')), 'wb') as out:
        out.setnchannels(1); out.setsampwidth(2); out.setframerate(rate); out.writeframes(pcm.tobytes())
    assert np.max(np.abs(signal)) <= .25
    assert abs(signal[0] - signal[-1]) < .1
    print(name, len(pcm), 'samples', 'peak', float(np.max(np.abs(signal))))
