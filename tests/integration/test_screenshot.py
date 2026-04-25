from driver_client import call


def test_screenshot_rejects_unknown_format():
    result = call("screenshot", {"format": "bitmap"})

    assert result["isError"] is True
    assert "format must be one of" in result["content"][0]["text"]
