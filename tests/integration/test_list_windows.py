from driver_client import call

def test_list_windows_smoke():
    result = call("list_windows")
    assert "content" in result
    assert result["content"][0]["type"] == "text"
    assert "windows" in result["structuredContent"]
    assert result["structuredContent"]["count"] == len(result["structuredContent"]["windows"])
