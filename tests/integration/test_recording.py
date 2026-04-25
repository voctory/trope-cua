from driver_client import call


def test_recording_state_returns_structured_content():
    result = call("get_recording_state")
    assert result["isError"] is False
    assert result["structuredContent"]["enabled"] is False
    assert result["structuredContent"]["next_turn"] == 1


def test_set_recording_enable_returns_structured_content(tmp_path):
    result = call("set_recording", {"enabled": True, "output_dir": str(tmp_path)})
    assert result["isError"] is False
    assert result["structuredContent"]["enabled"] is True
    assert result["structuredContent"]["output_dir"]
