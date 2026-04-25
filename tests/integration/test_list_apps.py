from driver_client import call


def test_list_apps_returns_structured_apps():
    result = call("list_apps")
    assert result["isError"] is False
    assert "apps" in result["structuredContent"]
    assert result["structuredContent"]["running_count"] == len(result["structuredContent"]["apps"])
