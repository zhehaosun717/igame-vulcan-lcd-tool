"""助手 B 状态动画（480x128）：abwork 工作中 / abdone 完成 / abask 需要确认。
输出 GIF，再用 GpuLcd.exe upload 转成 .savi 传到显卡屏幕。"""
import math, os, sys
from PIL import Image, ImageDraw, ImageFont

W, H = 480, 128
BG = (20, 20, 19)
ORANGE = (217, 119, 87)       # 助手B 橙
GREEN = (120, 200, 120)
AMBER = (240, 180, 60)
TEXT = (240, 238, 230)
SUB = (150, 148, 140)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
FONT_B = r"C:\Windows\Fonts\msyhbd.ttc"
FONT_R = r"C:\Windows\Fonts\msyh.ttc"
big = ImageFont.truetype(FONT_B, 34)
small = ImageFont.truetype(FONT_R, 16)
S = 3  # 超采样，画完缩小，边缘更平滑


def canvas():
    im = Image.new("RGB", (W * S, H * S), BG)
    return im, ImageDraw.Draw(im)


def finish(im):
    return im.resize((W, H), Image.LANCZOS)


def spark(d, cx, cy, r, angle, color, rays=12, width=0.16):
    """助手B 风格的星芒：一圈细长的花瓣。"""
    for i in range(rays):
        a = angle + i * 2 * math.pi / rays
        ln = r * (1.0 if i % 2 == 0 else 0.72)
        tip = (cx + math.cos(a) * ln, cy + math.sin(a) * ln)
        side = width * r
        p1 = (cx + math.cos(a + math.pi / 2) * side * 0.5, cy + math.sin(a + math.pi / 2) * side * 0.5)
        p2 = (cx + math.cos(a - math.pi / 2) * side * 0.5, cy + math.sin(a - math.pi / 2) * side * 0.5)
        d.polygon([p1, tip, p2, (cx, cy)], fill=color)


def text(d, xy, s, font, fill):
    x, y = xy
    d.text((x * S, y * S), s, font=ImageFont.truetype(font.path, int(font.size * S)), fill=fill)


def save(frames, name, ms):
    path = os.path.join(OUT, name)
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=ms, loop=0, optimize=False, disposal=2)
    print(path, len(frames), "frames")


# ---------- 工作中：星芒旋转 + 省略号
frames = []
N = 36
for f in range(N):
    im, d = canvas()
    t = f / N
    pulse = 1 + 0.06 * math.sin(t * 2 * math.pi * 2)
    spark(d, 68 * S, 64 * S, 40 * S * pulse, t * 2 * math.pi / 6, ORANGE)
    dots = "." * (1 + (f // 9) % 3)
    text(d, (130, 26), "助手B 正在工作" + dots, big, TEXT)
    text(d, (132, 80), "助手 B · working", small, SUB)
    frames.append(finish(im))
save(frames, "abwork.gif", 70)

# ---------- 完成：勾逐笔画出，然后停住
frames = []
N = 30
for f in range(N):
    im, d = canvas()
    cx, cy, r = 68 * S, 64 * S, 40 * S
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=GREEN, width=5 * S)
    p = min(1.0, f / 14)
    a, b, c = (cx - 20 * S, cy + 2 * S), (cx - 5 * S, cy + 17 * S), (cx + 22 * S, cy - 16 * S)
    if p > 0:
        if p <= 0.4:
            k = p / 0.4
            d.line([a, (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k)], fill=GREEN, width=8 * S)
        else:
            k = (p - 0.4) / 0.6
            d.line([a, b, (b[0] + (c[0] - b[0]) * k, b[1] + (c[1] - b[1]) * k)], fill=GREEN, width=8 * S, joint="curve")
    text(d, (130, 26), "任务完成", big, TEXT)
    text(d, (132, 80), "助手 B · done", small, SUB)
    frames.append(finish(im))
frames += [frames[-1]] * 30   # 停留
save(frames, "abdone.gif", 70)

# ---------- 需要确认：琥珀色边框呼吸闪烁
frames = []
N = 24
for f in range(N):
    im, d = canvas()
    k = 0.5 + 0.5 * math.sin(f / N * 2 * math.pi)
    col = tuple(int(BG[i] + (AMBER[i] - BG[i]) * (0.35 + 0.65 * k)) for i in range(3))
    d.rounded_rectangle([3 * S, 3 * S, (W - 4) * S, (H - 4) * S], radius=14 * S, outline=col, width=5 * S)
    cx, cy = 68 * S, 64 * S
    d.ellipse([cx - 36 * S, cy - 36 * S, cx + 36 * S, cy + 36 * S], fill=col)
    d.rectangle([cx - 5 * S, cy - 24 * S, cx + 5 * S, cy + 8 * S], fill=BG)
    d.ellipse([cx - 6 * S, cy + 14 * S, cx + 6 * S, cy + 26 * S], fill=BG)
    text(d, (130, 26), "需要你确认", big, TEXT)
    text(d, (132, 80), "助手 B · waiting for you", small, SUB)
    frames.append(finish(im))
save(frames, "abask.gif", 80)
