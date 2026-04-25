from driver_client import call


def test_set_config_uses_isolated_config_directory(tmp_path):
    env = {"CUA_DRIVER_CONFIG_DIR": str(tmp_path)}

    result = call("set_config", {"key": "capture_mode", "value": "ax"}, extra_env=env)
    assert result["isError"] is False
    assert result["structuredContent"]["capture_mode"] == "ax"

    loaded = call("get_config", extra_env=env)
    assert loaded["structuredContent"]["capture_mode"] == "ax"
    assert (tmp_path / "config.json").exists()


def test_set_config_persists_normalized_cursor_motion(tmp_path):
    env = {"CUA_DRIVER_CONFIG_DIR": str(tmp_path)}

    result = call(
        "set_config",
        {"key": "agent_cursor.motion.glide_duration_ms", "value": 1},
        extra_env=env,
    )

    assert result["isError"] is False
    assert result["structuredContent"]["agent_cursor"]["motion"]["glide_duration_ms"] == 50

    loaded = call("get_config", extra_env=env)
    assert loaded["structuredContent"]["agent_cursor"]["motion"]["glide_duration_ms"] == 50
