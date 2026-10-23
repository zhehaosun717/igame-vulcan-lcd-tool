"""DeepSeek Harness 状态动画（480x128）：dswork 工作中 / dsdone 完成 / dsask 待操作。
输出 GIF，再用 GpuLcd.exe upload 转成 .savi 传到显卡屏幕。配色与点阵屏一致：
工作深蓝 #4D6BFE、等待金黄 #FF9F45、完成青绿 #2ED3B7。"""
import math, os, sys
from PIL import Image, ImageDraw, ImageFont

W, H = 480, 128
BG = (16, 18, 28)
BLUE = (77, 107, 254)
AMBER = (255, 159, 69)
TEAL = (46, 211, 183)
TEXT = (238, 241, 250)
SUB = (150, 158, 180)
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
FONT_B = r"C:\Windows\Fonts\msyhbd.ttc"
FONT_R = r"C:\Windows\Fonts\msyh.ttc"
big = ImageFont.truetype(FONT_B, 34)
small = ImageFont.truetype(FONT_R, 16)
S = 3


def canvas():
    im = Image.new("RGB", (W * S, H * S), BG)
    return im, ImageDraw.Draw(im)


def finish(im):
    return im.resize((W, H), Image.LANCZOS)


def text(d, xy, s, font, fill):
    x, y = xy
    d.text((x * S, y * S), s, font=ImageFont.truetype(font.path, int(font.size * S)), fill=fill)


def whale(d, cx, cy, r, phase, color):
    """一条朝左的鲸鱼（DSH 的标识造型）：圆头 + 上扬的尾巴 + 喷气。"""
    x, y = cx * S, cy * S
    rr = r * S
    d.ellipse([x - rr, y - rr * 0.8, x + rr * 0.55, y + rr * 0.8], fill=color)
    d.polygon([(x + rr * 0.35, y), (x + rr * 1.05, y - rr * 0.75), (x + rr * 1.05, y + rr * 0.55)], fill=color)
    lift = rr * (0.55 + 0.12 * math.sin(phase * math.tau))
    d.ellipse([x + rr * 0.72, y - lift - rr * 0.16, x + rr * 1.06, y - lift + rr * 0.10], fill=color)
    for i in range(3):
        a = phase * math.tau + i * 2.1
        sx, sy = x - rr * 0.25 + math.cos(a) * rr * 0.22, y - rr * 0.95 - abs(math.sin(a)) * rr * 0.45
        d.ellipse([sx - 3 * S, sy - 3 * S, sx + 3 * S, sy + 3 * S], fill=color)
    d.ellipse([x - rr * 0.72, y - rr * 0.2, x - rr * 0.52, y], fill=BG)


def save(frames, name, ms):
    path = os.path.join(OUT, name)
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=ms, loop=0, optimize=False, disposal=2)
    print(path, len(frames), "frames")


# ---------- 工作中：鲸鱼游动 + 呼吸光晕 + 省略号
frames = []
N = 36
for f in range(N):
    im, d = canvas()
    t = f / N
    glow = tuple(int(BG[i] + (BLUE[i] - BG[i]) * (0.10 + 0.10 * math.sin(t * math.tau * 2))) for i in range(3))
    d.rounded_rectangle([6 * S, 6 * S, (W - 7) * S, (H - 7) * S], radius=14 * S, outline=glow, width=3 * S)
    whale(d, 68, 66 + 2 * math.sin(t * math.tau), 38, t * 2, BLUE)
    dots = "." * (1 + (f // 9) % 3)
    text(d, (130, 26), "DeepSeek 正在工作" + dots, big, TEXT)
    text(d, (132, 80), "DeepSeek Harness · working", small, SUB)
    frames.append(finish(im))
save(frames, "dswork.gif", 70)

# ---------- 完成：青绿对勾逐笔画出后停住
frames = []
N = 30
for f in range(N):
    im, d = canvas()
    cx, cy, r = 68 * S, 64 * S, 40 * S
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=TEAL, width=5 * S)
    p = min(1.0, f / 14)
    a, b, c = (cx - 20 * S, cy + 2 * S), (cx - 5 * S, cy + 17 * S), (cx + 22 * S, cy - 16 * S)
    if p > 0:
        if p <= 0.4:
            k = p / 0.4
            d.line([a, (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k)], fill=TEAL, width=8 * S)
        else:
            k = (p - 0.4) / 0.6
            d.line([a, b, (b[0] + (c[0] - b[0]) * k, b[1] + (c[1] - b[1]) * k)], fill=TEAL, width=8 * S, joint="curve")
    text(d, (130, 26), "本轮完成", big, TEXT)
    text(d, (132, 80), "DeepSeek Harness · done", small, SUB)
    frames.append(finish(im))
frames += [frames[-1]] * 30
save(frames, "dsdone.gif", 70)

# ---------- 待操作：金黄边框呼吸 + 感叹号
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
    text(d, (132, 80), "DeepSeek Harness · waiting for you", small, SUB)
    frames.append(finish(im))
save(frames, "dsask.gif", 80)
