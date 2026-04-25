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
