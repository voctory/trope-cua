from driver_client import call
import json


def test_get_config_returns_structured_content():
    result = call("get_config")
    assert result["isError"] is False
    assert result["structuredContent"]["schema_version"] >= 2
    assert result["structuredContent"]["max_image_dimension"] == 0
    assert "agent_cursor" in result["structuredContent"]


def test_get_config_migrates_old_default_image_dimension_to_native(tmp_path):
    config = {
        "schema_version": 1,
        "capture_mode": "som",
        "max_image_dimension": 1568,
        "chromium_debugging_port": None,
        "allow_parent_sendinput": False,
        "agent_cursor": {"enabled": True, "motion": {}},
    }
    (tmp_path / "config.json").write_text(json.dumps(config))

    result = call("get_config", extra_env={"TROPE_CUA_CONFIG_DIR": str(tmp_path)})

    assert result["isError"] is False
    assert result["structuredContent"]["schema_version"] == 3
    assert result["structuredContent"]["max_image_dimension"] == 0
