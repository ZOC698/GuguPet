from __future__ import annotations

import hashlib
import json
from pathlib import Path

from PIL import Image, ImageChops

from build_walk_repair import CELL_H, CELL_W
from make_walk_from_static_preview import load_direction


ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "Assets" / "spritesheet.png"
REPORT = ROOT.parent / "work" / "gugu-walk-animation" / "qa" / "install-report.json"
WALK_ROWS = {"right": 1, "left": 2}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def main() -> None:
    original_bytes_hash = sha256(ATLAS)
    original = Image.open(ATLAS).convert("RGBA")
    expected_size = (CELL_W * 8, CELL_H * 11)
    if original.size != expected_size:
        raise ValueError(f"Expected v2 atlas {expected_size}, got {original.size}")

    candidate = original.copy()
    frame_metrics: dict[str, list[dict[str, object]]] = {}
    for direction, row in WALK_ROWS.items():
        frames = load_direction(direction)
        if len(frames) != 8:
            raise ValueError(f"{direction} must contain exactly 8 frames")
        row_top = row * CELL_H
        candidate.paste((0, 0, 0, 0), (0, row_top, CELL_W * 8, row_top + CELL_H))
        metrics = []
        for column, frame in enumerate(frames):
            if frame.size != (CELL_W, CELL_H) or frame.getbbox() is None:
                raise ValueError(f"Invalid {direction} frame {column}")
            candidate.alpha_composite(frame, (column * CELL_W, row_top))
            bbox = frame.getbbox()
            metrics.append({
                "frame": column,
                "bbox": list(bbox) if bbox else None,
                "visible_height": bbox[3] - bbox[1] if bbox else 0,
            })
        frame_metrics[direction] = metrics

    # Prove that the trial installation changes only the two directional rows.
    outside_original = original.copy()
    outside_candidate = candidate.copy()
    for row in WALK_ROWS.values():
        box = (0, row * CELL_H, CELL_W * 8, (row + 1) * CELL_H)
        outside_original.paste((0, 0, 0, 0), box)
        outside_candidate.paste((0, 0, 0, 0), box)
    if ImageChops.difference(outside_original, outside_candidate).getbbox() is not None:
        raise RuntimeError("Candidate changed pixels outside walk rows")

    candidate.save(ATLAS, optimize=True)
    installed = Image.open(ATLAS).convert("RGBA")
    if ImageChops.difference(candidate, installed).getbbox() is not None:
        raise RuntimeError("Saved atlas does not match assembled candidate")

    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps({
        "ok": True,
        "atlas": str(ATLAS),
        "atlas_size": list(installed.size),
        "before_sha256": original_bytes_hash,
        "after_sha256": sha256(ATLAS),
        "changed_rows": [1, 2],
        "unchanged_rows_verified": [0, 3, 4, 5, 6, 7, 8, 9, 10],
        "frames": frame_metrics,
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(ATLAS)
    print(REPORT)


if __name__ == "__main__":
    main()
