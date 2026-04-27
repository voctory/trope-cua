from driver_client import call


def test_direct_call_infers_structured_action_receipt():
    result = call("launch_app", {"name": "notepad"})
    assert result["isError"] is True
    assert result["structuredContent"]["ok"] is False
    assert result["structuredContent"]["route"] == "requires_background_launch_lane"
    assert "Do not start a separate CDP/debugging browser" in result["structuredContent"]["reason"]


def test_legacy_allow_foreground_flag_is_refused():
    result = call("launch_app", {"name": "notepad", "allow_foreground": True})
    assert result["isError"] is True
    assert result["structuredContent"]["ok"] is False
    assert result["structuredContent"]["route"] == "foreground_launch_not_background_safe"
    assert "unsafe_allow_foreground" in result["structuredContent"]["reason"]
    assert "human explicitly requests" in result["structuredContent"]["reason"]


def test_launch_rejects_mixed_target_aliases():
    result = call("launch_app", {"name": "notepad", "app_id": "example.app"})

    assert result["isError"] is True
    assert "either app_id or path/exe/name" in result["content"][0]["text"]


def test_explicit_cdp_port_is_range_checked():
    result = call("browser_eval", {"expression": "1 + 1", "cdp_port": 70000})

    assert result["isError"] is True
    assert "TCP port must be between" in result["content"][0]["text"]


def test_right_click_rejects_mixed_addressing_modes():
    result = call("right_click", {"pid": 1, "element_index": 1, "x": 10, "y": 10})

    assert result["isError"] is True
    assert "either element_index or x/y" in result["content"][0]["text"]


def test_click_debug_image_rejects_element_index():
    result = call(
        "click",
        {"pid": 1, "window_id": 999999999, "element_index": 1, "debug_image_out": "debug.png"},
    )

    assert result["isError"] is True
    assert "debug_image_out only applies to pixel clicks" in result["content"][0]["text"]


def test_click_debug_image_requires_window_id():
    result = call("click", {"pid": 1, "x": 10, "y": 10, "debug_image_out": "debug.png"})

    assert result["isError"] is True
    assert "debug_image_out requires window_id" in result["content"][0]["text"]


def test_click_debug_image_rejects_zoom_coordinates():
    result = call(
        "click",
        {"pid": 1, "window_id": 999999999, "x": 10, "y": 10, "from_zoom": True, "debug_image_out": "debug.png"},
    )

    assert result["isError"] is True
    assert "debug_image_out is incompatible with from_zoom" in result["content"][0]["text"]


def test_scroll_rejects_partial_wheel_coordinates():
    result = call("scroll", {"pid": 1, "x": 10})

    assert result["isError"] is True
    assert "both x and y" in result["content"][0]["text"]


def test_hotkey_keys_requires_modifier_and_key():
    result = call("hotkey", {"pid": 1, "keys": ["ctrl"]})

    assert result["isError"] is True
    assert "at least one modifier and one non-modifier key" in result["content"][0]["text"]


def test_type_text_element_index_requires_window_id():
    result = call("type_text", {"pid": 1, "element_index": 1, "text": "hello"})

    assert result["isError"] is True
    assert "window_id is required for element_index type_text" in result["content"][0]["text"]


def test_browser_eval_requires_configured_cdp_port():
    result = call("browser_eval", {"expression": "1 + 1"})

    assert result["isError"] is True
    assert "requires cdp_port" in result["content"][0]["text"]


def test_window_scoped_tools_report_missing_window():
    result = call(
        "set_value",
        {"pid": 1, "window_id": 999999999, "element_index": 1, "value": "x"},
    )

    assert result["isError"] is True
    assert "No window with window_id 999999999" in result["content"][0]["text"]
