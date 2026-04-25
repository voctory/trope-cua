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
