# Sample Files

This directory contains sample files used for testing and demonstrating **ComfyWebpViewer**.

## WebP Test Samples

| File | Size | Type | ComfyUI Metadata | Notes |
| --- | --- | --- | --- | --- |
| `comfy_static_lossy.webp` | 640 × 384 | Static lossy | Yes | Contains prompt / workflow; verifies static-lossy decoding and metadata extraction |
| `comfy_static_lossless.webp` | 640 × 384 | Static lossless | Yes | Contains prompt / workflow; verifies static-lossless decoding |
| `comfy_anim.webp` | 320 × 240 | Animated | Yes | 8-frame animation with prompt / workflow; verifies animation playback |
| `plain.webp` | 64 × 64 | Static lossy | No | Plain WebP; verifies no crash when ComfyUI metadata is absent |
| `krea1.webp` | 1504 × 832 | Static lossy | Yes | Real ComfyUI `krea_style` output; verifies high-DPI / 72-DPI fit |

## Helper Files

- `gen.py`: Script that generates the synthetic samples above using Pillow.
- `exif/*.exif.xml`: Raw EXIF XML references exported with exiv2 / exiftool.
- `screenshots/*.preview.png`: Default plugin preview screenshots.
- `screenshots/*.preview-meta.png`: Screenshots with the right-side metadata drawer open.

## Generate Synthetic Samples

```bash
pip install Pillow
python gen.py
```

The generator produces static-lossy, static-lossless, and animated ComfyUI-style WebP files, plus one plain WebP without metadata.
