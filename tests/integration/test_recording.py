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


def test_set_recording_requires_boolean_enabled():
    result = call("set_recording", {"enabled": None})

    assert result["isError"] is True
    assert "Missing required boolean field enabled" in result["content"][0]["text"]


def test_replay_reports_non_object_action_arguments(tmp_path):
    turn_dir = tmp_path / "turn-00001"
    turn_dir.mkdir()
    (turn_dir / "action.json").write_text(
        '{"tool":"get_config","arguments":"not-an-object"}',
        encoding="utf-8",
    )

    result = call("replay_trajectory", {"dir": str(tmp_path), "delay_ms": 0})

    assert result["isError"] is True
    assert result["structuredContent"]["attempted"] == 0
    assert result["structuredContent"]["failed"] == 1
    assert result["structuredContent"]["turns"][0]["replay_error"] is True
    assert "arguments must be a JSON object" in result["structuredContent"]["first_failure"]["error"]
