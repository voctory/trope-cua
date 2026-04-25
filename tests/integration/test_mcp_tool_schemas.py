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

    zoom = tools["zoom"]["inputSchema"]
    assert "window_id" in zoom["properties"]


def test_mcp_initialize_includes_background_agent_instructions():
    result = mcp_request("initialize", {})
    instructions = result["instructions"]
    assert "background-safe" in instructions
    assert "list_windows" in instructions
    assert "get_window_state" in instructions
    assert "unsafe_allow_foreground" in instructions
    assert "Do not pass unsafe_allow_foreground" in instructions
    for blocked in ("M" + "ac", "mac" + "OS"):
        assert blocked not in instructions


def test_mcp_tool_descriptions_steer_away_from_foreground_routes():
    result = mcp_request("tools/list")
    tools = {tool["name"]: tool for tool in result["tools"]}

    launch = tools["launch_app"]
    launch_schema = launch["inputSchema"]
    assert "allow_foreground" not in launch_schema["properties"]
    assert "unsafe_allow_foreground" in launch_schema["properties"]
    assert "Do not pass unsafe_allow_foreground" in launch["description"]
    assert "routine background automation" in launch_schema["properties"]["unsafe_allow_foreground"]["description"]

    click_description = tools["click"]["description"]
    assert "element_index" in click_description
    assert "background-safe" in click_description
    assert "blind browser PostMessage clicks are refused" in click_description

    move_cursor = tools["move_cursor"]
    assert "visual agent cursor overlay" in move_cursor["description"]
    assert "should not be used as an input route" in move_cursor["description"]

    cursor_motion_schema = tools["set_agent_cursor_motion"]["inputSchema"]
    assert "press_duration_ms" in cursor_motion_schema["properties"]

    set_config_value_schema = tools["set_config"]["inputSchema"]["properties"]["value"]
    assert {"type": "string"} in set_config_value_schema["anyOf"]
    assert {"type": "number"} in set_config_value_schema["anyOf"]
    assert {"type": "boolean"} in set_config_value_schema["anyOf"]
    assert {"type": "null"} in set_config_value_schema["anyOf"]

    for blocked in ("M" + "ac", "mac" + "OS"):
        for tool in tools.values():
            assert blocked not in tool["description"]
