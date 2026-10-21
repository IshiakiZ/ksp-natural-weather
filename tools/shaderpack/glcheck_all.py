#!/usr/bin/env python3
"""Every hand-packed shader as make_bundle.py packs it (each texture's uniform given a location, where the driver has the
extension for that), compiled by this Mac's own OpenGL with glcheck.swift; and that what is in each bundle in the workspace is
that very text (a bundle packed before its GLSL or the packer was last changed is stale).

    python3 tools/shaderpack/glcheck_all.py

A Mac only (glcheck.swift uses its OpenGL). Run it after any change to a .glsl here or to make_bundle.py, and after a merge
that brings bundles packed elsewhere."""
import os, subprocess, sys
PACK = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(PACK))
sys.path.insert(0, PACK)
import make_bundle
import tempfile
OUT = tempfile.mkdtemp(prefix="glcheck")
exe = os.path.join(OUT, "glcheck")
subprocess.run(["swiftc", "-O", "-o", exe, os.path.join(PACK, "glcheck.swift")], check=True, stderr=subprocess.DEVNULL)
BUNDLES = {"volume": "src/VolumetricExplosions/Shaders/volume.bundle", "volumerelease": "src/VolumetricExplosions/Shaders/volumerelease.bundle", "layers": "src/VolumetricExplosions/Shaders/layers.bundle", "enlarge": "src/VolumetricExplosions/Shaders/enlarge.bundle",
           "shadow": "src/VolumetricExplosions/Shaders/shadow.bundle", "mark": "src/VolumetricExplosions/Shaders/mark.bundle", "shock": "src/VolumetricExplosions/Shaders/shock.bundle", "sundepth": "src/VolumetricExplosions/Shaders/sundepth.bundle",
           "lens": "src/Keystone/Shaders/lens.bundle", "dnfill": "src/Keystone/Shaders/dnfill.bundle", "farmotion": "src/Keystone/Shaders/farmotion.bundle", "shafts": "src/NaturalLight/Shaders/shafts.bundle", "clean": "src/NaturalLight/Shaders/clean.bundle", "veil": "src/NaturalLight/Shaders/veil.bundle",
           "clouds": "src/NaturalWeather/Shaders/clouds.bundle", "cloudsstorm": "src/NaturalWeather/Shaders/cloudsstorm.bundle", "cloudscalm": "src/NaturalWeather/Shaders/cloudscalm.bundle", "cloudsover": "src/NaturalWeather/Shaders/cloudsover.bundle", "cloudshade": "src/NaturalWeather/Shaders/cloudshade.bundle",
           "ocean": "src/NaturalOcean/Shaders/ocean.bundle", "oceanfoam": "src/NaturalOcean/Shaders/oceanfoam.bundle", "oceanweed": "src/NaturalOcean/Shaders/oceanweed.bundle",
           "oceanunder": "src/NaturalOcean/Shaders/oceanunder.bundle", "wet": "src/NaturalWeather/Shaders/wet.bundle", "motion": "src/VolumetricExplosions/Shaders/motion.bundle", "carry": "src/VolumetricExplosions/Shaders/carry.bundle",
           "drops": "src/NaturalWeather/Shaders/drops.bundle", "snow": "src/NaturalWeather/Shaders/snow.bundle",
           "grade": "src/CineCamera/Shaders/grade.bundle",
           "fargroundpoints": "src/FarGround/Shaders/fargroundpoints.bundle", "fargrounddraw": "src/FarGround/Shaders/fargrounddraw.bundle", "fargrounddepth": "src/FarGround/Shaders/fargrounddepth.bundle",
           "fargroundlights": "src/FarGround/Shaders/fargroundlights.bundle"}
bad = 0
for which, spec in make_bundle.SHADERS.items():
    if not os.path.exists(os.path.join(PACK, spec["code"])):
        continue                   # (one mod's own repository holds its own shaders alone)
    code = make_bundle.source_of(spec)
    packed = make_bundle.fixed_locations(code, [t[0] for t in spec["textures"]])
    path = os.path.join(OUT, which + ".packed.glsl")
    open(path, "wb").write(packed)
    said = subprocess.run([exe, path], capture_output=True, text=True)
    text = (said.stdout + said.stderr).strip()
    inside = "?"
    if which in BUNDLES and os.path.exists(os.path.join(ROOT, BUNDLES[which])):
        blob = open(os.path.join(ROOT, BUNDLES[which]), "rb").read()
        inside = "the bundle holds this text" if packed in blob else "THE BUNDLE HOLDS OTHER TEXT"
        if packed not in blob: bad += 1
    fine = said.returncode == 0 and "error" not in text.lower()
    if not fine: bad += 1
    # (and every value and texture the code declares must be in the packer's own list for it: one that is not is never set by the
    # game, and reads as nothing, with no word said. So it was with the clouds' _CloudHole values for a night, 2026-10-11.)
    import re
    declared = set(re.findall(r"^uniform \w+ (\w+);", code.decode("utf-8", "replace"), re.M))
    listed = set(t[0] for t in spec["textures"]) | set(u[0] for u in spec.get("fragment", [])) | set(u[0] for u in spec.get("vertex", []))
    unlisted = sorted(declared - listed)
    if unlisted:
        bad += 1
        text = (text + "\n" if text else "") + "NOT IN THE PACKER'S LIST (never set by the game): " + ", ".join(unlisted)
    print("%-11s %s; %s; %d samplers placed%s" % (which, "compiles" if fine else "DOES NOT COMPILE", inside, packed.count(b"layout(location"), ("\n    " + text.replace("\n", "\n    ")) if text else ""))
# (A stretch of code that two shaders must have word for word alike is marked in each by a line beginning "// <name>" and one
# "// </name>": the sea's surf, which the sea's mesh and the foam's pass must both work out the same.)
SAME = {"surf": ["ocean.glsl", "oceanfoam.glsl"]}
for name, files in SAME.items():
    texts = []
    for f in files:
        if not os.path.exists(os.path.join(PACK, f)):
            continue
        t = open(os.path.join(PACK, f), encoding="utf-8").read()
        if "// <%s>" % name not in t or "// </%s>" % name not in t:
            texts.append(None)
        else:
            texts.append(t[t.index("// <%s>" % name):t.index("// </%s>" % name)])
    if len(texts) > 1 and (None in texts or len(set(texts)) != 1):
        bad += 1
        print("THE STRETCH <%s> IS NOT THE SAME IN %s" % (name, " AND ".join(files)))
print("all well" if not bad else "%d wrong" % bad)
sys.exit(1 if bad else 0)
