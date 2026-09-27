#!/usr/bin/env python3
"""
Pack a 4D Gaussian-splat sequence (a folder of standard 3DGS .ply files, one per frame) into a GenXR v3 hologram
stream: stream.json + tiers/<tier>/NNNNNN.bin. Immersive Media plays it from local storage, so a stream written under
Assets/StreamingAssets ships inside the app and plays offline.

    python3 tools/content/splat_sequence_to_stream.py research/atlux_ue_zebra_4dgs_ply \
        Assets/StreamingAssets/ImmersiveXContent/Zebra --fps 30 --title Zebra

- Each Gaussian takes 17 bytes: a 16-byte texel (16-bit position in the frame's box, 8-bit log scales, rotation and
  sRGB colour) and an opacity byte. The frame uploads to the GPU as it is, so playback costs almost no CPU.
- Colour is the view-independent part (the DC term). The stream format has no higher-order spherical harmonics.
- The stream gets one fit for the whole clip ("fit" in stream.json): a character keeps its size and spot when it
  raises an arm or steps aside, instead of being refitted every frame.
- Frames are y-down (x right, z away from the viewer), the usual 3DGS convention. Use --up +y for y-up sources.
"""

import argparse
import json
import os
import re
import shutil
import struct
from multiprocessing import Pool

import numpy as np

WIDTH = 1024  # texels per row
C0 = 0.28209479177387814  # degree-0 spherical harmonic
PLY_TYPES = {
    "char": "i1", "int8": "i1", "uchar": "u1", "uint8": "u1", "short": "<i2", "int16": "<i2",
    "ushort": "<u2", "uint16": "<u2", "int": "<i4", "int32": "<i4", "uint": "<u4", "uint32": "<u4",
    "float": "<f4", "float32": "<f4", "double": "<f8", "float64": "<f8",
}


def read_ply(path):
    """The vertex element of a binary little-endian 3DGS PLY, as a structured numpy array."""
    with open(path, "rb") as f:
        data = f.read()
    end = data.index(b"end_header") + len(b"end_header")
    end = data.index(b"\n", end) + 1
    header = data[:end].decode("ascii", "replace")
    if "binary_little_endian" not in header:
        raise ValueError(f"{path}: only binary little-endian PLY is supported")
    count = None
    fields = []
    in_vertex = False
    for line in header.splitlines():
        words = line.split()
        if words[:1] == ["element"]:
            in_vertex = words[1] == "vertex"
            if in_vertex:
                count = int(words[2])
        elif words[:1] == ["property"] and in_vertex:
            if words[1] == "list":
                raise ValueError(f"{path}: vertex lists aren't Gaussian splats")
            fields.append((words[2], PLY_TYPES[words[1]]))
    vertices = np.frombuffer(data, dtype=np.dtype(fields), count=count, offset=end)
    missing = [n for n in ("x", "y", "z", "f_dc_0", "opacity", "scale_0", "rot_0") if n not in vertices.dtype.names]
    if missing:
        raise ValueError(f"{path}: not a 3DGS PLY (missing {', '.join(missing)})")
    return vertices


def gaussians(path, up):
    """Positions (y-down), log scales, unit rotations (w, x, y, z), sRGB colours and opacities of one frame."""
    v = read_ply(path)
    p = np.stack([v["x"], v["y"], v["z"]], 1).astype(np.float64)
    log_s = np.stack([v["scale_0"], v["scale_1"], v["scale_2"]], 1).astype(np.float64)
    q = np.stack([v["rot_0"], v["rot_1"], v["rot_2"], v["rot_3"]], 1).astype(np.float64)
    q /= np.maximum(np.linalg.norm(q, axis=1, keepdims=True), 1e-12)
    q *= np.where(q[:, :1] < 0, -1.0, 1.0)  # q and -q are the same rotation; keep w >= 0
    c = np.clip(0.5 + C0 * np.stack([v["f_dc_0"], v["f_dc_1"], v["f_dc_2"]], 1), 0, 1)
    o = 1 / (1 + np.exp(-v["opacity"].astype(np.float64)))
    if up == "+y":  # turn y-up into y-down: a half turn about x
        p[:, 1:] *= -1
        q[:, 2:] *= -1
    return p, log_s, q, c, o


def strongest(o, log_s, keep):
    """Indices of the most visible Gaussians: opacity times projected area."""
    area = np.exp(2 / 3 * log_s.sum(1))
    return np.sort(np.argpartition(-(o * area), keep - 1)[:keep])


def pack(p, log_s, q, c, o, scale_range):
    lo, hi = scale_range
    n = len(p)
    rows = (n + WIDTH - 1) // WIDTH
    bmin, bmax = p.min(0), p.max(0)
    t = np.round((p - bmin) / np.maximum(bmax - bmin, 1e-9) * 65535).astype(np.uint32)
    ls = np.clip(np.round((log_s - lo) / (hi - lo) * 255), 0, 255).astype(np.uint32)
    r = np.clip(np.round(q * 128 + 128), 0, 255).astype(np.uint32)
    rgb = np.round(c * 255).astype(np.uint32)
    tex = np.zeros((rows * WIDTH, 4), np.uint32)
    tex[:n, 0] = t[:, 0] | (t[:, 1] << 16)
    tex[:n, 1] = t[:, 2] | (ls[:, 0] << 16) | (ls[:, 1] << 24)
    tex[:n, 2] = ls[:, 2] | (r[:, 0] << 8) | (r[:, 1] << 16) | (r[:, 2] << 24)
    tex[:n, 3] = r[:, 3] | (rgb[:, 0] << 8) | (rgb[:, 1] << 16) | (rgb[:, 2] << 24)
    alpha = np.zeros(rows * WIDTH, np.uint8)
    alpha[:n] = np.round(o * 255).astype(np.uint8)
    header = struct.pack("<I6fI", n, *bmin.astype(np.float32), *bmax.astype(np.float32), rows)
    return header + tex.astype("<u4").tobytes() + alpha.tobytes()


def frame_fit(p):
    """The same fit the player makes per frame: 0.5 % tails of y, mean x and z."""
    top, bottom = np.quantile(p[:, 1], [0.005, 0.995])
    return float(p[:, 0].mean()), float(p[:, 2].mean()), float(top), float(bottom)


def convert(job):
    index, path, out_dir, up, scale_range, max_splats = job
    p, log_s, q, c, o = gaussians(path, up)
    if max_splats and len(p) > max_splats:
        keep = strongest(o, log_s, max_splats)
        p, log_s, q, c, o = p[keep], log_s[keep], q[keep], c[keep], o[keep]
    blob = pack(p, log_s, q, c, o, scale_range)
    with open(os.path.join(out_dir, f"{index:06d}.bin"), "wb") as f:
        f.write(blob)
    return index, len(p), len(blob), frame_fit(p)


def natural_key(name):
    return [int(part) if part.isdigit() else part.lower() for part in re.split(r"(\d+)", name)]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", help="folder of .ply frames, in name order")
    parser.add_argument("output", help="stream folder to write (replaced)")
    parser.add_argument("--fps", type=float, default=30.0, help="frame rate (default 30)")
    parser.add_argument("--title", default=None, help="title shown in logs (default: the source folder's name)")
    parser.add_argument("--tier", default="base", help="quality tier name (default base, what Immersive Media asks for)")
    parser.add_argument("--max-splats", type=int, default=0, help="keep at most this many Gaussians per frame (0 = all)")
    parser.add_argument("--up", choices=["-y", "+y"], default="-y", help="the source's up axis (default -y: y-down 3DGS)")
    parser.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 2) - 1))
    args = parser.parse_args()

    frames = sorted((f for f in os.listdir(args.source) if f.lower().endswith(".ply")), key=natural_key)
    if not frames:
        raise SystemExit(f"No .ply frames in {args.source}")
    paths = [os.path.join(args.source, f) for f in frames]

    # One scale range for the whole clip, from a sample of frames.
    sample = [gaussians(paths[i], args.up)[1] for i in range(0, len(paths), max(1, len(paths) // 12))]
    values = np.concatenate(sample).ravel()
    lo = float(np.floor(np.quantile(values, 1e-4) * 2) / 2)
    hi = float(np.ceil(np.quantile(values, 1 - 1e-4) * 2) / 2)

    tier_path = f"tiers/{args.tier}"
    tier_dir = os.path.join(args.output, *tier_path.split("/"))
    if os.path.isdir(args.output):
        shutil.rmtree(args.output)
    os.makedirs(tier_dir)

    jobs = [(i, path, tier_dir, args.up, (lo, hi), args.max_splats) for i, path in enumerate(paths)]
    with Pool(args.jobs) as pool:
        results = sorted(pool.imap_unordered(convert, jobs, chunksize=2))

    fits = np.array([r[3] for r in results])
    fit = {
        "centre_x": round(float(fits[:, 0].mean()), 5),
        "centre_z": round(float(fits[:, 1].mean()), 5),
        "top": round(float(np.median(fits[:, 2])), 5),     # the usual standing height, not the highest reach
        "bottom": round(float(np.median(fits[:, 3])), 5),  # y points down: the feet
    }
    entries = [[r[1], r[2]] for r in results]
    total = sum(e[1] for e in entries)
    manifest = {
        "version": 3,
        "source": os.path.basename(os.path.normpath(args.source)),
        "title": args.title or os.path.basename(os.path.normpath(args.source)),
        "fps": args.fps,
        "frames": len(entries),
        "duration_seconds": round(len(entries) / args.fps, 3),
        "base_url": None,
        "texture_width": WIDTH,
        "gaussian_bytes": 17,
        "scale_range": [lo, hi],
        "audio": None,
        "fit": fit,
        "tiers": [{"name": args.tier, "path": tier_path, "frames": entries,
                   "rate_mb_s": round(total / len(entries) * args.fps / 1e6, 2)}],
    }
    with open(os.path.join(args.output, "stream.json"), "w") as f:
        json.dump(manifest, f)

    counts = [e[0] for e in entries]
    print(f"{len(entries)} frames at {args.fps:g} fps ({len(entries) / args.fps:.1f} s) -> {args.output}")
    print(f"{min(counts):,}-{max(counts):,} Gaussians a frame, {total / 1e6:.0f} MB, "
          f"{manifest['tiers'][0]['rate_mb_s']} MB/s to play, scale range [{lo}, {hi}]")
    print(f"Fit: {fit['bottom'] - fit['top']:.2f} source units tall, centre ({fit['centre_x']:.2f}, {fit['centre_z']:.2f})")


if __name__ == "__main__":
    main()
