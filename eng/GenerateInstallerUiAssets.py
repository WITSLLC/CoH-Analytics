"""Generate WiX installer UI bitmaps from logo-hero.png."""
from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parents[1]
LOGO_PATH = REPO / "src/CoHAnalytics/Assets/Images/Header/logo-hero.png"
ASSETS = REPO / "installer/CoHAnalytics.Installer/Assets"

NAVY = (13, 34, 64, 255)
WHITE = (255, 255, 255)


def fit_logo(logo: Image.Image, panel_w: int, panel_h: int) -> tuple[Image.Image, int, int]:
    # Trim transparent padding so the mark sits visually centered in the panel.
    alpha = logo.split()[-1]
    bbox = alpha.getbbox()
    if bbox:
        logo = logo.crop(bbox)

    pad_x = int(panel_w * 0.10)
    pad_y = int(panel_h * 0.10)
    box_w = panel_w - 2 * pad_x
    box_h = panel_h - 2 * pad_y
    lw, lh = logo.size
    scale = min(box_w / lw, box_h / lh)
    nw = max(1, int(lw * scale))
    nh = max(1, int(lh * scale))
    resized = logo.resize((nw, nh), Image.Resampling.LANCZOS)
    x = (panel_w - nw) // 2
    y = (panel_h - nh) // 2
    return resized, x, y


def make_left_panel(logo: Image.Image, w: int, h: int) -> Image.Image:
    img = Image.new("RGBA", (w, h), NAVY)
    resized, x, y = fit_logo(logo, w, h)
    img.alpha_composite(resized, (x, y))
    return img.convert("RGB")


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    logo = Image.open(LOGO_PATH).convert("RGBA")

    left = make_left_panel(logo, 160, 312)
    left_path = ASSETS / "installer-left-panel.bmp"
    left.save(left_path, format="BMP")
    print(f"wrote {left_path} {left.size}")

    dialog = Image.new("RGB", (493, 312), WHITE)
    dialog.paste(make_left_panel(logo, 180, 312), (0, 0))
    draw = ImageDraw.Draw(dialog)
    draw.rectangle([180, 0, 492, 311], fill=(252, 253, 255))
    dialog_path = ASSETS / "installer-dialog.bmp"
    dialog.save(dialog_path, format="BMP")
    print(f"wrote {dialog_path} {dialog.size}")

    banner = Image.new("RGB", (493, 58), (252, 253, 255))
    banner_path = ASSETS / "installer-banner.bmp"
    banner.save(banner_path, format="BMP")
    print(f"wrote {banner_path} {banner.size}")

    for old in ("WixUIBanner.bmp", "WixUIDialog.bmp"):
        path = ASSETS / old
        if path.exists():
            path.unlink()
            print(f"removed {path}")


if __name__ == "__main__":
    main()
