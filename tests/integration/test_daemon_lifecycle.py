import json
import os
import subprocess


EXE = os.environ.get("CUA_DRIVER_EXE", "cua-driver-win.exe")


def test_daemon_list_returns_structured_instances():
    env = os.environ.copy()
    env["CUA_DRIVER_JSON"] = "1"
    proc = subprocess.run(
        [EXE, "daemon-list"],
        text=True,
        capture_output=True,
        env=env,
        timeout=30,
    )

    assert proc.returncode == 0
    result = json.loads(proc.stdout)
    assert result["isError"] is False
    assert "instances" in result["structuredContent"]


def test_daemon_stop_all_succeeds_without_instances():
    env = os.environ.copy()
    env["CUA_DRIVER_JSON"] = "1"
    proc = subprocess.run(
        [EXE, "daemon-stop", "--all"],
        text=True,
        capture_output=True,
        env=env,
        timeout=30,
    )

    assert proc.returncode == 0
    result = json.loads(proc.stdout)
    assert result["isError"] is False
    assert "instances" in result["structuredContent"]
