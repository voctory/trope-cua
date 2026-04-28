from driver_client import call

def test_list_windows_smoke():
    result = call("list_windows")
    assert "content" in result
    assert result["content"][0]["type"] == "text"
    assert "windows" in result["structuredContent"]
    assert result["structuredContent"]["count"] == len(result["structuredContent"]["windows"])
    assert result["structuredContent"]["shown_count"] <= result["structuredContent"]["count"]


def test_list_windows_verbose_expands_text():
    compact = call("list_windows")
    verbose = call("list_windows", {"verbose": True})

    assert verbose["structuredContent"]["verbose"] is True
    assert verbose["structuredContent"]["shown_count"] == verbose["structuredContent"]["count"]
    assert len(verbose["content"][0]["text"]) >= len(compact["content"][0]["text"])
