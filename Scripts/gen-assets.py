#!/usr/bin/env python3
"""从一张正方形源图（如 1024x1024）生成 WinUI 3 / Windows App SDK 的 8 个 Assets PNG。

用法:
    python scripts/gen-assets.py <源图路径.png>

源图应为正方形（推荐 1024x1024），且最好带透明背景，
这样 *_altform-unplated / *_lightunplated 和 SplashScreen 透明底的图才能正确显示。
AppIcon.ico 已存在，本脚本不处理。
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP_DIR = os.path.join(ROOT, "WinUIClash")

# (相对输出路径, 宽, 高)
TARGETS = [
    ("Assets/Square150x150Logo.scale-200.png", 300, 300),
    ("Assets/Square44x44Logo.scale-200.png", 88, 88),
    ("Assets/Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24),
    ("Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png", 48, 48),
    ("Assets/LockScreenLogo.scale-200.png", 48, 48),
    ("Assets/StoreLogo.png", 50, 50),
    ("Assets/Wide310x150Logo.scale-200.png", 620, 300),
    ("Assets/SplashScreen.scale-200.png", 1240, 600),
]


def generate(src_path: str) -> None:
    src = Image.open(src_path).convert("RGBA")
    sw, sh = src.size
    print(f"源图: {src_path} ({sw}x{sh})")

    for rel, w, h in TARGETS:
        out = os.path.join(APP_DIR, rel)
        os.makedirs(os.path.dirname(out), exist_ok=True)

        if w == h:
            # 正方形目标：直接缩放（源图假定为正方形，不变形）
            im = src.resize((w, h), Image.LANCZOS)
        else:
            # 非正方形目标：等比缩放后居中贴在透明画布上
            scale = min(w / sw, h / sh)
            nw, nh = max(1, round(sw * scale)), max(1, round(sh * scale))
            resized = src.resize((nw, nh), Image.LANCZOS)
            im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            im.paste(resized, ((w - nw) // 2, (h - nh) // 2), resized)

        im.save(out)
        print(f"  -> {rel} ({w}x{h})")

    print("完成。AppIcon.ico 未改动。")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("用法: python scripts/gen-assets.py <源图.png>")
        sys.exit(1)
    generate(sys.argv[1])
