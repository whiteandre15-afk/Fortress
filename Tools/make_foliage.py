"""Makes the foliage textures the game builds its plants from, out of the downloaded scans:

  Art/Branch/Branch_albedo.png  one conifer branch: real pine twigs (cut from Poly Haven's
                                pine_tree_01 atlas) fanned along a bark stem, with alpha
  Art/GrassTuft/GrassTuft_<n>_albedo.png  photographed grass for the terrain: the ready-made
                                tufts in Poly Haven's grass_medium_01 atlas, plus taller clumps
                                fanned from the single blades in grass_medium_02

Run after fetch_assets.py, from the project root:  python3 Tools/make_foliage.py
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageStat

ART = "Assets/Fortress/Art"
random.seed(11)


def islands(mask, smallest):
    """Every connected opaque area of a black/white mask with at least `smallest` pixels, as
    (bounding box, the island's own pixels as a mask), so a crop never picks up its neighbours."""
    w, h = mask.size
    pixels = mask.load()
    seen = bytearray(w * h)
    found = []
    for start in range(w * h):
        if seen[start] or not pixels[start % w, start // w]:
            continue
        stack, count, members = [start], 0, []
        seen[start] = 1
        x0, y0, x1, y1 = w, h, 0, 0
        while stack:
            i = stack.pop()
            count += 1
            members.append(i)
            x, y = i % w, i // w
            x0, y0, x1, y1 = min(x0, x), min(y0, y), max(x1, x), max(y1, y)
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                j = ny * w + nx
                if 0 <= nx < w and 0 <= ny < h and not seen[j] and pixels[nx, ny]:
                    seen[j] = 1
                    stack.append(j)
        if count >= smallest:
            own = Image.new("L", (w, h), 0)
            out = own.load()
            for i in members:
                out[i % w, i // w] = 255
            box = (x0, y0, x1 + 1, y1 + 1)
            found.append((box, own.crop(box)))
    return found


def cut(image, box, own):
    """`image` cropped to `box`, keeping only the pixels of the island `own`."""
    piece = image.crop(box)
    alpha = Image.composite(piece.getchannel("A"), Image.new("L", piece.size, 0), own)
    piece.putalpha(alpha)
    return piece


def largest_island(mask):
    """Keeps only the biggest connected opaque area of a black/white mask. The atlas packs bark
    scraps right up against the twigs, and a crop rectangle always catches some of them."""
    w, h = mask.size
    pixels = mask.load()
    seen = bytearray(w * h)
    best = []
    for start in range(w * h):
        if seen[start] or not pixels[start % w, start // w]:
            continue
        island, stack = [], [start]
        seen[start] = 1
        while stack:
            i = stack.pop()
            island.append(i)
            x, y = i % w, i // w
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                j = ny * w + nx
                if 0 <= nx < w and 0 <= ny < h and not seen[j] and pixels[nx, ny]:
                    seen[j] = 1
                    stack.append(j)
        if len(island) > len(best):
            best = island
    kept = Image.new("L", (w, h), 0)
    out = kept.load()
    for i in best:
        out[i % w, i // w] = 255
    return kept


def twig_cutouts():
    """The two needle twigs in the pine atlas, cropped to their alpha, pointing right."""
    atlas = Image.open(f"{ART}/Twigs/Twigs_albedo.png").convert("RGBA")
    w, h = atlas.size
    # Regions found by eye on the 2048 atlas: an upright twig top-left, a slanted one mid-right.
    regions = [(0.0, 0.0, 0.22, 0.45), (0.64, 0.33, 1.0, 0.54)]
    twigs = []
    for x0, y0, x1, y1 in regions:
        crop = atlas.crop((int(x0 * w), int(y0 * h), int(x1 * w), int(y1 * h)))
        alpha = largest_island(crop.getchannel("A").point(lambda a: 255 if a > 140 else 0))
        crop.putalpha(alpha)
        crop = crop.crop(alpha.getbbox())
        twigs.append(crop)
    # The first twig grows upward; turn it to grow right like the other.
    twigs[0] = twigs[0].rotate(-90, expand=True)
    return twigs


def branch(twigs):
    """A branch 4:1 wide, growing from its left edge, its stem along the middle."""
    width, height = 2048, 512
    card = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    bark = Image.open(f"{ART}/Bark/Bark_albedo.png").convert("RGB")
    stem = ImageDraw.Draw(card)
    # The stem tapers from the trunk out to the tip, tinted from the bark scan.
    stem_colour = tuple(int(c * 0.8) for c in bark.resize((1, 1)).getpixel((0, 0)))
    for x in range(0, int(width * 0.92)):
        thickness = 14 * (1 - x / width) + 3
        stem.line([(x, height / 2 - thickness / 2), (x, height / 2 + thickness / 2)], fill=stem_colour + (255,))
    # Twigs sprout along both sides, angled out toward the tip, shorter near the end.
    for i in range(46):
        along = random.uniform(0.04, 0.95)
        twig = random.choice(twigs)
        length = int(width * random.uniform(0.18, 0.3) * (1.15 - along * 0.6))
        scale = length / twig.width
        piece = twig.resize((length, max(8, int(twig.height * scale))), Image.LANCZOS)
        side = random.choice((-1, 1))
        angle = side * random.uniform(28, 62)
        piece = piece.rotate(angle, expand=True, resample=Image.BICUBIC)
        # Darken the inner, older twigs a little for depth.
        shade = 0.75 + 0.25 * along
        r, g, b, a = piece.split()
        piece = Image.merge("RGBA", [c.point(lambda v: int(v * shade)) for c in (r, g, b)] + [a])
        x = int(along * width * 0.92)
        y = height // 2 - (piece.height if side > 0 else 0)
        card.alpha_composite(piece, (min(max(x, 0), width - piece.width), min(max(y, 0), height - piece.height)))
    # A twig at the very tip.
    tip = twigs[1].resize((int(width * 0.22), int(twigs[1].height * width * 0.22 / twigs[1].width)), Image.LANCZOS)
    card.alpha_composite(tip, (width - tip.width, max(0, height // 2 - tip.height // 2)))
    return card


def opaque(image):
    return image.getchannel("A").point(lambda a: 255 if a > 120 else 0)


def ready_tufts():
    """The photographed tufts along the bottom of the grass_medium_01 atlas."""
    atlas = Image.open(f"{ART}/GrassClumpsA/GrassClumpsA_albedo.png").convert("RGBA")
    w, h = atlas.size
    band = atlas.crop((0, int(h * 0.72), w, h))
    tufts = []
    for box, own in islands(opaque(band), smallest=band.width * band.height // 60):
        tuft = cut(band, box, own)
        # Only tufts, not the stray leaf: a tuft is a good size and not a long flat strip.
        if tuft.width > band.width * 0.1 and tuft.height > tuft.width * 0.35:
            # The scan's tufts are shot in shade; lift them to sit with the sunlit meadow.
            r, g, b, a = trim_scraps(tuft).split()
            lifted = [r.point(lambda v: min(255, int(v * 1.45))), g.point(lambda v: min(255, int(v * 1.4))),
                      b.point(lambda v: min(255, int(v * 1.2)))]
            tufts.append(Image.merge("RGBA", lifted + [a]))
    return tufts


def trim_scraps(tuft):
    """Cuts off scraps hanging below a tuft. A tuft's base is its densest row of leaves; anything
    more than a little below that is atlas leftovers."""
    alpha = tuft.getchannel("A").load()
    w, h = tuft.size
    coverage = [sum(1 for x in range(w) if alpha[x, y] > 120) for y in range(h)]
    # The densest row in the lower half: the base of the tuft.
    lower = range(h // 2, h)
    base = max(lower, key=lambda y: coverage[y])
    return tuft.crop((0, 0, w, min(h, base + max(2, h // 25))))


def blades():
    """The single grass blades photographed in the grass_medium_02 atlas, standing upright."""
    atlas = Image.open(f"{ART}/GrassClumpsB/GrassClumpsB_albedo.png").convert("RGBA")
    found = []
    for box, own in islands(opaque(atlas), smallest=atlas.width * atlas.height // 2000):
        blade = cut(atlas, box, own)
        if blade.height < blade.width:
            blade = blade.rotate(90, expand=True)
        if blade.height < atlas.height * 0.25:
            continue
        # Green blades only: the pale, dried ones read as sticks in a clump.
        r, g, b = ImageStat.Stat(blade.convert("RGB"), mask=blade.getchannel("A")).mean
        if g > r * 1.02:
            found.append(blade)
    return found


def fanned_clump(blade_images, count):
    """Real blades fanned out from one root into a clump, bottom-centre."""
    width, height = 512, 512
    clump = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for _ in range(count):
        blade = random.choice(blade_images)
        tall = int(height * random.uniform(0.4, 0.95))
        # Half as wide as the scan's proportions: fine meadow grass rather than reeds.
        piece = blade.resize((max(4, int(blade.width * tall / blade.height * 0.5)), tall), Image.LANCZOS)
        # A little lighter or darker each, like blades catching the light differently.
        shade = random.uniform(0.75, 1.05)
        r, g, b, a = piece.split()
        piece = Image.merge("RGBA", [c.point(lambda v: min(255, int(v * shade))) for c in (r, g, b)] + [a])
        # Splay out from the root: rotate about the blade's base, not its middle.
        stage = Image.new("RGBA", (tall * 2, tall * 2), (0, 0, 0, 0))
        stage.alpha_composite(piece, (tall - piece.width // 2, tall - piece.height))
        stage = stage.rotate(random.uniform(-40, 40), center=(tall, tall), resample=Image.BICUBIC)
        root = width // 2 + random.randint(-35, 35)
        clump.alpha_composite(stage.crop((0, 0, tall * 2, tall)), (root - tall, height - tall))
    return clump


def grass():
    """Writes every grass variant, each on a square canvas with its roots on the bottom edge."""
    folder = f"{ART}/GrassTuft"
    for old in os.listdir(folder):
        if old.startswith("GrassTuft") and old.endswith(".png"):
            os.remove(f"{folder}/{old}")
    variants = ready_tufts()
    found = blades()
    variants += [fanned_clump(found, random.randint(40, 60)) for _ in range(3)]
    for index, variant in enumerate(variants):
        side = max(variant.width, variant.height)
        square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        square.alpha_composite(variant, ((side - variant.width) // 2, side - variant.height))
        square.resize((512, 512), Image.LANCZOS).save(f"{folder}/GrassTuft_{index}_albedo.png")
    print(f"grass: {len(variants)} variants ({len(variants) - 3} photographed tufts, 3 fanned from {len(found)} blades)")


def main():
    os.makedirs(f"{ART}/Branch", exist_ok=True)
    os.makedirs(f"{ART}/GrassTuft", exist_ok=True)
    branch(twig_cutouts()).save(f"{ART}/Branch/Branch_albedo.png")
    grass()
    print("done")


if __name__ == "__main__":
    main()
