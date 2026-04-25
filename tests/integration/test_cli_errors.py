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


def test_cli_reports_non_object_tool_arguments_without_stack_trace():
    proc = subprocess.run(
        [EXE, "get_config", "[]"],
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 64
    assert "Tool arguments must be a JSON object" in proc.stderr
    assert "Unhandled exception" not in proc.stderr


def test_cli_reports_missing_instance_value_without_stack_trace():
    proc = subprocess.run(
        [EXE, "--instance"],
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 64
    assert "--instance requires a value" in proc.stderr
    assert "Unhandled exception" not in proc.stderr
