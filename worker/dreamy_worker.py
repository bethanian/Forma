"""Forma Dreamy worker. Original code MIT; requires separately installed GPLv3 Pedalboard."""
import json
import math
import sys
from pathlib import Path

import numpy as np
from pedalboard import Chorus, Delay, HighpassFilter, LowpassFilter, LowShelfFilter, Pedalboard, Reverb
from pedalboard.io import AudioFile


def Process(Input, Output, Options):
    Speed, Wet, Room, Damping, Lowpass, Width, Mix = (
        float(Options[Key]) for Key in ("Speed", "Wet", "Room", "Damping", "Lowpass", "Width", "Chorus")
    )
    Highpass, DelayMs, DelayMix, Warmth, BassDb, ChorusRate = (
        float(Options.get(Key, Default)) for Key, Default in (
            ("Highpass", 40), ("DelayMs", 0), ("DelayMix", 0), ("Warmth", 0), ("BassDb", 0), ("ChorusRate", .5))
    )
    TailSeconds = float(Options.get("TailSeconds", 0))
    Bounds = ((Speed, .7, 1.25), (Wet, 0, 1), (Room, 0, 1), (Damping, 0, 1),
              (Lowpass, 500, 20000), (Width, .5, 2.5), (Mix, 0, 1), (Highpass, 20, 800),
              (DelayMs, 0, 600), (DelayMix, 0, .3), (Warmth, 0, .6), (BassDb, -6, 6), (ChorusRate, .1, 2), (TailSeconds, 0, 4))
    if any(not math.isfinite(Value) or not Minimum <= Value <= Maximum for Value, Minimum, Maximum in Bounds):
        raise ValueError("Effect parameters outside supported range")
    if Highpass >= Lowpass:
        raise ValueError("High-pass must be lower than low-pass")
    Temporary = Path(Output).with_suffix(".float.wav")
    Plugins = [HighpassFilter(cutoff_frequency_hz=Highpass), LowpassFilter(cutoff_frequency_hz=Lowpass)]
    if BassDb:
        Plugins.append(LowShelfFilter(cutoff_frequency_hz=180, gain_db=BassDb))
    Plugins.append(Chorus(rate_hz=ChorusRate, depth=Mix * .33, mix=Mix))
    if DelayMs and DelayMix:
        Plugins.append(Delay(delay_seconds=DelayMs / 1000, feedback=.16, mix=DelayMix))
    Plugins.append(Reverb(room_size=Room, damping=Damping, wet_level=Wet, dry_level=1 - Wet, width=1))
    Board = Pedalboard(Plugins)
    Peak, Offset, NextPosition, Previous = 0., 0, 0., None
    try:
        with AudioFile(Input) as Source:
            SampleRate, Total = Source.samplerate, Source.frames
            if Total == 0:
                raise ValueError("Empty audio input")
            with AudioFile(str(Temporary), "w", SampleRate, 2, bit_depth=32) as Sink:
                while Source.tell() < Total:
                    Block = Source.read(65536).astype(np.float32)
                    if Block.shape[0] == 1:
                        Block = np.repeat(Block, 2, axis=0)
                    Block = Block[:2]
                    Count = Block.shape[1]
                    Start = Offset
                    if Previous is not None:
                        Block = np.concatenate((Previous, Block), axis=1)
                        Start -= 1
                    Previous = Block[:, -1:]
                    End = Offset + Count - 1
                    Positions = np.arange(NextPosition, End, Speed)
                    if Positions.size:
                        NextPosition = float(Positions[-1] + Speed)
                        Coordinates = np.arange(Start, End + 1)
                        Slowed = np.stack([np.interp(Positions, Coordinates, Channel) for Channel in Block]).astype(np.float32)
                        if Warmth:
                            Slowed = Slowed * (1 - Warmth) + np.tanh(Slowed * 2) / np.tanh(2) * Warmth
                        Mid, Side = (Slowed[0] + Slowed[1]) * .5, (Slowed[0] - Slowed[1]) * .5 * Width
                        Audio = Board(np.stack((Mid + Side, Mid - Side)), SampleRate, reset=False)
                        if not np.isfinite(Audio).all():
                            raise ValueError("Audio processor produced nonfinite samples")
                        Peak = max(Peak, float(np.max(np.abs(Audio))))
                        Sink.write(Audio)
                    Offset += Count
                    print(f"PROGRESS:{Offset / Total * 80:.3f}", flush=True)
                Remaining = int(TailSeconds * SampleRate)
                while Remaining:
                    Count = min(65536, Remaining)
                    Audio = Board(np.zeros((2, Count), dtype=np.float32), SampleRate, reset=False)
                    if not np.isfinite(Audio).all():
                        raise ValueError("Reverb tail produced nonfinite samples")
                    Peak = max(Peak, float(np.max(np.abs(Audio))))
                    Sink.write(Audio)
                    Remaining -= Count
            Gain = 10 ** (-1 / 20) / Peak if Peak else 1.
            with AudioFile(str(Temporary)) as Processed, AudioFile(Output, "w", SampleRate, 2, bit_depth=24) as Sink:
                while Processed.tell() < Processed.frames:
                    Start = Processed.tell()
                    Audio = Processed.read(65536) * Gain
                    Positions = np.arange(Start, Start + Audio.shape[1])
                    FadeIn = np.minimum(1, Positions / max(1, SampleRate * .005))
                    FadeOut = np.minimum(1, (Processed.frames - 1 - Positions) / max(1, SampleRate * (.25 if TailSeconds else .015)))
                    Sink.write(Audio * FadeIn * FadeOut)
                    print(f"PROGRESS:{80 + Processed.tell() / Processed.frames * 20:.3f}", flush=True)
    finally:
        Temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    try:
        Process(sys.argv[1], sys.argv[2], json.loads(Path(sys.argv[3]).read_text(encoding="utf-8-sig")))
    except Exception as Error:
        print(f"Dreamy: {Error}", file=sys.stderr)
        sys.exit(1)
