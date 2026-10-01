"""Builds the README animation: phone frames (Robolectric) next to PC frames
(preview captures), matched by their moment in DemoVoice's 6-second loop."""
import glob
import os
import sys

from PIL import Image, ImageDraw

phone_dir, pc_dir, out_webp, out_gif = sys.argv[1:5]
FPS, PERIOD = 15, 6.0
H = 600            # height of both screens in the animation
PAD, GAP = 32, 40
LAG = -0.06        # phone frames are shot a step after their level is set; this lines the waves up best
BORDER = (0x1B, 0x1E, 0x24)

pc = []
for f in glob.glob(os.path.join(pc_dir, '*.png')):
    t = float(os.path.basename(f)[:-4].split('_')[1])
    pc.append((t, f))


def pc_frame_at(t):
    def dist(x):
        d = abs(x - t) % PERIOD
        return min(d, PERIOD - d)
    return min(pc, key=lambda p: dist(p[0]))[1]


def rounded_mask(size, radius):
    m = Image.new('L', size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius, fill=255)
    return m


phones = sorted(glob.glob(os.path.join(phone_dir, '*.png')))
first = Image.open(phones[0])
pw = round(first.width * H / first.height)
first_pc = Image.open(pc[0][1])
cw = round(first_pc.width * H / first_pc.height)
W = PAD * 2 + pw + GAP + cw
HH = PAD * 2 + H
mask = rounded_mask((pw, H), 22)

frames = []
for k, f in enumerate(phones):
    t = k / FPS
    canvas = Image.new('RGB', (W, HH), (0, 0, 0))
    phone = Image.open(f).convert('RGB').resize((pw, H), Image.LANCZOS)
    canvas.paste(phone, (PAD, PAD), mask)
    d = ImageDraw.Draw(canvas)
    d.rounded_rectangle([PAD - 1, PAD - 1, PAD + pw, PAD + H], 23, outline=BORDER, width=2)
    win = Image.open(pc_frame_at((t - LAG) % PERIOD)).convert('RGB').resize((cw, H), Image.LANCZOS)
    x = PAD + pw + GAP
    canvas.paste(win, (x, PAD))
    d.rectangle([x - 1, PAD - 1, x + cw, PAD + H], outline=BORDER, width=1)
    frames.append(canvas)

ms = round(1000 / FPS)
frames[0].save(out_webp, save_all=True, append_images=frames[1:], duration=ms, loop=0, quality=82, method=6)

# GIF: one palette for every frame, so colours do not flicker from frame to frame.
sample = Image.new('RGB', (W, HH * 4))
for i, k in enumerate((10, 25, 40, 70)):
    sample.paste(frames[k], (0, HH * i))
palette = sample.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
gif = [fr.quantize(palette=palette, dither=Image.Dither.FLOYDSTEINBERG) for fr in frames]
gif[0].save(out_gif, save_all=True, append_images=gif[1:], duration=ms, loop=0, optimize=True, disposal=1)

for p in (out_webp, out_gif):
    print(os.path.basename(p), f'{os.path.getsize(p) / 1e6:.2f} MB', f'{W}x{HH}', len(frames), 'frames')
