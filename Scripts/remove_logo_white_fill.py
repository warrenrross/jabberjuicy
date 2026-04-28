"""
remove_logo_white_fill.py
--------------------------
Removes white/cream fill from the JabberJuicy logo letterforms so they render
cleanly on colored backgrounds (e.g., the orange navbar, hero gradient).

BACKGROUND
----------
The logo PNGs were exported from Canva with white inline strokes baked into the
letterforms of "JabberJuicy" and "CHAT. CONNECT. DELIGHT." These appear as white
rectangles against any non-white background. The files are already RGBA, but the
letterform fill pixels are fully opaque white rather than transparent.

Two passes were developed; this script documents and re-runs both.

PASS 1 — Simple threshold (run Apr 27, 2026)
---------------------------------------------
Any pixel with R >= 235 AND G >= 235 AND B >= 235 in the text region was made
transparent. This caught the most obvious white pixels but missed the warm-cream
fills (e.g., RGB 251, 246, 234) where the blue channel falls just below 235.

PASS 2 — Dark-green proximity masking (run Apr 27, 2026)
---------------------------------------------------------
For each remaining near-white pixel in the text region, scan outward in all
directions within RADIUS pixels. If any dark-green pixel (the letterform stroke
color, ~#2e5a3d) is found nearby, the pixel is inside a letterform and is made
transparent. Pixels not near dark green — such as white highlights on the dragon's
eyes, teeth, and cup rim — are left alone.

This approach is surgical: it uses the letterform's own stroke color as the
boundary detector rather than a blanket color threshold.

REGION BOUNDARIES
-----------------
The text sits in predictable regions of each image. Pixels outside these regions
are never examined, protecting the dragon illustration entirely.

  logo-primary-transparent.png  (605 x 452):  text region y >= 255 (below dragon)
  logo-horizontal-transparent.png (398 x 201): text region x >= 90  (right of dragon circle)

TUNING
------
If white fills remain after running:   increase RADIUS or lower WHITE_* thresholds
If dragon highlights are affected:     decrease RADIUS or tighten GREEN_* thresholds
"""

from PIL import Image
import numpy as np
import shutil
import os

# ── Paths ──────────────────────────────────────────────────────────────────────

SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
LOGO_SRC     = os.path.join(PROJECT_ROOT, 'Branding', 'web-assets', 'logos')
LOGO_WWWROOT = os.path.join(PROJECT_ROOT, 'WebAppWithDB_Starter_v2',
                            'WebAppWithDB_Starter_v2', 'wwwroot', 'brand', 'logos')

# ── Tunable parameters ─────────────────────────────────────────────────────────

# Pass 1 — strict white threshold (applied first, fast)
PASS1_THRESHOLD = 235          # all three channels must meet or exceed this

# Pass 2 — near-white definition (permissive, catches warm/cream fills)
WHITE_R_MIN = 200
WHITE_G_MIN = 195
WHITE_B_MIN = 185

# Pass 2 — dark green detection (letterform stroke color ~#2e5a3d = RGB 46, 90, 61)
GREEN_R_MAX = 110              # low red
GREEN_G_MIN = 50               # meaningful green
GREEN_B_MAX = 110              # low blue
# green must also be the dominant channel (G > R and G > B)

# Pass 2 — neighborhood search radius in pixels
RADIUS = 15

# ── Logo definitions: (filename, text_region_box) ─────────────────────────────
# box = (x1, y1, x2, y2) — only pixels inside this box are examined

LOGOS = [
    ('logo-primary-transparent.png',    (0,  255, 605, 452)),
    ('logo-horizontal-transparent.png', (90,   0, 398, 201)),
]

# ── Processing ─────────────────────────────────────────────────────────────────

def process(filename, box):
    src_path = os.path.join(LOGO_SRC, filename)
    img = Image.open(src_path).convert('RGBA')
    arr = np.array(img, dtype=np.int32)
    R, G, B, A = arr[:,:,0], arr[:,:,1], arr[:,:,2], arr[:,:,3]
    h, w = R.shape
    x1, y1, x2, y2 = box

    # Region mask — only examine pixels inside the text area
    region = np.zeros((h, w), dtype=bool)
    region[y1:y2, x1:x2] = True

    # ── Pass 1: strict brightness threshold ──────────────────────────────────
    pass1_white = (A > 0) & (R >= PASS1_THRESHOLD) & (G >= PASS1_THRESHOLD) & (B >= PASS1_THRESHOLD)
    target1 = pass1_white & region
    arr[target1, 3] = 0
    A = arr[:,:,3]   # refresh alpha after pass 1

    # ── Pass 2: near-white pixels that are adjacent to dark green ─────────────

    # Build boolean mask of dark-green pixels across the entire image
    green_mask = (
        (A > 0) &
        (R < GREEN_R_MAX) &
        (G > GREEN_G_MIN) &
        (B < GREEN_B_MAX) &
        (R < G) &          # green is dominant over red
        (B < G)            # green is dominant over blue
    ).astype(np.uint8)

    # Dilate the green mask: for each pixel, check if any green neighbor exists
    # within RADIUS. Implemented as a sliding OR over a (2R+1)x(2R+1) window.
    padded = np.pad(green_mask, RADIUS, mode='constant', constant_values=0)
    near_green = np.zeros((h, w), dtype=bool)
    for dy in range(2 * RADIUS + 1):
        for dx in range(2 * RADIUS + 1):
            near_green |= padded[dy:dy+h, dx:dx+w].astype(bool)

    # Near-white pixel definition (permissive — catches warm cream fills)
    near_white = (A > 0) & (R >= WHITE_R_MIN) & (G >= WHITE_G_MIN) & (B >= WHITE_B_MIN)

    # Only remove near-white pixels that are both inside the text region
    # and have dark green in their neighborhood (i.e., inside a letterform)
    target2 = near_white & near_green & region
    arr[target2, 3] = 0

    total_changed = int(target1.sum()) + int(target2.sum())

    # Save modified image back to source and copy to wwwroot
    result = Image.fromarray(arr.astype(np.uint8))
    result.save(src_path)
    dst_path = os.path.join(LOGO_WWWROOT, filename)
    if os.path.isdir(LOGO_WWWROOT):
        shutil.copy(src_path, dst_path)
        print(f'{filename}: {total_changed} pixels made transparent  →  copied to wwwroot')
    else:
        print(f'{filename}: {total_changed} pixels made transparent  (wwwroot not found, skipped copy)')

if __name__ == '__main__':
    for filename, box in LOGOS:
        process(filename, box)
    print('Done. Verify visuals, then commit and push.')
