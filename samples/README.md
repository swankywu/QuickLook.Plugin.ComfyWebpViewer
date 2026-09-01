# 示例文件

本目录包含用于测试和演示 **ComfyWebpViewer** 的样例文件。

## WebP 测试样本

| 文件名 | 尺寸 | 类型 | ComfyUI 元数据 | 说明 |
| --- | --- | --- | --- | --- |
| `comfy_static_lossy.webp` | 640 × 384 | 静态有损 | 有 | 含 prompt / workflow，验证静态有损解码与元数据提取 |
| `comfy_static_lossless.webp` | 640 × 384 | 静态无损 | 有 | 含 prompt / workflow，验证静态无损解码 |
| `comfy_anim.webp` | 320 × 240 | 动画 | 有 | 8 帧动画，含 prompt / workflow，验证动画播放 |
| `plain.webp` | 64 × 64 | 静态有损 | 无 | 普通 WebP，验证无 ComfyUI 元数据时不会崩溃 |
| `krea1.webp` | 1504 × 832 | 静态有损 | 有 | 真实 ComfyUI `krea_style` 输出，验证高 DPI / 72-DPI 适配与 fit |

## 辅助文件

- `gen.py`：使用 Pillow 生成上述合成样例的脚本。
- `exif/*.exif.xml`：用 exiv2 / exiftool 导出的 EXIF 原始 XML 参考。
- `screenshots/*.preview.png`：插件默认预览截图。
- `screenshots/*.preview-meta.png`：插件右侧元数据抽屉打开后的截图。

## 生成合成样本

```bash
pip install Pillow
python gen.py
```

生成脚本会同时产出静态有损、静态无损、动画三种 ComfyUI 风格 WebP，以及一个无元数据的普通 WebP。
