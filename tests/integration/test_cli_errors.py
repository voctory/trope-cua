import os
import subprocess


EXE = os.environ.get("CUA_DRIVER_EXE", "cua-driver-win.exe")


def test_cli_reports_invalid_json_arguments_without_stack_trace():
    proc = subprocess.run(
        [EXE, "get_config", "{not-json}"],
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 64
    assert "Invalid JSON arguments" in proc.stderr
    assert "Unhandled exception" not in proc.stderr
