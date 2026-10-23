"""Build a particle-rich hardstyle stage from the proven hopstep donors.

The hopstep AVFX supplies the organic particles and looping emitter timing.
Its two animated model channels are replaced with filled DJ-stage geometry:
model 0 becomes a circular waveform/floor pulse, and model 1 becomes the
speaker/booth/equalizer assembly.  No wireframe-stage donor is used.
"""

from __future__ import annotations

import math
import struct
import sys
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
TOOLS = Path(r"D:\github\VFX Work\tools")
SOURCE = Path(r"C:\Users\scott\AppData\Local\Temp\stage-djsona-inspect\vfx\emote_sp\song_emt\eff")
OUTPUT = ROOT / "artifacts" / "Hardstyle Lightshow - Dynamic Candidate" / "vfx" / "stage" / "variants"
sys.path.insert(0, str(TOOLS))

from avfxtree import children, find, parse_file, serialize  # noqa: E402


DONORS = {
    "synthwave": SOURCE / "hopsteppurple.avfx",
    "metal": SOURCE / "hopstepred.avfx",
    "acid_techno": SOURCE / "hopstepgreen.avfx",
}


def half(value: float) -> int:
    return int(np.float16(value).view(np.uint16))


def vertex(position, alpha=255, colour=(1.0, 1.0, 1.0)) -> bytes:
    out = bytearray(36)
    for index, value in enumerate((*position, 1.0)):
        struct.pack_into("<H", out, index * 2, half(float(value)))
    out[8:12] = bytes((128, 128, 255, 255))
    out[12:16] = bytes((255, 128, 128, 255))
    rgb = tuple(max(0, min(255, round(channel * 255))) for channel in colour)
    out[16:20] = bytes((*rgb, alpha))
    for index in range(4):
        struct.pack_into("<HH", out, 20 + index * 4, half(0.5), half(0.5))
    return bytes(out)


class Mesh:
    def __init__(self):
        self.vertices: list[bytes] = []
        self.triangles: list[tuple[int, int, int]] = []

    def add_quad(self, a, b, c, d, alpha=255, colour=(1.0, 1.0, 1.0)):
        base = len(self.vertices)
        self.vertices.extend(vertex(p, alpha, colour) for p in (a, b, c, d))
        self.triangles.extend(((base, base + 1, base + 2), (base, base + 2, base + 3)))

    def add_disc(self, centre, radius, plane="xy", steps=32, alpha=255, colour=(1.0, 1.0, 1.0)):
        cx, cy, cz = centre
        centre_index = len(self.vertices)
        self.vertices.append(vertex(centre, alpha, colour))
        ring = []
        for index in range(steps):
            angle = 2 * math.pi * index / steps
            u, v = radius * math.cos(angle), radius * math.sin(angle)
            position = (cx + u, cy + v, cz) if plane == "xy" else (cx + u, cy, cz + v)
            ring.append(len(self.vertices))
            self.vertices.append(vertex(position, alpha, colour))
        for index in range(steps):
            self.triangles.append((centre_index, ring[index], ring[(index + 1) % steps]))

    def add_annulus(self, centre, inner, outer, plane="xz", steps=48, alpha=255, colour=(1.0, 1.0, 1.0)):
        cx, cy, cz = centre
        for index in range(steps):
            a0 = 2 * math.pi * index / steps
            a1 = 2 * math.pi * (index + 1) / steps
            if plane == "xz":
                points = (
                    (cx + inner * math.cos(a0), cy, cz + inner * math.sin(a0)),
                    (cx + outer * math.cos(a0), cy, cz + outer * math.sin(a0)),
                    (cx + outer * math.cos(a1), cy, cz + outer * math.sin(a1)),
                    (cx + inner * math.cos(a1), cy, cz + inner * math.sin(a1)),
                )
            else:
                points = (
                    (cx + inner * math.cos(a0), cy + inner * math.sin(a0), cz),
                    (cx + outer * math.cos(a0), cy + outer * math.sin(a0), cz),
                    (cx + outer * math.cos(a1), cy + outer * math.sin(a1), cz),
                    (cx + inner * math.cos(a1), cy + inner * math.sin(a1), cz),
                )
            self.add_quad(*points, alpha=alpha, colour=colour)

    def add_box(self, x0, x1, y0, y1, z0, z1, alpha=255, colour=(1.0, 1.0, 1.0)):
        self.add_quad((x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), alpha, colour)
        self.add_quad((x1, y0, z1), (x0, y0, z1), (x0, y1, z1), (x1, y1, z1), alpha, colour)
        self.add_quad((x0, y0, z1), (x0, y0, z0), (x0, y1, z0), (x0, y1, z1), alpha, colour)
        self.add_quad((x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0), alpha, colour)
        self.add_quad((x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1), alpha, colour)

    def bytes(self):
        if len(self.vertices) >= 32768:
            raise RuntimeError(f"Model exceeds signed AVFX index limit: {len(self.vertices)}")
        return (
            b"".join(self.vertices),
            b"".join(struct.pack("<3h", *triangle) for triangle in self.triangles),
            len(self.vertices),
            len(self.triangles),
        )


def circular_wave_and_floor() -> Mesh:
    mesh = Mesh()
    # Three filled floor bands. Existing donor particles expand these on beat.
    mesh.add_annulus((0, 0.000, 0), 0.24, 0.29, steps=64, alpha=170)
    mesh.add_annulus((0, 0.003, 0), 0.47, 0.53, steps=64, alpha=220)
    mesh.add_annulus((0, 0.006, 0), 0.76, 0.84, steps=72, alpha=150)

    # Alternating floor wedges produce motion without turning the scene into a grid.
    for index in range(16):
        a0 = 2 * math.pi * (index + 0.10) / 16
        a1 = 2 * math.pi * (index + 0.68) / 16
        r0, r1 = 0.32, 0.72
        mesh.add_quad(
            (r0 * math.cos(a0), 0.008, r0 * math.sin(a0)),
            (r1 * math.cos(a0), 0.008, r1 * math.sin(a0)),
            (r1 * math.cos(a1), 0.008, r1 * math.sin(a1)),
            (r0 * math.cos(a1), 0.008, r0 * math.sin(a1)),
            alpha=78,
        )

    # A 360-degree audio waveform fence. The donor's large Y multiplier turns
    # these compact values into a waist/chest-height pulse surrounding the DJ.
    steps = 96
    for index in range(steps):
        a0 = 2 * math.pi * index / steps
        a1 = 2 * math.pi * (index + 1) / steps
        beat0 = 0.018 + 0.018 * (0.5 + 0.5 * math.sin(index * 1.73))
        beat1 = 0.018 + 0.018 * (0.5 + 0.5 * math.sin((index + 1) * 1.73))
        radius = 0.61
        mesh.add_quad(
            (radius * math.cos(a0), -beat0, radius * math.sin(a0)),
            (radius * math.cos(a0), beat0, radius * math.sin(a0)),
            (radius * math.cos(a1), beat1, radius * math.sin(a1)),
            (radius * math.cos(a1), -beat1, radius * math.sin(a1)),
            alpha=235,
        )
    return mesh


def speakers_booth_equalizer() -> Mesh:
    mesh = Mesh()
    # Model particles halve X, so these coordinates land as two broad stacks.
    for side in (-1, 1):
        cx = 3.65 * side
        mesh.add_box(cx - 1.05, cx + 1.05, -0.72, 1.35, 0.08, 0.43, alpha=205)
        for cy, radius in ((-0.12, 0.58), (0.78, 0.48)):
            mesh.add_disc((cx, cy, 0.055), radius, "xy", 40, alpha=95)
            mesh.add_annulus((cx, cy, 0.045), radius * 0.58, radius, "xy", 40, alpha=255)
            mesh.add_disc((cx, cy, 0.035), radius * 0.25, "xy", 28, alpha=255)

    # Solid DJ booth and glowing deck lip.
    mesh.add_box(-2.42, 2.42, -0.68, 0.08, 0.02, 0.50, alpha=150)
    mesh.add_box(-2.58, 2.58, 0.08, 0.20, -0.02, 0.60, alpha=245)
    # Turntable platters face slightly upward/front and pulse with the assembly.
    for x in (-1.15, 1.15):
        mesh.add_disc((x, 0.24, 0.18), 0.62, "xy", 40, alpha=80)
        mesh.add_annulus((x, 0.24, 0.16), 0.32, 0.60, "xy", 40, alpha=245)

    # Filled equalizer panels—not wire boxes—with a deliberately irregular beat profile.
    heights = (0.42, 0.74, 1.12, 0.62, 1.36, 0.88, 0.50, 1.18, 0.68, 1.00, 0.58, 0.34)
    for index, height in enumerate(heights):
        x0 = -2.13 + index * 0.37
        x1 = x0 + 0.27
        mesh.add_quad((x0, -0.62, -0.005), (x1, -0.62, -0.005), (x1, -0.62 + height, -0.005), (x0, -0.62 + height, -0.005), alpha=225)
    return mesh


def disable_model_textures(particle) -> None:
    for component_name in ("TC1", "TC2", "TC3", "TC4", "TN", "TR", "TD", "TP"):
        for component in children(particle, component_name):
            for field in component.kids:
                if field.name == "bEna":
                    field.raw = struct.pack("<i", 0)
                elif field.name == "TLst":
                    field.raw = bytes((255,))
                elif field.name == "TxNo" and len(field.raw) == 4:
                    field.raw = struct.pack("<i", -1)
    try:
        find(particle, "CulT[0]").raw = struct.pack("<i", 0)
    except (IndexError, AttributeError):
        pass


def build_variant(name: str, donor: Path, floor_mesh: Mesh, stage_mesh: Mesh) -> dict:
    root = parse_file(str(donor))
    models = children(root, "Modl")
    floor_v, floor_i, floor_vertices, floor_triangles = floor_mesh.bytes()
    stage_v, stage_i, stage_vertices, stage_triangles = stage_mesh.bytes()
    find(models[0], "VDrw[0]").raw = floor_v
    find(models[0], "VIdx[0]").raw = floor_i
    find(models[1], "VDrw[0]").raw = stage_v
    find(models[1], "VIdx[0]").raw = stage_i

    # Only the model particles are made texture-free. All donor discs, powder,
    # decals, and light particles remain intact for the richer 1.0-style motion.
    model_particles = []
    for index, particle in enumerate(children(root, "Ptcl")):
        try:
            find(particle, "Data[0]/MdNo[0]")
        except (IndexError, AttributeError):
            continue
        disable_model_textures(particle)
        model_particles.append(index)

    OUTPUT.mkdir(parents=True, exist_ok=True)
    destination = OUTPUT / f"{name}.avfx"
    data = serialize(root)
    destination.write_bytes(data)
    reparsed = parse_file(data)
    if len(children(reparsed, "Emit")) != 10 or len(children(reparsed, "Ptcl")) != 21:
        raise RuntimeError("Donor emitter/particle graph changed unexpectedly")
    return {
        "file": str(destination),
        "bytes": len(data),
        "emitters": len(children(reparsed, "Emit")),
        "particles": len(children(reparsed, "Ptcl")),
        "model_particles": model_particles,
        "floor_vertices": floor_vertices,
        "floor_triangles": floor_triangles,
        "stage_vertices": stage_vertices,
        "stage_triangles": stage_triangles,
    }


def main() -> None:
    missing = [str(path) for path in DONORS.values() if not path.exists()]
    if missing:
        raise FileNotFoundError("Missing hopstep donors: " + ", ".join(missing))
    floor_mesh = circular_wave_and_floor()
    stage_mesh = speakers_booth_equalizer()
    for name, donor in DONORS.items():
        print(build_variant(name, donor, floor_mesh, stage_mesh))


if __name__ == "__main__":
    main()
