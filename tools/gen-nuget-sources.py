#!/usr/bin/env python3
"""Emit a Flatpak nuget-sources.json from an isolated NuGet global-packages folder.

Each restored package lives at <root>/<id>/<version>/<id>.<version>.nupkg (NuGet lowercases
id + normalizes version in this layout), which maps directly to the nuget.org flatcontainer URL.
We hash each .nupkg (sha512) so flatpak-builder can fetch + verify them offline at build time.
"""
import hashlib
import json
import os
import sys

root = sys.argv[1]
out = sys.argv[2]

entries = []
skipped = []
for id_dir in sorted(os.listdir(root)):
    id_path = os.path.join(root, id_dir)
    if not os.path.isdir(id_path):
        continue
    for ver in sorted(os.listdir(id_path)):
        ver_path = os.path.join(id_path, ver)
        if not os.path.isdir(ver_path):
            continue
        nupkg = os.path.join(ver_path, f"{id_dir}.{ver}.nupkg")
        if not os.path.isfile(nupkg):
            skipped.append(f"{id_dir}/{ver}")
            continue
        h = hashlib.sha512()
        with open(nupkg, "rb") as f:
            for chunk in iter(lambda: f.read(1 << 20), b""):
                h.update(chunk)
        fn = f"{id_dir}.{ver}.nupkg"
        entries.append({
            "type": "file",
            "url": f"https://api.nuget.org/v3-flatcontainer/{id_dir}/{ver}/{fn}",
            "sha512": h.hexdigest(),
            "dest": "nuget-sources",
            "dest-filename": fn,
        })

with open(out, "w") as f:
    json.dump(entries, f, indent=4)
    f.write("\n")

print(f"wrote {len(entries)} packages to {out}")
if skipped:
    print(f"WARNING: {len(skipped)} package dirs had no .nupkg (not from nuget.org?):")
    for s in skipped:
        print(f"  - {s}")
