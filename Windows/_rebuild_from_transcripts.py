# -*- coding: utf-8 -*-
import json
import os
import glob
from collections import OrderedDict

ROOT = r"C:\Users\Lenovo\.cursor\projects"
OUT_DIR = r"E:\Cursor_Dashboard\Windows"
KEEP_NAMES = (
    "QuotaUi.cs",
    "QuotaCore.cs",
    "SqliteKv.cs",
    "Program.cs",
    "AssemblyInfo.cs",
    "GenerateIcon.cs",
    "build-win.ps1",
    "build-installer.ps1",
    "Start-CursorQuotaPet.bat",
    "CursorDashboard.iss",
    ".gitignore",
    "README.md",
)


def interesting(path):
    name = os.path.basename(path.replace("\\", "/"))
    return name in KEEP_NAMES


def iter_ops():
    files = glob.glob(os.path.join(ROOT, "**", "*.jsonl"), recursive=True)
    ops = []
    for p in files:
        try:
            mtime = os.path.getmtime(p)
        except OSError:
            continue
        with open(p, "r", encoding="utf-8") as f:
            for i, line in enumerate(f, 1):
                if '"Write"' not in line and '"StrReplace"' not in line:
                    continue
                try:
                    obj = json.loads(line)
                except Exception:
                    continue
                content = obj.get("message", {}).get("content", [])
                for c in content:
                    if c.get("type") != "tool_use" or c.get("name") not in ("Write", "StrReplace"):
                        continue
                    inp = c.get("input") or {}
                    path = str(inp.get("path") or "")
                    if not interesting(path):
                        continue
                    ops.append((mtime, p, i, c.get("name"), path, inp))
    ops.sort(key=lambda x: (x[0], x[1], x[2]))
    return ops


def main():
    files = {}
    log = []
    for mtime, src, line, name, path, inp in iter_ops():
        base = os.path.basename(path.replace("\\", "/"))
        key = base
        if name == "Write":
            files[key] = inp.get("contents") or ""
            log.append("WRITE %s L%s -> %s (%d chars)" % (os.path.basename(os.path.dirname(src)), line, key, len(files[key])))
        else:
            old = inp.get("old_string") or ""
            new = inp.get("new_string") or ""
            current = files.get(key)
            if current is None:
                log.append("SKIP no file %s L%s %s" % (os.path.basename(os.path.dirname(src)), line, key))
                continue
            if old not in current:
                log.append("MISS %s L%s %s old=%d" % (os.path.basename(os.path.dirname(src)), line, key, len(old)))
                continue
            count = current.count(old)
            files[key] = current.replace(old, new, 1)
            log.append("PATCH %s L%s %s occ=%s" % (os.path.basename(os.path.dirname(src)), line, key, count))

    os.makedirs(os.path.join(OUT_DIR, "src"), exist_ok=True)
    for key, content in files.items():
        dest = os.path.join(OUT_DIR, "src", key)
        with open(dest, "w", encoding="utf-8") as f:
            f.write(content)
        print("WROTE", dest, len(content))

    with open(os.path.join(OUT_DIR, "_rebuild_log.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(log))
    print("LOG", len(log), "ops")
    miss = [x for x in log if x.startswith("MISS") or x.startswith("SKIP")]
    print("FAILURES", len(miss))
    for x in miss:
        print(x)


if __name__ == "__main__":
    main()
