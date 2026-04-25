from driver_client import call


def test_get_accessibility_tree_forces_ax_mode_without_persisting_config(tmp_path):
    env = {"CUA_DRIVER_CONFIG_DIR": str(tmp_path)}
    configured = call("set_config", {"key": "capture_mode", "value": "vision"}, extra_env=env)
    assert configured["structuredContent"]["capture_mode"] == "vision"

    windows = call("list_windows", extra_env=env)["structuredContent"]["windows"]
    assert windows
    target = windows[0]

    result = call(
        "get_accessibility_tree",
        {"pid": target["pid"], "window_id": target["window_id"]},
        extra_env=env,
    )

    assert result["isError"] is False
    assert result["structuredContent"]["capture_mode"] == "ax"
    assert "capture" not in result["structuredContent"]

    loaded = call("get_config", extra_env=env)
    assert loaded["structuredContent"]["capture_mode"] == "vision"


def test_get_accessibility_tree_query_filters_markdown_only(tmp_path):
    env = {"CUA_DRIVER_CONFIG_DIR": str(tmp_path)}
    windows = call("list_windows", extra_env=env)["structuredContent"]["windows"]
    assert windows
    target = windows[0]

    result = call(
        "get_accessibility_tree",
        {
            "pid": target["pid"],
            "window_id": target["window_id"],
            "query": "pytest-unlikely-accessibility-query",
        },
        extra_env=env,
    )

    assert result["isError"] is False
    structured = result["structuredContent"]
    assert structured["query"] == "pytest-unlikely-accessibility-query"
    assert structured["uia"]["element_count"] >= 0
    assert structured["uia"]["tree_markdown"] == ""
