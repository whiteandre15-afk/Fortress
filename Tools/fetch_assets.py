"""Downloads the game's photo-scanned art from Poly Haven (all CC0) into Assets/Fortress/Art,
repacked into the maps Unity's URP Lit shader reads:

  <name>_albedo   colour (with alpha for foliage)
  <name>_normal   OpenGL-style normal map, which matches Unity
  <name>_mask     R metallic, G ambient occlusion, A smoothness (1 - roughness);
                  used as both URP's metallic/smoothness map and its occlusion map (which reads G)

Run from the project root:  python3 Tools/fetch_assets.py
Re-running skips anything already downloaded.
"""
import io
import json
import os
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from PIL import Image

ART = "Assets/Fortress/Art"
API = "https://api.polyhaven.com"

# name in the game -> (Poly Haven asset, resolution, map prefix within the asset or "")
SURFACES = {
    "Brick": ("brick_wall_001", "2k", ""),
    "Planks": ("brown_planks_05", "2k", ""),
    "Metal": ("rusty_corrugated_iron", "2k", ""),
    "Steel": ("rusty_metal_02", "1k", ""),
    "Grass": ("leafy_grass", "2k", ""),
    "ForestFloor": ("forest_ground_04", "2k", ""),
    "RockGround": ("aerial_rocks_02", "2k", ""),
    "Mud": ("brown_mud_02", "1k", ""),
    "Bark": ("pine_tree_01", "1k", "bark_"),
    "Twigs": ("pine_tree_01", "2k", "twig_"),
}
# name in the game -> (Poly Haven model, resolution, [map prefixes: one material each])
MODELS = {
    "Rifle": ("bolt_action_rifle_7_62", "2k", ["", "accesories_"]),
    "Crate": ("old_military_crate", "1k", [""]),
    "Car": ("covered_car", "1k", [""]),
    "Rock07": ("rock_07", "1k", [""]),
    "Rock09": ("rock_09", "1k", [""]),
    "Fern": ("fern_02", "1k", [""]),
    # Heavy scans: Tools/reduce_models.py cuts these down to game-ready detail levels.
    "MossRocksA": ("rock_moss_set_01", "1k", [""]),
    "MossRocksB": ("rock_moss_set_02", "1k", [""]),
    "Boulder02": ("namaqualand_boulder_02", "1k", [""]),
    "Boulder03": ("namaqualand_boulder_03", "1k", [""]),
    "Boulder04": ("namaqualand_boulder_04", "1k", [""]),
    "Boulder06": ("namaqualand_boulder_06", "1k", [""]),
    "GrassClumpsA": ("grass_medium_01", "1k", [""]),
    "GrassClumpsB": ("grass_medium_02", "1k", [""]),
}
SKY = ("kloofendal_48d_partly_cloudy_puresky", "2k")


def get(url):
    # Poly Haven turns away Python's default user agent and asks scripts to name themselves.
    request = urllib.request.Request(url, headers={"User-Agent": "FortressPC-asset-fetch/1.0"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def save(path, url):
    """Downloads to a temporary name and renames on success, so a failed download never leaves
    an empty file behind that later runs would mistake for done."""
    data = get(url)
    with open(path + ".part", "wb") as out:
        out.write(data)
    os.replace(path + ".part", path)


def done(path):
    return os.path.exists(path) and os.path.getsize(path) > 0


def files(asset):
    return json.loads(get(f"{API}/files/{asset}"))


def pick(entry, resolution, formats):
    """The URL of a map at `resolution` in the first available format."""
    by_res = entry.get(resolution) or entry[sorted(entry)[0]]
    for fmt in formats:
        if fmt in by_res:
            return by_res[fmt]["url"]
    raise KeyError(f"none of {formats} in {list(by_res)}")


def find(listing, prefix, *keys):
    """The listing entry for a map, trying Poly Haven's different key spellings."""
    for key in keys:
        for candidate in (prefix + key, prefix + key.lower(), key if not prefix else None):
            if candidate and candidate in listing:
                return listing[candidate]
    return None


def make_surface(name, folder, listing, resolution, prefix):
    albedo_path = f"{folder}/{name}_albedo.png"
    if done(albedo_path):
        return
    diffuse = find(listing, prefix, "Diffuse", "diff")
    normal = find(listing, prefix, "nor_gl")
    arm = find(listing, prefix, "arm")
    alpha = find(listing, prefix, "Alpha", "alpha")
    with ThreadPoolExecutor(4) as pool:
        jobs = {
            "diffuse": pool.submit(get, pick(diffuse, resolution, ["jpg", "png"])),
            "normal": pool.submit(get, pick(normal, resolution, ["png", "jpg"])),
            "arm": pool.submit(get, pick(arm, resolution, ["png", "jpg"])) if arm else None,
            "alpha": pool.submit(get, pick(alpha, resolution, ["png", "jpg"])) if alpha else None,
        }
        data = {key: job.result() if job else None for key, job in jobs.items()}

    colour = Image.open(io.BytesIO(data["diffuse"])).convert("RGB")
    if data["alpha"]:
        cut = Image.open(io.BytesIO(data["alpha"])).convert("L").resize(colour.size)
        colour = colour.convert("RGBA")
        colour.putalpha(cut)
    colour.save(albedo_path)

    normal_image = Image.open(io.BytesIO(data["normal"]))
    if normal_image.mode not in ("RGB", "RGBA"):
        # 16-bit PNGs open as "I;16" per channel sets; normalise to 8-bit RGB.
        normal_image = normal_image.convert("RGB")
    normal_image.convert("RGB").save(f"{folder}/{name}_normal.png")

    if data["arm"]:
        ao, rough, metal = Image.open(io.BytesIO(data["arm"])).convert("RGB").split()
        smooth = rough.point(lambda value: 255 - value)
        Image.merge("RGBA", (metal, ao, Image.new("L", ao.size, 0), smooth)).save(f"{folder}/{name}_mask.png")
    print("  made", name)


def fetch_surface(name, spec):
    asset, resolution, prefix = spec
    folder = f"{ART}/{name}"
    os.makedirs(folder, exist_ok=True)
    make_surface(name, folder, files(asset), resolution, prefix)
    return name, asset


def fetch_model(name, spec):
    asset, resolution, prefixes = spec
    folder = f"{ART}/{name}"
    os.makedirs(folder, exist_ok=True)
    listing = files(asset)
    fbx_path = f"{folder}/{name}.fbx"
    # Heavy scans live on in ArtSource/ once Tools/reduce_models.py has cut them down.
    if not done(fbx_path) and not done(f"ArtSource/{name}.fbx"):
        save(fbx_path, pick(listing["fbx"], resolution, ["fbx"]))
    for prefix in prefixes:
        part = name + ("" if not prefix else "_" + prefix.strip("_").capitalize())
        make_surface(part, folder, listing, resolution, prefix)
    return name, asset


def fetch_sky():
    asset, resolution = SKY
    folder = f"{ART}/Sky"
    os.makedirs(folder, exist_ok=True)
    path = f"{folder}/Sky.hdr"
    if not done(path):
        save(path, pick(files(asset)["hdri"], resolution, ["hdr"]))
    return "Sky", asset


def main():
    if not os.path.isdir("Assets"):
        sys.exit("run from the Unity project root")
    os.makedirs(ART, exist_ok=True)
    with ThreadPoolExecutor(4) as pool:
        jobs = [pool.submit(fetch_surface, n, s) for n, s in SURFACES.items()]
        jobs += [pool.submit(fetch_model, n, s) for n, s in MODELS.items()]
        jobs.append(pool.submit(fetch_sky))
        credits = sorted(job.result() for job in jobs)
    with open(f"{ART}/CREDITS.md", "w") as out:
        out.write("# Art credits\n\nAll from [Poly Haven](https://polyhaven.com), released CC0 (public domain).\n\n")
        for name, asset in credits:
            out.write(f"- {name}: [{asset}](https://polyhaven.com/a/{asset})\n")
    print("done")


if __name__ == "__main__":
    main()
