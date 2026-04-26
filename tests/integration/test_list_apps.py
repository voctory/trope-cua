from driver_client import call


def test_list_apps_returns_structured_apps():
    result = call("list_apps")
    assert result["isError"] is False
    assert "apps" in result["structuredContent"]
    apps = result["structuredContent"]["apps"]
    assert result["structuredContent"]["running_count"] == len([app for app in apps if app["running"]])
    assert result["structuredContent"]["installed_count"] == len([app for app in apps if not app["running"]])
