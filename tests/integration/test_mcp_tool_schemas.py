import json
import os
import subprocess


EXE = os.environ.get("TROPE_CUA_EXE", "trope-cua.exe")


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


def test_mcp_tool_schemas_are_openai_compatible_plain_objects():
    result = mcp_request("tools/list")
    tools = {tool["name"]: tool for tool in result["tools"]}

    forbidden_top_level_keywords = {"anyOf", "oneOf", "allOf", "enum", "not"}
    for tool in tools.values():
        schema = tool["inputSchema"]
        assert schema["type"] == "object"
        assert forbidden_top_level_keywords.isdisjoint(schema)

    get_window_state = tools["get_window_state"]["inputSchema"]
    assert get_window_state["required"] == ["pid", "window_id"]
    assert "capture_mode" in get_window_state["properties"]
    assert "include_structured_tree" in get_window_state["properties"]

    click = tools["click"]["inputSchema"]
    assert click["required"] == ["pid"]
    assert {"element_index", "x", "y"}.issubset(click["properties"])

    launch = tools["launch_app"]["inputSchema"]
    assert {"path", "exe", "name", "app_id"}.issubset(launch["properties"])

    zoom = tools["zoom"]["inputSchema"]
    assert "window_id" in zoom["properties"]


def test_mcp_initialize_includes_background_agent_instructions():
    result = mcp_request("initialize", {})
    instructions = result["instructions"]
    assert "background-safe" in instructions
    assert "list_windows" in instructions
    assert "get_window_state" in instructions
    assert "Browser text workflow" in instructions
    assert "Do not launch a separate debugging-profile browser" in instructions
    assert "browser_eval with a user-gesture navigation expression" in instructions
    assert "retry with a fresh element_index" in instructions
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
    assert "--remote-debugging-port" in launch["description"]

    click_description = tools["click"]["description"]
    assert "element_index" in click_description
    assert "background-safe" in click_description
    assert "blind browser PostMessage clicks are refused" in click_description
    assert "refresh get_window_state and retry with the new element_index" in click_description

    type_text_description = tools["type_text"]["description"]
    assert "pass the edit/search/address element_index directly to type_text" in type_text_description
    assert "Follow with press_key enter/return" in type_text_description
    assert "It defaults true" in type_text_description
    assert "Defaults true" in tools["type_text"]["inputSchema"]["properties"]["allow_transient_foreground"]["description"]
    assert "Defaults true" in tools["press_key"]["inputSchema"]["properties"]["allow_transient_foreground"]["description"]
    assert "browser_eval for user-gesture navigation" in tools["press_key"]["description"]

    set_value_description = tools["set_value"]["description"]
    assert "prefer type_text with element_index" in set_value_description
    assert "use_type_text_for_browser_text" in set_value_description
    assert "Defaults true" in tools["click"]["inputSchema"]["properties"]["allow_transient_foreground"]["description"]

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
