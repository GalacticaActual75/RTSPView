from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
source = Image.open(root / "assets" / "branding" / "rtspview-icon.png").convert("RGBA")
bounds = source.getbbox()
if not bounds:
    raise SystemExit("Brand source has no visible pixels")
mark = source.crop(bounds)
side = max(mark.size)
padding = max(8, round(side * 0.055))
canvas = Image.new("RGBA", (side + padding * 2, side + padding * 2))
canvas.alpha_composite(mark, ((canvas.width - mark.width) // 2, (canvas.height - mark.height) // 2))

def resized(size: int) -> Image.Image:
    return canvas.resize((size, size), Image.Resampling.LANCZOS)

viewer_assets = root / "src" / "RTSPView.Viewer" / "Assets"
controller_assets = root / "src" / "RTSPView.Controller" / "Assets"
web = root / "src" / "RTSPView.Controller" / "wwwroot"
for directory in (viewer_assets, controller_assets, web):
    directory.mkdir(parents=True, exist_ok=True)

ico_sizes = [16, 24, 32, 48, 64, 128, 256]
resized(256).save(viewer_assets / "RTSPView.ico", format="ICO", sizes=[(size, size) for size in ico_sizes])
resized(256).save(controller_assets / "RTSPView.ico", format="ICO", sizes=[(size, size) for size in ico_sizes])
resized(48).save(web / "favicon.ico", format="ICO", sizes=[(16, 16), (32, 32), (48, 48)])
for size, name in ((16, "favicon-16.png"), (32, "favicon-32.png"), (180, "apple-touch-icon.png"), (192, "icon-192.png"), (512, "icon-512.png")):
    resized(size).save(web / name, optimize=True)

print("Generated RTSPView Windows and web icon assets")
