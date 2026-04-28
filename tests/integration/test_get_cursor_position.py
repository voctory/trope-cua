from driver_client import call


def test_get_cursor_position_returns_structured_probe_result():
    result = call("get_cursor_position")

    assert result["isError"] is False
    structured = result["structuredContent"]
    assert structured["ok"] is True
    assert structured["route"] == "user32.getcursorpos"
    assert isinstance(structured["x"], int)
    assert isinstance(structured["y"], int)


def test_visual_move_cursor_does_not_move_real_cursor():
    before = call("get_cursor_position")["structuredContent"]
    target_x = before["x"] + 40
    target_y = before["y"] + 20

    moved = call("move_cursor", {"x": target_x, "y": target_y})
    assert moved["isError"] is False
    assert moved["structuredContent"]["route"] == "agent_cursor.visual_move"
    assert moved["structuredContent"]["cursor_moved"] is False
    assert moved["structuredContent"]["foreground_changed"] is False

    after = call("get_cursor_position")["structuredContent"]
    assert after["x"] == before["x"]
    assert after["y"] == before["y"]


def test_parent_cursor_override_is_reported_as_not_background_safe():
    current = call("get_cursor_position")["structuredContent"]

    moved = call(
        "move_cursor",
        {"x": current["x"], "y": current["y"], "allow_parent_cursor": True},
    )

    assert moved["isError"] is False
    assert moved["structuredContent"]["ok"] is True
    assert moved["structuredContent"]["route"] == "parent.setcursorpos"
    assert moved["structuredContent"]["background_safe"] is False
    assert moved["structuredContent"]["session"] == "parent"


def test_get_agent_cursor_state_returns_structured_content():
    result = call("get_agent_cursor_state")

    assert result["isError"] is False
    structured = result["structuredContent"]
    assert structured["route"] == "winforms.click_through_overlay"
    assert "motion" in structured
    assert "enabled" in structured
    assert "render_fps" in structured
    assert "render_ms" in structured
    assert "render_frame_count" in structured
    assert structured["palette"]["name"] == "default_blue"


def test_set_agent_cursor_enabled_requires_boolean():
    result = call("set_agent_cursor_enabled", {"enabled": None})

    assert result["isError"] is True
    assert "Missing required boolean field enabled" in result["content"][0]["text"]
