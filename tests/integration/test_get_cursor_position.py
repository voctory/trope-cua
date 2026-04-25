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
