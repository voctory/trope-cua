import json
import os
import subprocess


EXE = os.environ.get("CUA_DRIVER_EXE", "cua-driver-win.exe")


def mcp_request(method, params=None):
    request = {"jsonrpc": "2.0", "id": 1, "method": method}
    if params is not None:
        request["params"] = params
    proc = subprocess.run(
        [EXE, "mcp"],
        input=json.dumps(request) + "\n",
        text=True,
        capture_output=True,
        timeout=30,
    )
    assert proc.returncode == 0
    return json.loads(proc.stdout.splitlines()[0])["result"]


def test_mcp_tool_schemas_advertise_required_and_alternatives():
    result = mcp_request("tools/list")
    tools = {tool["name"]: tool for tool in result["tools"]}

    get_window_state = tools["get_window_state"]["inputSchema"]
    assert get_window_state["required"] == ["pid", "window_id"]

    click = tools["click"]["inputSchema"]
    assert click["required"] == ["pid"]
    assert {"required": ["element_index"]} in click["anyOf"]
    assert {"required": ["x", "y"]} in click["anyOf"]

    launch = tools["launch_app"]["inputSchema"]
    assert {"required": ["path"]} in launch["anyOf"]
    assert {"required": ["app_id"]} in launch["anyOf"]
