import json
import os
import subprocess
from pathlib import Path

EXE = os.environ.get("TROPE_CUA_EXE", "trope-cua.exe")

def call(tool, args=None, extra_env=None):
    raw = json.dumps(args or {})
    env = os.environ.copy()
    env["TROPE_CUA_JSON"] = "1"
    if extra_env:
        env.update(extra_env)
    p = subprocess.run([EXE, tool, raw], text=True, capture_output=True, env=env, timeout=30)
    if p.returncode not in (0, 1):
        raise RuntimeError(f"{tool} failed rc={p.returncode}\nstdout={p.stdout}\nstderr={p.stderr}")
    return json.loads(p.stdout)
