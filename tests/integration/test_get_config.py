from driver_client import call


def test_get_config_returns_structured_content():
    result = call("get_config")
    assert result["isError"] is False
    assert result["structuredContent"]["schema_version"] >= 1
    assert "agent_cursor" in result["structuredContent"]
