from driver_client import call


def test_set_config_uses_isolated_config_directory(tmp_path):
    env = {"CUA_DRIVER_CONFIG_DIR": str(tmp_path)}

    result = call("set_config", {"key": "capture_mode", "value": "ax"}, extra_env=env)
    assert result["isError"] is False
    assert result["structuredContent"]["capture_mode"] == "ax"

    loaded = call("get_config", extra_env=env)
    assert loaded["structuredContent"]["capture_mode"] == "ax"
    assert (tmp_path / "config.json").exists()
