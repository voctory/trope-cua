from driver_client import call


def test_direct_call_infers_structured_action_receipt():
    result = call("launch_app", {"name": "notepad"})
    assert result["isError"] is True
    assert result["structuredContent"]["ok"] is False
    assert result["structuredContent"]["route"] == "requires_background_launch_lane"


def test_legacy_allow_foreground_flag_is_refused():
    result = call("launch_app", {"name": "notepad", "allow_foreground": True})
    assert result["isError"] is True
    assert result["structuredContent"]["ok"] is False
    assert result["structuredContent"]["route"] == "foreground_launch_not_background_safe"
    assert "unsafe_allow_foreground" in result["structuredContent"]["reason"]


def test_explicit_cdp_port_is_range_checked():
    result = call("browser_eval", {"expression": "1 + 1", "cdp_port": 70000})

    assert result["isError"] is True
    assert "TCP port must be between" in result["content"][0]["text"]


def test_right_click_rejects_mixed_addressing_modes():
    result = call("right_click", {"pid": 1, "element_index": 1, "x": 10, "y": 10})

    assert result["isError"] is True
    assert "either element_index or x/y" in result["content"][0]["text"]


def test_scroll_rejects_partial_wheel_coordinates():
    result = call("scroll", {"pid": 1, "x": 10})

    assert result["isError"] is True
    assert "both x and y" in result["content"][0]["text"]
