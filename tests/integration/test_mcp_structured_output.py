import json
import os
import subprocess


EXE = os.environ.get("CUA_DRIVER_EXE", "cua-driver-win.exe")


def test_mcp_tools_call_includes_structured_content_for_receipts():
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": "launch_app",
            "arguments": {"name": "notepad"},
        },
    }
    proc = subprocess.run(
        [EXE, "mcp"],
        input=json.dumps(request) + "\n",
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 0
    response = json.loads(proc.stdout.splitlines()[0])
    result = response["result"]
    assert result["isError"] is True
    assert result["structuredContent"]["route"] == "requires_background_launch_lane"
    assert result["structuredContent"]["ok"] is False


def test_child_session_status_includes_structured_content():
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": "child_session_status",
            "arguments": {},
        },
    }
    proc = subprocess.run(
        [EXE, "mcp"],
        input=json.dumps(request) + "\n",
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 0
    response = json.loads(proc.stdout.splitlines()[0])
    structured = response["result"]["structuredContent"]
    assert "child_sessions_supported_by_os" in structured
    assert "host" in structured


def test_child_session_stop_includes_structured_content():
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": "child_session_stop",
            "arguments": {},
        },
    }
    proc = subprocess.run(
        [EXE, "mcp"],
        input=json.dumps(request) + "\n",
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 0
    response = json.loads(proc.stdout.splitlines()[0])
    assert response["result"]["structuredContent"]["ok"] is True
    assert "host" in response["result"]["structuredContent"]


def test_mcp_reports_parse_error_for_invalid_json():
    proc = subprocess.run(
        [EXE, "mcp"],
        input="{not-json}\n",
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 0
    response = json.loads(proc.stdout.splitlines()[0])
    assert response["error"]["code"] == -32700


def test_mcp_reports_invalid_params_for_missing_tool_name():
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {},
    }
    proc = subprocess.run(
        [EXE, "mcp"],
        input=json.dumps(request) + "\n",
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 0
    response = json.loads(proc.stdout.splitlines()[0])
    assert response["id"] == 1
    assert response["error"]["code"] == -32602


def test_mcp_reports_invalid_params_for_non_object_arguments():
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": "get_config",
            "arguments": "not-an-object",
        },
    }
    proc = subprocess.run(
        [EXE, "mcp"],
        input=json.dumps(request) + "\n",
        text=True,
        capture_output=True,
        timeout=30,
    )

    assert proc.returncode == 0
    response = json.loads(proc.stdout.splitlines()[0])
    assert response["id"] == 1
    assert response["error"]["code"] == -32602
    assert "arguments" in response["error"]["message"]
