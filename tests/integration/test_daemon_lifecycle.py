import json
import os
import subprocess
import time
import uuid


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


def test_daemon_stop_missing_instance_reports_error():
    instance = f"pytest-missing-{uuid.uuid4().hex}"
    env = os.environ.copy()
    env["CUA_DRIVER_JSON"] = "1"
    proc = subprocess.run(
        [EXE, "daemon-stop", "--instance", instance],
        text=True,
        capture_output=True,
        env=env,
        timeout=30,
    )

    assert proc.returncode == 1
    result = json.loads(proc.stdout)
    assert result["isError"] is True
    assert "daemon not running" in result["content"][0]["text"]


def test_daemon_instances_are_isolated():
    first = f"pytest-{uuid.uuid4().hex}-a"
    second = f"pytest-{uuid.uuid4().hex}-b"
    processes = []

    try:
        processes.append(start_daemon(first))
        processes.append(start_daemon(second))

        first_status = wait_for_status(first)
        second_status = wait_for_status(second)

        assert first_status["instance_id"] == first
        assert second_status["instance_id"] == second
        assert first_status["pipe_name"] != second_status["pipe_name"]
        assert first_status["pid"] != second_status["pid"]
    finally:
        stop_daemon(first)
        stop_daemon(second)
        for process in processes:
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)


def start_daemon(instance):
    return subprocess.Popen(
        [EXE, "serve", "--instance", instance],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        text=True,
    )


def wait_for_status(instance):
    env = os.environ.copy()
    env["CUA_DRIVER_JSON"] = "1"
    last_stdout = ""
    last_stderr = ""
    for _ in range(50):
        proc = subprocess.run(
            [EXE, "daemon-status", "--instance", instance],
            text=True,
            capture_output=True,
            env=env,
            timeout=5,
        )
        last_stdout = proc.stdout
        last_stderr = proc.stderr
        if proc.returncode == 0:
            result = json.loads(proc.stdout)
            if result["structuredContent"]["running"] is True:
                return result["structuredContent"]
        time.sleep(0.1)

    raise AssertionError(f"daemon {instance} did not start\nstdout={last_stdout}\nstderr={last_stderr}")


def stop_daemon(instance):
    env = os.environ.copy()
    env["CUA_DRIVER_JSON"] = "1"
    subprocess.run(
        [EXE, "daemon-stop", "--instance", instance],
        text=True,
        capture_output=True,
        env=env,
        timeout=10,
    )


def test_daemon_stop_all_prunes_stale_records(tmp_path):
    registry = tmp_path / "daemons"
    registry.mkdir()
    stale_id = f"pytest-stale-{uuid.uuid4().hex}"
    stale_record = {
        "instanceId": stale_id,
        "pid": 999999,
        "pipeName": f"cua-driver-win-test-{stale_id}",
        "startedAt": "2026-01-01T00:00:00.0000000Z",
        "exePath": str(tmp_path / "missing.exe"),
    }
    stale_path = registry / f"{stale_id}.json"
    stale_path.write_text(json.dumps(stale_record), encoding="utf-8")

    env = os.environ.copy()
    env["CUA_DRIVER_JSON"] = "1"
    env["CUA_DRIVER_CONFIG_DIR"] = str(tmp_path)
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
    assert result["structuredContent"]["stopped"] == 1
    assert result["structuredContent"]["failed"] == 0
    assert result["structuredContent"]["instances"][0]["stale"] is True
    assert not stale_path.exists()
