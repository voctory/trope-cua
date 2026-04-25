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


def test_set_config_bounds_max_image_dimension(tmp_path):
    env = {"CUA_DRIVER_CONFIG_DIR": str(tmp_path)}

    result = call("set_config", {"key": "max_image_dimension", "value": 999999}, extra_env=env)

    assert result["isError"] is False
    assert result["structuredContent"]["max_image_dimension"] == 8192


def test_set_config_rejects_invalid_chromium_port(tmp_path):
    env = {"CUA_DRIVER_CONFIG_DIR": str(tmp_path)}

    result = call("set_config", {"key": "chromium_debugging_port", "value": 70000}, extra_env=env)

    assert result["isError"] is True
    assert "TCP port must be between" in result["content"][0]["text"]
    assert not (tmp_path / "config.json").exists()


def test_set_config_clears_nullable_chromium_port(tmp_path):
    env = {"CUA_DRIVER_CONFIG_DIR": str(tmp_path)}

    result = call("set_config", {"key": "chromium_debugging_port", "value": 9222}, extra_env=env)
    assert result["isError"] is False
    assert result["structuredContent"]["chromium_debugging_port"] == 9222

    cleared = call("set_config", {"key": "chromium_debugging_port", "value": None}, extra_env=env)
    assert cleared["isError"] is False
    assert cleared["structuredContent"]["chromium_debugging_port"] is None

    loaded = call("get_config", extra_env=env)
    assert loaded["structuredContent"]["chromium_debugging_port"] is None
