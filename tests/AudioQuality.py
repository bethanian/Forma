"""Measure the new presets on original stereo music fixtures, mono, and silence."""
import contextlib
import io
import json
import sys
from pathlib import Path

import numpy as np
from pedalboard.io import AudioFile

Root, PresetsPath, WorkerPath = map(Path, sys.argv[1:])
sys.path.insert(0, str(WorkerPath.parent))
from dreamy_worker import Process

Root.mkdir(exist_ok=True)
Presets = json.loads(PresetsPath.read_text(encoding="utf-8-sig"))
SampleRate = 48000
Time = np.arange(SampleRate * 8) / SampleRate
Music = np.zeros_like(Time)
for Index, Notes in enumerate(((220, 261.626, 329.628), (174.614, 220, 261.626), (130.813, 164.814, 195.998), (146.832, 174.614, 220))):
    Local = Time - Index * 2
    Envelope = np.where((Local >= 0) & (Local < 2), np.maximum(0, 1 - np.exp(-np.maximum(Local, 0) * 25)) * np.exp(-np.maximum(Local, 0) * .8), 0)
    for Frequency in Notes:
        Music += Envelope * (np.sin(2 * np.pi * Frequency * Time) + .25 * np.sin(4 * np.pi * Frequency * Time)) * .07
    Music += Envelope * np.sin(2 * np.pi * Notes[0] / 4 * Time) * .12
Music += .025 * np.sin(2 * np.pi * 7500 * Time) + .015 * np.sin(2 * np.pi * 9200 * Time)
Side = .035 * np.sin(2 * np.pi * 390 * Time) * np.sin(2 * np.pi * .6 * Time)
Source = np.stack((Music + Side, Music - Side)).astype(np.float32)
Source *= .65 / np.max(np.abs(Source))
InputPath = Root / "Original.wav"
with AudioFile(str(InputPath), "w", SampleRate, 2, bit_depth=24) as Sink:
    Sink.write(Source)


def Band(Audio, Low, High):
    Spectrum = np.abs(np.fft.rfft(Audio.mean(axis=0))) ** 2
    Frequencies = np.fft.rfftfreq(Audio.shape[1], 1 / SampleRate)
    return float(Spectrum[(Frequencies >= Low) & (Frequencies < High)].sum())


Results = {}
for Name in ("Velvet Dream", "Moonlit Cinema", "Cassette Sunset", "Underwater", "Midnight Drive", "Starlight"):
    Options = Presets[Name]
    Output = Root / (Name.replace(" ", "") + ".wav")
    with contextlib.redirect_stdout(io.StringIO()):
        Process(str(InputPath), str(Output), Options)
    with AudioFile(str(Output)) as File:
        Audio = File.read(File.frames)
    Expected = 8 / Options["Speed"] + Options["TailSeconds"]
    Peak = float(np.max(np.abs(Audio)))
    Correlation = float(np.corrcoef(Audio)[0, 1])
    assert np.isfinite(Audio).all() and .7 < Peak < .9, (Name, "headroom", Peak)
    assert abs(Audio.shape[1] / SampleRate - Expected) < .003, (Name, "timing")
    assert not Audio[:, 0].any() and not Audio[:, -1].any(), (Name, "edge fades")
    assert Correlation > .2, (Name, "mono compatibility", Correlation)
    assert float(np.sqrt(np.mean(Audio[:, -int(SampleRate * Options["TailSeconds"]):] ** 2))) > .00001, (Name, "audible tail")
    BoundaryJumps = [float(np.max(np.abs(Audio[:, Round] - Audio[:, Round - 1]))) for Round in (round((65536 * Index - 1) / Options["Speed"]) for Index in range(1, 6)) if Round < Audio.shape[1]]
    assert max(BoundaryJumps) < .2, (Name, "streaming discontinuity")
    Results[Name] = {"Peak": Peak, "Duration": Audio.shape[1] / SampleRate, "StereoCorrelation": Correlation, "MaxBoundaryJump": max(BoundaryJumps)}
    if Name == "Underwater":
        assert Band(Audio, 5000, 14000) / Band(Audio, 500, 1500) < .2 * Band(Source, 5000, 14000) / Band(Source, 500, 1500), "Underwater must reduce high frequencies"
    if Name == "Midnight Drive":
        assert Band(Audio, 40, 180) / Band(Audio, 500, 3000) > 1.15 * Band(Source, 40, 180) / Band(Source, 500, 3000), "Midnight Drive must increase bass balance"

Silence = Root / "Silence.wav"
with AudioFile(str(Silence), "w", SampleRate, 1, bit_depth=24) as Sink:
    Sink.write(np.zeros((1, SampleRate // 2), dtype=np.float32))
with contextlib.redirect_stdout(io.StringIO()):
    Process(str(Silence), str(Root / "SilentEffect.wav"), Presets["Velvet Dream"])
with AudioFile(str(Root / "SilentEffect.wav")) as File:
    Silent = File.read(File.frames)
assert np.isfinite(Silent).all() and not Silent.any(), "Silence must remain silent"
(Root / "measurements.json").write_text(json.dumps(Results, indent=2), encoding="utf-8")
print("Six stereo music fixtures: headroom, timing, tails, fades, correlation and block boundaries passed; filter/bass responses and mono silence passed.")
