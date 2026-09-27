#!/usr/bin/env python3
"""Make the lightweight test samples ImmersiveX Media is tested with, one or more per supported format.

    python3 tools/samples/make_media_samples.py            # writes Assets/StreamingAssets/ImmersiveXSamples/

Everything is generated here (no downloaded content, so no licence questions), and kept small (a few MB in total).

The splat, point and mesh samples show the same asymmetric test figure, so a decoding or axis mistake is obvious:
a red head on top, blue feet at the bottom, a GREEN arm on the viewer's LEFT, a YELLOW arm on the right, and an
orange nose pointing AT the viewer. Each format stores it in that format's own convention:
  - 3DGS .ply, compressed .ply, .splat, .ksplat, GenXR streams: x right, y DOWN, z forward (away from the camera)
  - .spz: x right, y UP, z back (towards the camera)
  - point-cloud and mesh .ply / .obj: x right, y UP, z towards the viewer (right-handed, y up)

Needs numpy, Pillow, ffmpeg (videos) and zstandard (SPZ v4).
"""
import gzip
import io
import json
import math
import os
import shutil
import struct
import subprocess
import sys

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "StreamingAssets", "ImmersiveXSamples")
C0 = 0.28209479177387814
RNG = np.random.default_rng(7)


# ---------------------------------------------------------------- the test figure (y up, z towards the viewer)

def figure(t=0.0, density=1.0):
    """Gaussians of the figure as (positions, scales, rotations wxyz, colours rgb 0-1, opacity) in a y-up frame where
    +z points towards the viewer. t (seconds) waves the yellow arm. The same seed every call, so only the arm moves
    between frames."""
    rng = np.random.default_rng(11)
    parts = []

    def blob(centre, radii, colour, count, stretch=(1, 1, 1)):
        n = max(8, int(count * density))
        d = rng.normal(size=(n, 3))
        d /= np.linalg.norm(d, axis=1, keepdims=True)
        r = rng.uniform(0.75, 1.0, size=(n, 1)) ** (1 / 3)
        p = np.asarray(centre) + d * r * np.asarray(radii)
        s = np.tile(np.asarray(stretch, dtype=float) * min(radii) * 0.22, (n, 1))
        parts.append((p, s, np.tile([1.0, 0, 0, 0], (n, 1)), np.tile(colour, (n, 1))))

    def limb(a, b, radius, colour, count):
        n = max(8, int(count * density))
        a, b = np.asarray(a, float), np.asarray(b, float)
        u = rng.uniform(0, 1, size=(n, 1))
        axis = b - a
        length = np.linalg.norm(axis)
        axis /= length
        ortho = np.cross(axis, [0, 0, 1] if abs(axis[2]) < 0.9 else [1, 0, 0])
        ortho /= np.linalg.norm(ortho)
        ortho2 = np.cross(axis, ortho)
        ang = rng.uniform(0, 2 * math.pi, size=(n, 1))
        p = a + u * (b - a) + radius * (np.cos(ang) * ortho + np.sin(ang) * ortho2)
        # Elongated along the limb: rotate the x axis onto the limb direction.
        q = rotation_between([1, 0, 0], axis)
        s = np.tile([length / 12, radius * 0.35, radius * 0.35], (n, 1))
        parts.append((p, s, np.tile(q, (n, 1)), np.tile(colour, (n, 1))))

    red, white, blue = [0.9, 0.1, 0.1], [0.92, 0.92, 0.92], [0.15, 0.3, 0.95]
    green, yellow, orange = [0.1, 0.85, 0.2], [0.95, 0.85, 0.1], [1.0, 0.5, 0.05]
    blob([0, 1.52, 0], [0.12, 0.13, 0.12], red, 1800)                   # head
    blob([0, 1.12, 0], [0.2, 0.3, 0.12], white, 3000)                   # body
    limb([-0.08, 0.85, 0], [-0.12, 0.05, 0.02], 0.06, blue, 1500)       # legs
    limb([0.08, 0.85, 0], [0.12, 0.05, 0.02], 0.06, blue, 1500)
    blob([-0.13, 0.03, 0.06], [0.06, 0.03, 0.1], blue, 400)             # feet
    blob([0.13, 0.03, 0.06], [0.06, 0.03, 0.1], blue, 400)
    limb([-0.22, 1.35, 0], [-0.62, 1.25, 0], 0.045, green, 1200)        # viewer's LEFT arm: green, straight out
    wave = math.sin(t * 2 * math.pi * 1.0) * 0.35
    limb([0.22, 1.35, 0], [0.55, 1.62 + wave, 0], 0.045, yellow, 1200)  # right arm: yellow, waving
    limb([0, 1.52, 0.1], [0, 1.5, 0.3], 0.025, orange, 500)             # nose, towards the viewer (+z)

    p = np.concatenate([x[0] for x in parts])
    s = np.concatenate([x[1] for x in parts])
    q = np.concatenate([x[2] for x in parts])
    c = np.concatenate([x[3] for x in parts])
    c = np.clip(c + rng.normal(scale=0.03, size=c.shape), 0, 1)
    o = np.full(len(p), 0.92)
    return p.astype(np.float32), s.astype(np.float32), q.astype(np.float32), c.astype(np.float32), o.astype(np.float32)


def rotation_between(a, b):
    a = np.asarray(a, float) / np.linalg.norm(a)
    b = np.asarray(b, float) / np.linalg.norm(b)
    v = np.cross(a, b)
    w = 1.0 + np.dot(a, b)
    if w < 1e-8:
        return np.array([0.0, 0.0, 1.0, 0.0])
    q = np.array([w, v[0], v[1], v[2]])
    return q / np.linalg.norm(q)


def quat_mul(a, b):
    aw, ax, ay, az = a.T if a.ndim > 1 else a
    bw, bx, by, bz = b.T
    return np.stack([aw * bw - ax * bx - ay * by - az * bz,
                     aw * bx + ax * bw + ay * bz - az * by,
                     aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw], axis=1)


def to_rdf(p, q):
    """Viewer frame (y up, z towards the viewer) → RDF (x right, y down, z away from the camera): 180° about X."""
    turn = np.array([0.0, 1.0, 0.0, 0.0])
    return p * np.array([1, -1, -1], np.float32), quat_mul(turn, q).astype(np.float32)


def to_rub(p, q):
    """Viewer frame → RUB (x right, y up, z back = towards the viewer): the same frame."""
    return p.copy(), q.copy()


def logit(o):
    o = np.clip(o, 1e-4, 1 - 1e-4)
    return np.log(o / (1 - o))


# ---------------------------------------------------------------- splat writers

def write_inria_ply(path, p, s, q, c, o):
    n = len(p)
    header = ("ply\nformat binary_little_endian 1.0\n"
              f"element vertex {n}\n" +
              "".join(f"property float {name}\n" for name in
                      ["x", "y", "z", "nx", "ny", "nz", "f_dc_0", "f_dc_1", "f_dc_2", "opacity",
                       "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3"]) +
              "end_header\n")
    data = np.zeros((n, 17), np.float32)
    data[:, 0:3] = p
    data[:, 6:9] = (c - 0.5) / C0
    data[:, 9] = logit(o)
    data[:, 10:13] = np.log(s)
    data[:, 13:17] = q * RNG.uniform(0.8, 1.2, size=(n, 1))  # 3DGS stores unnormalised quaternions
    with open(path, "wb") as f:
        f.write(header.encode("ascii"))
        f.write(data.astype("<f4").tobytes())


def write_splat(path, p, s, q, c, o):
    rec = np.zeros(len(p), dtype=[("p", "<f4", 3), ("s", "<f4", 3), ("c", "u1", 4), ("r", "u1", 4)])
    rec["p"] = p
    rec["s"] = s
    rec["c"][:, :3] = np.clip(np.round(c * 255), 0, 255)
    rec["c"][:, 3] = np.clip(np.round(o * 255), 0, 255)
    qn = q / np.linalg.norm(q, axis=1, keepdims=True)
    rec["r"] = np.clip(np.round(qn * 128 + 128), 0, 255)
    with open(path, "wb") as f:
        f.write(rec.tobytes())


def write_compressed_ply(path, p, s, q, c, o):
    """PlayCanvas compressed PLY: chunks of 256 with 18 floats, packed position/rotation/scale/colour."""
    n = len(p)
    chunks = (n + 255) // 256
    chunk_rows = np.zeros((chunks, 18), np.float32)
    packed = np.zeros((n, 4), np.uint32)
    log_s = np.log(s)
    for k in range(chunks):
        idx = slice(k * 256, min(n, (k + 1) * 256))
        pm, px = p[idx].min(0), p[idx].max(0)
        sm, sx = log_s[idx].min(0), log_s[idx].max(0)
        cm, cx = c[idx].min(0), c[idx].max(0)
        chunk_rows[k] = np.concatenate([pm, px, sm, sx, cm, cx])

        def pack111011(v, lo, hi):
            t = np.clip((v - lo) / np.maximum(hi - lo, 1e-9), 0, 1)
            x = np.round(t[:, 0] * 2047).astype(np.uint32)
            y = np.round(t[:, 1] * 1023).astype(np.uint32)
            z = np.round(t[:, 2] * 2047).astype(np.uint32)
            return (x << 21) | (y << 11) | z

        packed[idx, 0] = pack111011(p[idx], pm, px)
        packed[idx, 2] = pack111011(log_s[idx], sm, sx)
        tc = np.clip((c[idx] - cm) / np.maximum(cx - cm, 1e-9), 0, 1)
        rgb = np.round(tc * 255).astype(np.uint32)
        a = np.round(np.clip(o[idx], 0, 1) * 255).astype(np.uint32)
        packed[idx, 3] = (rgb[:, 0] << 24) | (rgb[:, 1] << 16) | (rgb[:, 2] << 8) | a
        qn = q[idx] / np.linalg.norm(q[idx], axis=1, keepdims=True)
        largest = np.argmax(np.abs(qn), axis=1)
        qn = qn * np.where(qn[np.arange(len(qn)), largest] < 0, -1, 1)[:, None]
        words = np.zeros(len(qn), np.uint32)
        for i, (row, big) in enumerate(zip(qn, largest)):
            rest = [row[j] for j in range(4) if j != big]
            v = [int(round((r / math.sqrt(2) + 0.5) * 1023)) for r in rest]
            v = [min(1023, max(0, x)) for x in v]
            words[i] = (int(big) << 30) | (v[0] << 20) | (v[1] << 10) | v[2]
        packed[idx, 1] = words
    header = ("ply\nformat binary_little_endian 1.0\n"
              f"element chunk {chunks}\n" +
              "".join(f"property float {name}\n" for name in
                      ["min_x", "min_y", "min_z", "max_x", "max_y", "max_z",
                       "min_scale_x", "min_scale_y", "min_scale_z", "max_scale_x", "max_scale_y", "max_scale_z",
                       "min_r", "min_g", "min_b", "max_r", "max_g", "max_b"]) +
              f"element vertex {n}\n"
              "property uint packed_position\nproperty uint packed_rotation\nproperty uint packed_scale\nproperty uint packed_color\n"
              "end_header\n")
    with open(path, "wb") as f:
        f.write(header.encode("ascii"))
        f.write(chunk_rows.astype("<f4").tobytes())
        f.write(packed.astype("<u4").tobytes())


def spz_blocks(p, s, q, c, o, version, fractional_bits=12):
    n = len(p)
    fixed = np.round(p * (1 << fractional_bits)).astype(np.int64)
    fixed &= 0xFFFFFF
    positions = np.zeros((n, 3, 3), np.uint8)
    for b in range(3):
        positions[:, :, b] = (fixed >> (8 * b)) & 0xFF
    alphas = np.clip(np.round(o * 255), 0, 255).astype(np.uint8)
    dc = (c - 0.5) / C0
    colours = np.clip(np.round(dc * 0.15 * 255 + 127.5), 0, 255).astype(np.uint8)
    scales = np.clip(np.round((np.log(s) + 10) * 16), 0, 255).astype(np.uint8)
    qn = q / np.linalg.norm(q, axis=1, keepdims=True)
    xyzw = np.stack([qn[:, 1], qn[:, 2], qn[:, 3], qn[:, 0]], axis=1)
    if version >= 3:
        words = np.zeros(n, np.uint32)
        for i, row in enumerate(xyzw):
            big = int(np.argmax(np.abs(row)))
            if row[big] < 0:
                row = -row
            word = big << 30
            slot = 0
            for j in range(4):
                if j == big:
                    continue
                m = min(511, int(round(abs(row[j]) / math.sqrt(0.5) * 511)))
                word |= ((512 if row[j] < 0 else 0) | m) << (20 - 10 * slot)
                slot += 1
            words[i] = word
        rotations = words.astype("<u4").tobytes()
    else:
        xyzw = xyzw * np.where(xyzw[:, 3:4] < 0, -1, 1)
        rotations = np.clip(np.round(xyzw[:, :3] * 127.5 + 127.5), 0, 255).astype(np.uint8).tobytes()
    return [positions.tobytes(), alphas.tobytes(), colours.tobytes(), scales.tobytes(), rotations]


def write_spz_legacy(path, p, s, q, c, o, version):
    header = struct.pack("<IIIBBBB", 0x5053474E, version, len(p), 0, 12, 0, 0)
    with open(path, "wb") as f:
        f.write(gzip.compress(header + b"".join(spz_blocks(p, s, q, c, o, version))))


def write_spz_v4(path, p, s, q, c, o):
    import zstandard
    blocks = spz_blocks(p, s, q, c, o, 4)
    frames = [zstandard.ZstdCompressor(level=12).compress(b) for b in blocks]
    toc_offset = 32
    header = struct.pack("<IIIBBBBI", 0x5053474E, 4, len(p), 0, 12, 0, len(frames), toc_offset) + bytes(12)
    toc = b"".join(struct.pack("<QQ", len(fr), len(b)) for fr, b in zip(frames, blocks))
    with open(path, "wb") as f:
        f.write(header + toc + b"".join(frames))


def write_ksplat(path, p, s, q, c, o, level):
    """GaussianSplats3D .ksplat 0.1, one section. Level 0: float32; level 1: uint16 positions in 5 m buckets + float16."""
    n = len(p)
    qn = q / np.linalg.norm(q, axis=1, keepdims=True)
    colour = np.zeros((n, 4), np.uint8)
    colour[:, :3] = np.clip(np.floor(c * 255), 0, 255)
    colour[:, 3] = np.clip(np.floor(o * 255), 0, 255)
    bucket_size, block = 256, 5.0
    if level == 0:
        rec = np.zeros(n, dtype=[("p", "<f4", 3), ("s", "<f4", 3), ("r", "<f4", 4), ("c", "u1", 4)])
        rec["p"], rec["s"], rec["r"], rec["c"] = p, s, qn, colour
        records, centres, partial, full, bucket_count, stride = rec.tobytes(), b"", b"", 0, 0, 44
        order = np.arange(n)
    else:
        # Bucket by 5 m cells anchored at the minimum corner; full buckets of 256 first, then partial ones.
        cell = np.floor((p - p.min(0)) / block).astype(int)
        keys = [tuple(k) for k in cell]
        groups = {}
        for i, k in enumerate(keys):
            groups.setdefault(k, []).append(i)
        full_lists, partial_lists = [], []
        for k, members in groups.items():
            for start in range(0, len(members), bucket_size):
                chunk = members[start:start + bucket_size]
                (full_lists if len(chunk) == bucket_size else partial_lists).append((k, chunk))
        buckets = full_lists + partial_lists
        order = np.array([i for _, members in buckets for i in members])
        centre_of = [p.min(0) + (np.array(k) + 0.5) * block for k, _ in buckets]
        R = 32767
        rec = np.zeros(n, dtype=[("p", "<u2", 3), ("s", "<f2", 3), ("r", "<f2", 4), ("c", "u1", 4)])
        pos = 0
        for (k, members), centre in zip(buckets, centre_of):
            for i in members:
                u = np.clip(np.round((p[i] - centre) * R / (block / 2)) + R, 0, 2 * R + 1)
                rec[pos] = (u, s[i], qn[i], colour[i])
                pos += 1
        records = rec.tobytes()
        centres = np.array(centre_of, np.float32).astype("<f4").tobytes()
        partial = np.array([len(m) for _, m in partial_lists], np.uint32).astype("<u4").tobytes()
        full, bucket_count, stride = len(full_lists), len(buckets), 24
    main = bytearray(4096)
    struct.pack_into("<BB", main, 0, 0, 1)
    struct.pack_into("<IIII", main, 4, 1, 1, n, n)
    struct.pack_into("<H", main, 20, level)
    struct.pack_into("<fff", main, 24, *p.mean(0))
    section = bytearray(1024)
    struct.pack_into("<IIIIf", section, 0, n, n, bucket_size if level else 0, bucket_count, block if level else 0.0)
    struct.pack_into("<H", section, 20, 12 if level else 0)
    struct.pack_into("<III", section, 24, 0, len(records) + len(centres) + len(partial), full)
    struct.pack_into("<IH", section, 36, len(partial) // 4, 0)
    with open(path, "wb") as f:
        f.write(bytes(main) + bytes(section) + partial + centres + records)


def write_point_ply(path, p, c):
    rec = np.zeros(len(p), dtype=[("p", "<f4", 3), ("c", "u1", 3)])
    rec["p"] = p
    rec["c"] = np.clip(np.round(c * 255), 0, 255)
    header = ("ply\nformat binary_little_endian 1.0\ncomment ImmersiveX test figure as points (y up)\n"
              f"element vertex {len(p)}\nproperty float x\nproperty float y\nproperty float z\n"
              "property uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n")
    with open(path, "wb") as f:
        f.write(header.encode("ascii") + rec.tobytes())


# ---------------------------------------------------------------- GenXR 3.5D stream

def write_stream(folder, frames=36, fps=12.0):
    """A tiny GenXR v3 stream of the waving figure: stream.json + tiers/base/NNNNNN.bin (+ a tone as audio.m4a)."""
    width = 1024
    tier_dir = os.path.join(folder, "tiers", "base")
    os.makedirs(tier_dir, exist_ok=True)
    lo, hi = -9.0, 1.0
    entries = []
    for i in range(frames):
        p, s, q, c, o = figure(i / fps, density=0.15)
        p, q = to_rdf(p, q)
        n = len(p)
        rows = (n + width - 1) // width
        bmin, bmax = p.min(0), p.max(0)
        t = np.round((p - bmin) / np.maximum(bmax - bmin, 1e-9) * 65535).astype(np.uint32)
        ls = np.clip(np.round((np.log(s) - lo) / (hi - lo) * 255), 0, 255).astype(np.uint32)
        qn = q / np.linalg.norm(q, axis=1, keepdims=True)
        r = np.clip(np.round(qn * 128 + 128), 0, 255).astype(np.uint32)
        rgb = np.clip(np.round(c * 255), 0, 255).astype(np.uint32)
        tex = np.zeros((rows * width, 4), np.uint32)
        tex[:n, 0] = t[:, 0] | (t[:, 1] << 16)
        tex[:n, 1] = t[:, 2] | (ls[:, 0] << 16) | (ls[:, 1] << 24)
        tex[:n, 2] = ls[:, 2] | (r[:, 0] << 8) | (r[:, 1] << 16) | (r[:, 2] << 24)
        tex[:n, 3] = r[:, 3] | (rgb[:, 0] << 8) | (rgb[:, 1] << 16) | (rgb[:, 2] << 24)
        alpha = np.zeros(rows * width, np.uint8)
        alpha[:n] = np.clip(np.round(o * 255), 0, 255)
        blob = struct.pack("<I6fI", n, *bmin, *bmax, rows) + tex.astype("<u4").tobytes() + alpha.tobytes()
        with open(os.path.join(tier_dir, f"{i:06d}.bin"), "wb") as f:
            f.write(blob)
        entries.append([n, len(blob)])
    audio = tone(os.path.join(folder, "audio.m4a"), frames / fps, 523.25)
    manifest = {
        "version": 3, "source": "ImmersiveX sample", "fps": fps, "frames": frames, "duration_seconds": frames / fps,
        "base_url": None, "texture_width": width, "gaussian_bytes": 17, "scale_range": [lo, hi],
        "audio": {"file": "audio.m4a", "codec": "aac"} if audio else None,
        "tiers": [{"name": "base", "path": "tiers/base", "frames": entries,
                   "rate_mb_s": sum(e[1] for e in entries) / frames * fps / 1e6}],
    }
    with open(os.path.join(folder, "stream.json"), "w") as f:
        json.dump(manifest, f)


# ---------------------------------------------------------------- meshes

def figure_mesh(t=0.0):
    """A low-poly figure (boxes) with vertex colours, y up, front towards the viewer (+z)."""
    verts, cols, faces = [], [], []

    def box(centre, size, colour):
        cx, cy, cz = centre
        sx, sy, sz = (v / 2 for v in size)
        base = len(verts)
        for dx in (-sx, sx):
            for dy in (-sy, sy):
                for dz in (-sz, sz):
                    verts.append((cx + dx, cy + dy, cz + dz))
                    cols.append(colour)
        # right-handed, counter-clockwise from outside
        for f in [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]:
            faces.append(tuple(base + i for i in f))

    wave = math.sin(t * 2 * math.pi) * 0.3
    box((0, 1.52, 0), (0.24, 0.26, 0.24), (230, 25, 25))
    box((0, 1.12, 0), (0.4, 0.55, 0.24), (235, 235, 235))
    box((-0.1, 0.45, 0), (0.12, 0.85, 0.12), (40, 75, 240))
    box((0.1, 0.45, 0), (0.12, 0.85, 0.12), (40, 75, 240))
    box((-0.42, 1.3, 0), (0.42, 0.09, 0.09), (25, 215, 50))
    box((0.42, 1.3 + wave, 0), (0.42, 0.09, 0.09), (240, 215, 25))
    box((0, 1.5, 0.19), (0.05, 0.05, 0.14), (255, 128, 12))
    return verts, cols, faces


def write_obj(path, verts, cols, faces, mtl=None, uvs=None):
    with open(path, "w") as f:
        if mtl:
            f.write(f"mtllib {mtl}\nusemtl skin\n")
        for (x, y, z), (r, g, b) in zip(verts, cols):
            f.write(f"v {x:.4f} {y:.4f} {z:.4f} {r / 255:.3f} {g / 255:.3f} {b / 255:.3f}\n" if not mtl else f"v {x:.4f} {y:.4f} {z:.4f}\n")
        if uvs:
            for u, v in uvs:
                f.write(f"vt {u:.4f} {v:.4f}\n")
        for face in faces:
            f.write("f " + " ".join(f"{i + 1}/{i + 1}" if uvs else str(i + 1) for i in face) + "\n")


def write_mesh_ply(path, verts, cols, faces):
    header = ("ply\nformat binary_little_endian 1.0\n"
              f"element vertex {len(verts)}\nproperty float x\nproperty float y\nproperty float z\n"
              "property uchar red\nproperty uchar green\nproperty uchar blue\n"
              f"element face {len(faces)}\nproperty list uchar int vertex_indices\nend_header\n")
    with open(path, "wb") as f:
        f.write(header.encode("ascii"))
        for (x, y, z), (r, g, b) in zip(verts, cols):
            f.write(struct.pack("<fffBBB", x, y, z, r, g, b))
        for face in faces:
            f.write(struct.pack("<B", len(face)) + struct.pack(f"<{len(face)}i", *face))


def textured_totem(folder):
    """A textured column with a face texture (checks texture loading through MTL), y up, front towards the viewer."""
    from PIL import Image, ImageDraw
    size = 256
    img = Image.new("RGB", (size, size), (40, 40, 60))
    d = ImageDraw.Draw(img)
    for y in range(0, size, 32):
        for x in range(0, size, 32):
            if (x // 32 + y // 32) % 2 == 0:
                d.rectangle([x, y, x + 31, y + 31], fill=(70, 70, 110))
    d.rectangle([96, 20, 160, 60], fill=(230, 30, 30))       # top: red band
    d.text((70, 104), "FRONT", fill=(255, 200, 0), font=font(34))
    d.rectangle([96, 200, 160, 236], fill=(40, 80, 240))     # bottom: blue band
    img.save(os.path.join(folder, "totem.png"))
    with open(os.path.join(folder, "totem.mtl"), "w") as f:
        f.write("newmtl skin\nKd 1 1 1\nmap_Kd totem.png\n")
    # A box 0.5 × 1.6 × 0.5 whose front face (+z) shows the whole texture.
    w, h = 0.25, 1.6
    verts = [(-w, 0, w), (w, 0, w), (w, h, w), (-w, h, w),          # front (+z)
             (w, 0, -w), (-w, 0, -w), (-w, h, -w), (w, h, -w)]      # back
    uvs = [(0, 0), (1, 0), (1, 1), (0, 1), (0, 0), (1, 0), (1, 1), (0, 1)]
    faces = [(0, 1, 2, 3), (4, 5, 6, 7), (1, 4, 7, 2), (5, 0, 3, 6), (3, 2, 7, 6), (5, 4, 1, 0)]
    write_obj(os.path.join(folder, "totem.obj"), verts, [(255, 255, 255)] * 8, faces, mtl="totem.mtl", uvs=uvs)


def write_animated_glb(path):
    """The box figure as a glTF 2.0 binary (y up, front +z, left arm at -x), its yellow arm waving: a 2 s rotation."""
    faces = [((0, 0, 1), [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]),
             ((0, 0, -1), [(1, -1, -1), (-1, -1, -1), (-1, 1, -1), (1, 1, -1)]),
             ((1, 0, 0), [(1, -1, 1), (1, -1, -1), (1, 1, -1), (1, 1, 1)]),
             ((-1, 0, 0), [(-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)]),
             ((0, 1, 0), [(-1, 1, 1), (1, 1, 1), (1, 1, -1), (-1, 1, -1)]),
             ((0, -1, 0), [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)])]
    positions, normals, indices = [], [], []
    for normal, corners in faces:
        base = len(positions)
        positions += [tuple(c * 0.5 for c in corner) for corner in corners]
        normals += [normal] * 4
        indices += [base, base + 1, base + 2, base, base + 2, base + 3]  # counter-clockwise from outside
    times = [0.0, 0.5, 1.0, 1.5, 2.0]
    angles = [0, 35, 0, -25, 0]
    rotations = [(0.0, 0.0, math.sin(math.radians(a) / 2), math.cos(math.radians(a) / 2)) for a in angles]

    blob = bytearray()

    def add(data, fmt):
        offset = len(blob)
        for row in data:
            blob.extend(struct.pack(fmt, *row) if isinstance(row, tuple) else struct.pack(fmt, row))
        while len(blob) % 4:
            blob.append(0)
        return offset, len(blob) - offset

    views, accessors = [], []

    def accessor(data, fmt, component, kind, count, target=None, bounds=None):
        offset, length = add(data, fmt)
        view = {"buffer": 0, "byteOffset": offset, "byteLength": length}
        if target:
            view["target"] = target
        views.append(view)
        entry = {"bufferView": len(views) - 1, "componentType": component, "count": count, "type": kind}
        if bounds:
            entry["min"], entry["max"] = bounds
        accessors.append(entry)
        return len(accessors) - 1

    pos = accessor(positions, "<fff", 5126, "VEC3", len(positions), 34962, ([-0.5] * 3, [0.5] * 3))
    nor = accessor(normals, "<fff", 5126, "VEC3", len(normals), 34962)
    idx = accessor(indices, "<H", 5123, "SCALAR", len(indices), 34963)
    tin = accessor(times, "<f", 5126, "SCALAR", len(times), None, ([0.0], [2.0]))
    rot = accessor(rotations, "<ffff", 5126, "VEC4", len(rotations))

    colours = {"red": [0.9, 0.1, 0.1, 1], "white": [0.92, 0.92, 0.92, 1], "blue": [0.15, 0.3, 0.95, 1],
               "green": [0.1, 0.85, 0.2, 1], "yellow": [0.95, 0.85, 0.1, 1], "orange": [1.0, 0.5, 0.05, 1]}
    names = list(colours)
    materials = [{"name": n, "pbrMetallicRoughness": {"baseColorFactor": colours[n], "metallicFactor": 0.0, "roughnessFactor": 0.8}} for n in names]
    meshes = [{"name": n, "primitives": [{"attributes": {"POSITION": pos, "NORMAL": nor}, "indices": idx, "material": i}]} for i, n in enumerate(names)]

    def box(name, t, s, colour, children=None):
        node = {"name": name, "translation": list(t), "scale": list(s), "mesh": names.index(colour)}
        if children:
            node["children"] = children
        return node

    nodes = [
        {"name": "Figure", "children": [1, 2, 3, 4, 5, 6, 8]},
        box("Head", (0, 1.52, 0), (0.24, 0.26, 0.24), "red"),
        box("Body", (0, 1.12, 0), (0.4, 0.55, 0.24), "white"),
        box("Left Leg", (-0.1, 0.45, 0), (0.12, 0.85, 0.12), "blue"),
        box("Right Leg", (0.1, 0.45, 0), (0.12, 0.85, 0.12), "blue"),
        box("Left Arm (green)", (-0.42, 1.3, 0), (0.42, 0.09, 0.09), "green"),
        {"name": "Right Shoulder", "translation": [0.21, 1.3, 0], "children": [7]},
        box("Right Arm (yellow)", (0.21, 0, 0), (0.42, 0.09, 0.09), "yellow"),
        box("Nose", (0, 1.5, 0.19), (0.05, 0.05, 0.14), "orange"),
    ]
    gltf = {
        "asset": {"version": "2.0", "generator": "ImmersiveX make_media_samples.py"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": nodes, "meshes": meshes, "materials": materials,
        "animations": [{"name": "Wave", "samplers": [{"input": tin, "output": rot, "interpolation": "LINEAR"}],
                        "channels": [{"sampler": 0, "target": {"node": 6, "path": "rotation"}}]}],
        "buffers": [{"byteLength": len(blob)}], "bufferViews": views, "accessors": accessors,
    }
    text = json.dumps(gltf, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    body = bytes(blob) + b"\0" * (-len(blob) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(text) + 8 + len(body)))
        f.write(struct.pack("<II", len(text), 0x4E4F534A) + text)
        f.write(struct.pack("<II", len(body), 0x004E4942) + body)


# ---------------------------------------------------------------- video

def tone(path, seconds, frequency):
    if not shutil.which("ffmpeg"):
        return False
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "lavfi", "-i", f"sine=frequency={frequency}:duration={seconds}",
                    "-c:a", "aac", "-b:a", "64k", path], check=True)
    return True


def font(size):
    from PIL import ImageFont
    try:
        return ImageFont.load_default(size=size)
    except TypeError:  # Pillow < 10.1
        return ImageFont.load_default()


def equirect_frame(path, width, height, label=""):
    """An equirectangular test image: FRONT in the middle, RIGHT at 3/4, BACK at the edges, LEFT at 1/4, UP and DOWN."""
    from PIL import Image, ImageDraw
    img = Image.new("RGB", (width, height), (20, 24, 40))
    big = font(max(18, height // 22))
    d = ImageDraw.Draw(img)
    for i in range(0, 361, 30):  # longitude lines every 30°
        x = int(i / 360 * (width - 1))
        d.line([x, 0, x, height], fill=(70, 80, 120), width=2)
    for j in range(0, 181, 30):
        y = int(j / 180 * (height - 1))
        d.line([0, y, width, y], fill=(70, 80, 120), width=2)
    for text, u, v, colour in [("FRONT", 0.5, 0.5, (255, 220, 0)), ("RIGHT", 0.75, 0.5, (240, 80, 80)),
                               ("LEFT", 0.25, 0.5, (80, 220, 80)), ("BACK", 0.02, 0.5, (160, 160, 255)),
                               ("BACK", 0.93, 0.5, (160, 160, 255)), ("UP", 0.5, 0.06, (255, 255, 255)),
                               ("DOWN", 0.5, 0.92, (255, 255, 255))]:
        x, y = int(u * width), int(v * height)
        words = text + (" " + label if label else "")
        box = d.textbbox((0, 0), words, font=big)
        w, h = box[2] - box[0], box[3] - box[1]
        d.rectangle([x - w // 2 - 12, y - h // 2 - 10, x + w // 2 + 12, y + h // 2 + 14], outline=colour, width=4)
        d.text((x - w // 2, y - h // 2), words, fill=colour, font=big)
    img.save(path)


def videos(folder):
    if not shutil.which("ffmpeg"):
        print("ffmpeg not found: skipping video samples")
        return
    run = lambda *a: subprocess.run(["ffmpeg", "-y", "-loglevel", "error", *a], check=True)
    # Flat 16:9 with a running clock and a tone.
    run("-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=6", "-f", "lavfi", "-i", "sine=frequency=440:duration=6",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-profile:v", "main", "-crf", "30", "-c:a", "aac", "-b:a", "64k", "-shortest",
        os.path.join(folder, "video_2d.mp4"))
    tmp = os.path.join(folder, "_frame.png")
    # 360° mono (2:1), a marker scrolling so you can see it play.
    equirect_frame(tmp, 2048, 1024)
    run("-loop", "1", "-i", tmp, "-f", "lavfi", "-i", "sine=frequency=330:duration=5",
        "-vf", "drawbox=x='mod(t*300,iw)':y=ih/2-8:w=40:h=16:color=white:t=fill", "-t", "5", "-r", "24",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "32", "-c:a", "aac", "-b:a", "48k", "-shortest",
        os.path.join(folder, "video_360.mp4"))
    # 360° stereo top-bottom (left eye on top), 1:1.
    from PIL import Image
    equirect_frame(tmp, 1024, 512, "L")
    left = Image.open(tmp).copy()
    equirect_frame(tmp, 1024, 512, "R")
    right = Image.open(tmp).copy()
    both = Image.new("RGB", (1024, 1024))
    both.paste(left, (0, 0))
    both.paste(right, (0, 512))
    both.save(tmp)
    run("-loop", "1", "-i", tmp, "-t", "4", "-r", "24", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "32",
        os.path.join(folder, "video_360_tb.mp4"))
    # 180° stereo side-by-side (left eye on the left), each eye 1:1.
    equirect_frame(tmp, 1024, 1024, "L")
    left = Image.open(tmp).copy()
    equirect_frame(tmp, 1024, 1024, "R")
    right = Image.open(tmp).copy()
    both = Image.new("RGB", (2048, 1024))
    both.paste(left, (0, 0))
    both.paste(right, (1024, 0))
    both.save(tmp)
    run("-loop", "1", "-i", tmp, "-t", "4", "-r", "24", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "32",
        os.path.join(folder, "video_180_sbs.mp4"))
    os.remove(tmp)


# ---------------------------------------------------------------- main

def main():
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)
    splats = os.path.join(OUT, "splats")
    os.makedirs(splats)

    p, s, q, c, o = figure(density=0.6)
    prdf, qrdf = to_rdf(p, q)
    write_inria_ply(os.path.join(splats, "figure.ply"), prdf, s, qrdf, c, o)
    write_compressed_ply(os.path.join(splats, "figure.compressed.ply"), prdf, s, qrdf, c, o)
    write_splat(os.path.join(splats, "figure.splat"), prdf, s, qrdf, c, o)
    write_ksplat(os.path.join(splats, "figure-level0.ksplat"), prdf, s, qrdf, c, o, 0)
    write_ksplat(os.path.join(splats, "figure.ksplat"), prdf, s, qrdf, c, o, 1)
    prub, qrub = to_rub(p, q)
    write_spz_legacy(os.path.join(splats, "figure-v2.spz"), prub, s, qrub, c, o, 2)
    write_spz_legacy(os.path.join(splats, "figure-v3.spz"), prub, s, qrub, c, o, 3)
    try:
        write_spz_v4(os.path.join(splats, "figure.spz"), prub, s, qrub, c, o)
    except ImportError:
        print("zstandard not installed: skipping SPZ v4")

    points = os.path.join(OUT, "points")
    os.makedirs(points)
    write_point_ply(os.path.join(points, "figure-points.ply"), p, c)

    # Sequences: splat frames (.splat, RDF), point frames (.ply, y up), mesh frames (.obj and .ply, y up).
    for name, frames, fps in [("splat-sequence", 24, 12.0), ("point-sequence", 24, 12.0), ("mesh-sequence", 24, 12.0)]:
        folder = os.path.join(OUT, name)
        os.makedirs(folder)
        files = []
        for i in range(frames):
            t = i / fps
            if name == "splat-sequence":
                fp, fs, fq, fc, fo = figure(t, density=0.12)
                fp, fq = to_rdf(fp, fq)
                file = f"frame_{i:04d}.splat"
                write_splat(os.path.join(folder, file), fp, fs, fq, fc, fo)
            elif name == "point-sequence":
                fp, _, _, fc, _ = figure(t, density=0.15)
                file = f"frame_{i:04d}.ply"
                write_point_ply(os.path.join(folder, file), fp, fc)
            else:
                v, col, f = figure_mesh(t)
                file = f"frame_{i:04d}.obj"
                write_obj(os.path.join(folder, file), v, col, f)
            files.append(file)
        with open(os.path.join(folder, "sequence.json"), "w") as f:
            json.dump({"type": "sequence", "fps": fps, "frames": files}, f, indent=1)

    meshes = os.path.join(OUT, "meshes")
    os.makedirs(meshes)
    v, col, f = figure_mesh()
    write_obj(os.path.join(meshes, "figure.obj"), v, col, f)
    write_mesh_ply(os.path.join(meshes, "figure-mesh.ply"), v, col, f)
    textured_totem(meshes)

    models = os.path.join(OUT, "models")
    os.makedirs(models)
    write_animated_glb(os.path.join(models, "figure-animated.glb"))

    write_stream(os.path.join(OUT, "hologram"))
    videos(os.path.join(OUT))

    total = 0
    for dirpath, _, names in os.walk(OUT):
        for name in names:
            total += os.path.getsize(os.path.join(dirpath, name))
    print(f"Wrote {OUT} ({total / 1e6:.1f} MB)")


if __name__ == "__main__":
    sys.exit(main())
