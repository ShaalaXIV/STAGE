"""Build the three self-contained Hardstyle Techno Lightshow AVFX variants.

The effect is deliberately texture-free.  All visible elements are embedded
neon geometry, so the Penumbra package needs only one AVFX per colour option.
"""

from __future__ import annotations

import math
import struct
import sys
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
VFX_TOOLS = Path(r"D:\github\VFX Work\tools")
TEMPLATE = Path(
    r"D:\github\VFX Work\Aly Synthwave VFX Overhaul\vfx\torii\torii_synthcycle_iconic.avfx"
)
OUTPUT = ROOT / "artifacts" / "Hardstyle Lightshow - Silent" / "vfx" / "stage" / "variants"
sys.path.insert(0, str(VFX_TOOLS))

from avfxtree import children, find, parse_file, serialize  # noqa: E402


I32 = lambda value: struct.pack("<i", value)


PALETTES = {
    "synthwave": {
        "period": 120,
        "stops": [
            (1.00, 0.02, 0.62),
            (0.40, 0.03, 1.00),
            (0.00, 0.85, 1.00),
            (1.00, 0.10, 0.78),
        ],
    },
    "metal": {
        "period": 96,
        "stops": [
            (1.00, 0.01, 0.01),
            (1.00, 0.32, 0.03),
            (1.00, 1.00, 1.00),
            (0.72, 0.00, 0.00),
        ],
    },
    "acid_techno": {
        "period": 105,
        "stops": [
            (0.36, 1.00, 0.00),
            (0.98, 1.00, 0.00),
            (0.05, 0.18, 0.02),
            (0.68, 0.02, 1.00),
        ],
    },
}


def half(value: float) -> int:
    return int(np.float16(value).view(np.uint16))


def pack_vertex(position, alpha: int) -> bytes:
    out = bytearray(36)
    for index, value in enumerate((*position, 1.0)):
        struct.pack_into("<H", out, index * 2, half(float(value)))
    out[8:12] = bytes((128, 128, 255, 255))
    out[12:16] = bytes((255, 128, 128, 255))
    out[16:20] = bytes((255, 255, 255, alpha))
    for index in range(4):
        struct.pack_into("<HH", out, 20 + index * 4, half(0.5), half(0.5))
    return bytes(out)


def key_colour(frame: int, colour: tuple[float, float, float]) -> bytes:
    return struct.pack("<hhfff", frame, 1, *colour)


def set_curve(curve, keys: list[bytes], repeat: bool) -> None:
    find(curve, "KeyC[0]").raw = I32(len(keys))
    find(curve, "Keys[0]").raw = b"".join(keys)
    find(curve, "BvPr[0]").raw = I32(1 if repeat else 0)
    find(curve, "BvPo[0]").raw = I32(1 if repeat else 0)


def set_rgb(parent, values, repeat=True) -> None:
    set_curve(find(parent, "Col[0]/RGB[0]"), [key_colour(t, c) for t, c in values], repeat)


def point(x, y, z):
    return (round(float(x), 5), round(float(y), 5), round(float(z), 5))


def add_line(lines: set, a, b) -> None:
    a, b = point(*a), point(*b)
    if a != b:
        lines.add(tuple(sorted((a, b))))


def add_polyline(lines: set, points, close=False) -> None:
    for a, b in zip(points, points[1:]):
        add_line(lines, a, b)
    if close and len(points) > 2:
        add_line(lines, points[-1], points[0])


def add_box(lines: set, x0, x1, y0, y1, z0, z1) -> None:
    for y in (y0, y1):
        add_polyline(lines, [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], True)
    for x in (x0, x1):
        for z in (z0, z1):
            add_line(lines, (x, y0, z), (x, y1, z))


def add_ring(lines: set, centre, radius, plane="xy", steps=32) -> None:
    cx, cy, cz = centre
    points = []
    for index in range(steps):
        angle = 2.0 * math.pi * index / steps
        u, v = radius * math.cos(angle), radius * math.sin(angle)
        if plane == "xy":
            points.append((cx + u, cy + v, cz))
        else:
            points.append((cx + u, cy, cz + v))
    add_polyline(lines, points, True)


def stage_segments() -> list[tuple]:
    lines: set = set()

    # Floor: a wide perspective grid centered on the dancer/DJ.
    for x in np.linspace(-5.0, 5.0, 11):
        add_line(lines, (x, 0.03, -3.4), (x, 0.03, 3.7))
    for z in np.linspace(-3.4, 3.7, 9):
        add_line(lines, (-5.0, 0.03, z), (5.0, 0.03, z))
    add_ring(lines, (0.0, 0.045, 0.0), 1.45, "xz", 48)
    add_ring(lines, (0.0, 0.05, 0.0), 2.20, "xz", 56)

    # Raised DJ booth behind the performer, with a slanted control surface.
    add_box(lines, -2.25, 2.25, 0.18, 1.28, 1.72, 2.34)
    add_line(lines, (-2.25, 1.28, 1.72), (-1.86, 1.62, 2.16))
    add_line(lines, (2.25, 1.28, 1.72), (1.86, 1.62, 2.16))
    add_line(lines, (-1.86, 1.62, 2.16), (1.86, 1.62, 2.16))
    add_line(lines, (-2.25, 1.28, 1.72), (2.25, 1.28, 1.72))

    # Twin turntables and mixer lines on the deck.
    for x in (-0.98, 0.98):
        add_ring(lines, (x, 1.48, 1.98), 0.52, "xz", 36)
        add_ring(lines, (x, 1.49, 1.98), 0.25, "xz", 24)
        add_line(lines, (x, 1.50, 1.98), (x + 0.32, 1.50, 2.18))
    for x in np.linspace(-0.42, 0.42, 5):
        add_line(lines, (x, 1.35, 1.83), (x, 1.56, 2.12))

    # Equalizer facade: varied bar heights read as a hardstyle beat display.
    heights = (0.44, 0.72, 0.98, 0.62, 1.08, 0.78, 0.48, 0.88, 0.58, 1.02, 0.70, 0.40)
    width = 0.25
    for index, height in enumerate(heights):
        x = -1.78 + index * 0.32
        add_box(lines, x, x + width, 0.30, 0.30 + height, 1.67, 1.72)

    # Speaker stacks: two woofers per side with concentric pulse rings.
    for side in (-1, 1):
        cx = 3.12 * side
        add_box(lines, cx - 0.72, cx + 0.72, 0.18, 2.76, 1.82, 2.42)
        for cy, radius in ((0.88, 0.55), (2.02, 0.49)):
            add_ring(lines, (cx, cy, 1.80), radius, "xy", 40)
            add_ring(lines, (cx, cy, 1.79), radius * 0.56, "xy", 32)
            for spoke in range(8):
                angle = 2 * math.pi * spoke / 8
                add_line(
                    lines,
                    (cx + radius * 0.56 * math.cos(angle), cy + radius * 0.56 * math.sin(angle), 1.78),
                    (cx + radius * math.cos(angle), cy + radius * math.sin(angle), 1.78),
                )

    # Overhead waveform crown.
    wave = []
    for index in range(41):
        x = -4.4 + index * 0.22
        envelope = 0.35 + 0.42 * (1.0 - abs(x) / 4.4)
        y = 3.45 + math.sin(index * 1.28) * envelope
        wave.append((x, y, 2.26))
    add_polyline(lines, wave)
    add_line(lines, (-4.55, 3.45, 2.26), (4.55, 3.45, 2.26))

    # Symmetrical laser fan, kept high enough not to obscure the character.
    origins = [(-1.75, 2.76, 2.28), (1.75, 2.76, 2.28)]
    targets = [
        (-5.7, 5.25, -2.8), (-4.7, 4.35, -1.0), (-4.1, 5.65, 1.0),
        (5.7, 5.25, -2.8), (4.7, 4.35, -1.0), (4.1, 5.65, 1.0),
    ]
    for origin in origins:
        for target in targets:
            if origin[0] * target[0] > 0:
                add_line(lines, origin, target)

    # Central hardstyle diamond logo above the booth.
    add_polyline(lines, [(0, 4.65, 2.22), (0.62, 4.02, 2.22), (0, 3.58, 2.22), (-0.62, 4.02, 2.22)], True)
    add_polyline(lines, [(0, 4.43, 2.21), (0.34, 4.03, 2.21), (0, 3.79, 2.21), (-0.34, 4.03, 2.21)], True)

    return sorted(lines)


def wire_geometry(segments, core_radius=0.014, halo_radius=0.048):
    vertices: list[bytes] = []
    triangles: list[tuple[int, int, int]] = []

    def ribbon(a, b, perpendicular, radius, alpha):
        base = len(vertices)
        pa, pb = np.asarray(a, dtype=np.float64), np.asarray(b, dtype=np.float64)
        offset = perpendicular * radius
        for position in (pa - offset, pa + offset, pb + offset, pb - offset):
            vertices.append(pack_vertex(position, alpha))
        triangles.extend(((base, base + 1, base + 2), (base, base + 2, base + 3)))

    for a, b in segments:
        pa, pb = np.asarray(a, dtype=np.float64), np.asarray(b, dtype=np.float64)
        direction = pb - pa
        length = np.linalg.norm(direction)
        if length < 1e-7:
            continue
        direction /= length
        reference = np.asarray((0.0, 0.0, 1.0) if abs(direction[2]) < 0.84 else (1.0, 0.0, 0.0))
        first = np.cross(direction, reference)
        first /= np.linalg.norm(first)
        second = np.cross(direction, first)
        second /= np.linalg.norm(second)
        for perpendicular in (first, second):
            ribbon(a, b, perpendicular, halo_radius, 22)
            ribbon(a, b, perpendicular, core_radius, 255)

    if len(vertices) >= 32768:
        raise RuntimeError(f"Stage exceeds AVFX signed-index limit: {len(vertices)} vertices")
    return (
        b"".join(vertices),
        b"".join(struct.pack("<3h", *triangle) for triangle in triangles),
        len(vertices),
        len(triangles),
    )


def build_variant(name: str, settings: dict, segments) -> dict:
    root = parse_file(str(TEMPLATE))
    model = children(root, "Modl")[0]
    vertex_data, index_data, vertex_count, triangle_count = wire_geometry(segments)
    find(model, "VDrw[0]").raw = vertex_data
    find(model, "VIdx[0]").raw = index_data

    particle = children(root, "Ptcl")[0]
    find(particle, "RMT[0]").raw = I32(2)
    find(particle, "CulT[0]").raw = I32(0)
    find(particle, "DsDw[0]").raw = I32(0)
    texture = find(particle, "TC1[0]")
    find(texture, "bEna[0]").raw = I32(0)
    find(texture, "TLst[0]").raw = bytes((255,))

    period = settings["period"]
    stops = settings["stops"]
    values = []
    # Hardstyle pulse: a hot hit just after every palette stop, then settle.
    for index, colour in enumerate(stops):
        frame = round(index * period / len(stops))
        values.append((frame, tuple(channel * 0.82 for channel in colour)))
        values.append((min(period, frame + 4), tuple(channel * 1.34 for channel in colour)))
    values.append((period, tuple(channel * 0.82 for channel in stops[0])))
    set_rgb(particle, values, True)

    OUTPUT.mkdir(parents=True, exist_ok=True)
    destination = OUTPUT / f"{name}.avfx"
    data = serialize(root)
    destination.write_bytes(data)
    # A complete parse after serialization catches malformed nested sizes.
    parse_file(data)
    return {
        "file": str(destination),
        "bytes": len(data),
        "vertices": vertex_count,
        "triangles": triangle_count,
    }


def main() -> None:
    if not TEMPLATE.exists():
        raise FileNotFoundError(TEMPLATE)
    segments = stage_segments()
    print(f"stage_segments={len(segments)}")
    for name, settings in PALETTES.items():
        print(build_variant(name, settings, segments))


if __name__ == "__main__":
    main()
