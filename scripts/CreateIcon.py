"""Rebuild Forma's code-native geometric icon using only the Python standard library."""
import math
import struct
import zlib
from pathlib import Path

Root = Path(__file__).resolve().parents[1] / "src" / "Forma.App" / "Assets"
Root.mkdir(exist_ok=True)
Polygon = [(18, 14), (49, 14), (44, 24), (28, 24), (28, 30), (41, 30), (36, 40), (28, 40), (28, 51), (18, 51)]


def Inside(X, Y):
    Result = False
    Previous = Polygon[-1]
    for Current in Polygon:
        Ax, Ay = Previous
        Bx, By = Current
        if (Ay > Y) != (By > Y) and X < (Bx - Ax) * (Y - Ay) / (By - Ay) + Ax:
            Result = not Result
        Previous = Current
    return Result


def Png(Size):
    Data = bytearray()
    for Y in range(Size):
        Data.append(0)
        for X in range(Size):
            Sum = [0, 0, 0, 0]
            for Dy in range(4):
                for Dx in range(4):
                    Px, Py = (X + (Dx + .5) / 4) * 64 / Size, (Y + (Dy + .5) / 4) * 64 / Size
                    Outside = math.hypot(max(10 - Px, 0, Px - 54), max(10 - Py, 0, Py - 54)) > 8
                    Color = (0, 0, 0, 0) if Outside else (108, 211, 246, 255) if Inside(Px, Py) else (34, 36, 46, 255)
                    Sum = [A + B for A, B in zip(Sum, Color)]
            Data.extend(round(Value / 16) for Value in Sum)

    def Chunk(Name, Payload):
        return struct.pack(">I", len(Payload)) + Name + Payload + struct.pack(">I", zlib.crc32(Name + Payload))

    return b"\x89PNG\r\n\x1a\n" + Chunk(b"IHDR", struct.pack(">2I5B", Size, Size, 8, 6, 0, 0, 0)) + Chunk(b"IDAT", zlib.compress(Data, 9)) + Chunk(b"IEND", b"")


Sizes = [16, 24, 32, 48, 64, 128, 256]
Images = [Png(Size) for Size in Sizes]
Offset = 6 + 16 * len(Sizes)
Header = struct.pack("<3H", 0, 1, len(Sizes))
for Size, Image in zip(Sizes, Images):
    Header += struct.pack("<4B2H2I", Size % 256, Size % 256, 0, 0, 1, 32, len(Image), Offset)
    Offset += len(Image)
(Root / "Forma.ico").write_bytes(Header + b"".join(Images))
(Root / "Forma.png").write_bytes(Images[-1])
