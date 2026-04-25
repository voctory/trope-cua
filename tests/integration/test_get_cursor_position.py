from driver_client import call


def test_get_cursor_position_returns_structured_probe_result():
    result = call("get_cursor_position")

    assert result["isError"] is False
    structured = result["structuredContent"]
    assert structured["ok"] is True
    assert structured["route"] == "user32.getcursorpos"
    assert isinstance(structured["x"], int)
    assert isinstance(structured["y"], int)
