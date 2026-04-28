from driver_client import call


def test_list_apps_returns_structured_apps():
    result = call("list_apps")
    assert result["isError"] is False
    assert "apps" in result["structuredContent"]
    apps = result["structuredContent"]["apps"]
    assert result["structuredContent"]["shown_count"] == len(apps)
    assert result["structuredContent"]["omitted_count"] >= 0
    assert result["structuredContent"]["running_count"] >= len([app for app in apps if app["running"]])
    assert result["structuredContent"]["installed_count"] >= len([app for app in apps if not app["running"]])


def test_list_apps_verbose_returns_all_matched_apps():
    verbose = call("list_apps", {"verbose": True})
    assert verbose["isError"] is False
    assert verbose["structuredContent"]["verbose"] is True
    assert verbose["structuredContent"]["shown_count"] == (
        verbose["structuredContent"]["running_count"] + verbose["structuredContent"]["installed_count"]
    )
    assert verbose["structuredContent"]["shown_count"] == len(verbose["structuredContent"]["apps"])
