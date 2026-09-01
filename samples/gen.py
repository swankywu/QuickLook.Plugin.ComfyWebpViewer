import json, os
from PIL import Image

def make_exif(prompt, workflow):
    exif = Image.Exif()
    exif[0x0110] = "prompt:" + json.dumps(prompt)
    tag = 0x010F
    for k, v in {"workflow": workflow}.items():
        exif[tag] = "{}:{}".format(k, json.dumps(v))
        tag -= 1
    return exif

prompt = {
    "3": {"inputs": {"seed": 12345, "steps": 20, "cfg": 7.5, "sampler_name": "euler",
                     "positive": ["6", 0], "model": ["4", 0]}, "class_type": "KSampler"},
    "6": {"inputs": {"text": "a cinematic shot of a girl, 4k & 8k, <lora:foo>, tags & more",
                     "clip": ["4", 1]}, "class_type": "CLIPTextEncode"},
}
workflow = {"nodes": [{"id": 3, "type": "KSampler", "title": "Sampler & Denoise"},
                      {"id": 6, "type": "CLIPTextEncode", "widgets_values": ["a & b"]}]}

exif = make_exif(prompt, workflow)

img = Image.new("RGB", (640, 384))
for x in range(640):
    for y in range(384):
        img.putpixel((x, y), (x * 255 // 640, y * 255 // 384, (x + y) % 256))

img.save("comfy_static_lossy.webp", lossless=False, quality=80, method=4, exif=exif)
img.save("comfy_static_lossless.webp", lossless=True, quality=100, method=4, exif=exif)

frames = []
for i in range(8):
    f = Image.new("RGB", (320, 240))
    for x in range(320):
        for y in range(240):
            f.putpixel((x, y), ((x + i * 40) % 256, y, (y + i * 30) % 256))
    frames.append(f)
frames[0].save("comfy_anim.webp", save_all=True, duration=125, append_images=frames[1:],
               lossless=True, quality=100, method=4, exif=exif)

Image.new("RGB", (64, 64), (200, 30, 30)).save("plain.webp", lossless=False, quality=80)

for f in sorted(os.listdir(".")):
    if f.endswith(".webp"):
        print(f, os.path.getsize(f))
