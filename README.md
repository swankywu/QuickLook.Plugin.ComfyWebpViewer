# ComfyWebpViewer

让 QuickLook 正确预览 ComfyUI 生成的 WebP 图片，并读取其中嵌入的 prompt / workflow 元数据。

## 问题背景

QuickLook 自带的 **ImageViewer** 在预览 ComfyUI 生成的 `.webp` 时，会通过 exiv2 读取 EXIF，并直接把输出塞进 `XmlDocument.LoadXml()`。

ComfyUI 将 prompt 和 workflow JSON 写入 EXIF tag（`0x0110` / `0x010F`），这些 JSON 里常包含裸 `&`、`<lora:...>` 等字符，导致 XML 解析抛异常 → `Prepare()` 失败 → 预览窗口直接关闭。

本插件绕过 exiv2：自己解析 WebP RIFF 容器和 EXIF 块，用 Windows Imaging Component（WIC）解码像素，并手写 JSON 解析器提取生成参数。

## 功能特性

- 支持 **静态有损 / 静态无损 / 动画 WebP**
- 自动识别并解析 **ComfyUI prompt / workflow** 元数据
- 右侧元数据抽屉显示：Sampler、Steps、CFG、Seed、Nodes、正向/负向提示词、完整 prompt API JSON、workflow UI JSON
- 高 DPI / 非 96-DPI WebP 正确 fit，不裁剪、不拉伸
- 鼠标交互：滚轮缩放、Ctrl + 滚轮快速缩放、拖动平移、双击 适应/100% 切换
- 不抢占键盘焦点，方向键仍可在资源管理器中切换文件
- 仅当检测到 ComfyUI 元数据时才接管 `.webp`，不影响普通 WebP

## 快速开始

### 环境要求

- Windows 10 / 11
- 已安装 [.NET Framework 4.6.2 运行时](https://dotnet.microsoft.com/download/dotnet-framework/net462)
- 构建工具：Visual Studio 2019+ 或 `dotnet build` CLI

### 构建与部署

```powershell
cd src\QuickLook.Plugin.ComfyWebpViewer
dotnet build -c Release
```

构建完成后，`QuickLook.Plugin.ComfyWebpViewer.dll` 与 `Translations.config` 会被 `DeployPlugin` Target 自动复制到：

```
QuickLook.Plugin\QuickLook.Plugin.ComfyWebpViewer\
```

> **注意**：QuickLook 会锁定已加载的 DLL，升级前请先退出 QuickLook，再重新启动。

## 项目结构

| 文件 | 作用 |
| --- | --- |
| `Plugin.cs` | QuickLook 插件入口，实现 `IViewer` |
| `ImagePanel.xaml` / `.xaml.cs` | 预览 UI：暗色棋盘底、缩放/平移、动画控制、元数据抽屉 |
| `WebpDecoder.cs` | 用 WIC 解码 WebP；将帧归一化到 96 DPI，避免 DIP 尺寸失真 |
| `WebpFile.cs` | 手工解析 RIFF / VP8X / VP8 / VP8L / ANIM / ANMF / EXIF / XMP |
| `ComfyMetadata.cs` | 从 JSON 中回溯生成参数 |
| `Json.cs` | 最小 JSON 解析器 |
| `Translations.config` | 多语言文本（en / zh-CN / zh-TW） |
| `samples/` | 测试样例与截图，见 [samples/README.md](samples/README.md) |

## 工作原理

### 1. 容器解析

`WebpFile.cs` 直接按 RIFF 规范遍历 chunk，定位 `EXIF` chunk。Pillow 写入的 EXIF 会带 `Exif\0\0` 6 字节前缀，这里会剥离；`ANIM` 的循环次数按 16-bit 解析。

### 2. 元数据提取

EXIF 块中是 UTF-8 文本：

- `prompt:{...}`：ComfyUI 的 prompt API JSON
- `workflow:{...}`：workflow UI JSON

`Json.cs` 对其进行最小化解析；`ComfyMetadata.cs` 沿 prompt 的链接图（如 `["6",0]`）找到 `CLIPTextEncode` 节点，提取正向/负向提示词与 `KSampler` 参数。

### 3. 解码与 DPI 修复

WIC 解码器会按 WebP 文件内嵌的 DPI 给出 `BitmapSource.Width/Height`。ComfyUI 输出常见 72 DPI，导致自然尺寸被当成 `PixelWidth / 72 * 96`，进而被裁剪。

`WebpDecoder.NormalizeDpi()` 通过 `FormatConvertedBitmap` 转成 `Bgra32` 后再写入 96 DPI 的 `WriteableBitmap`，使 DIP 尺寸等于像素尺寸，fit 与缩放完全正确。

## 鼠标与键盘

| 操作 | 效果 |
| --- | --- |
| 滚轮 | 以鼠标位置为中心缩放 |
| Ctrl + 滚轮 | 快速缩放 |
| 按住左键拖动 | 平移图片 |
| 双击 | 适应 ↔ 100% 切换 |
| 点击“适应” | 将图片完整 fit 到窗口 |
| 方向键 ↑↓←→ | 在资源管理器中切换文件（焦点仍在资源管理器） |

## 配置

插件默认**只接管包含 ComfyUI 元数据的 `.webp`**。若希望它处理所有 WebP 文件，可创建配置文件：

路径：`%LOCALAPPDATA%\QuickLook\QuickLook.Plugin.ComfyWebpViewer.config`

内容：

```xml
<?xml version="1.0" encoding="utf-8"?>
<Settings>
  <HandleAllWebP>True</HandleAllWebP>
</Settings>
```

## 已知问题与排查

| 现象 | 原因 | 解决 |
| --- | --- | --- |
| 预览窗口闪退 | ImageViewer 的 exiv2 → XML 解析被 ComfyUI 元数据破坏 | 已由本插件绕过 |
| 适应后图片仍被裁剪，滚动条存在 | WebP 内嵌 72 DPI，DIP 尺寸 ≠ 像素尺寸 | 已在 `WebpDecoder` 中归一化到 96 DPI |
| 方向键不能切换文件 | 插件抢占了键盘焦点 | 已移除 `Focus()` 调用 |
| 动画 WebP 只显示一帧 | 正在播放的帧索引为 0 | 自动循环播放；可拖动帧滑块或暂停 |

## 示例截图

### 静态有损 WebP（含元数据抽屉）

![静态有损预览](samples/screenshots/comfy_static_lossy.preview-meta.png)

### 静态无损 WebP（默认预览）

![静态无损预览](samples/screenshots/comfy_static_lossless.preview.png)

### 动画 WebP（元数据抽屉打开）

![动画预览](samples/screenshots/comfy_anim.preview-meta.png)

## 测试用例

项目提供了一组最小化测试文件，覆盖静态有损/无损、动画、无元数据、真实 ComfyUI 输出等场景，详见 [samples/README.md](samples/README.md)。
