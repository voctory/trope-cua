from driver_client import call

def test_check_permissions_smoke():
    result = call("check_permissions")
    text = result["content"][0]["text"]
    assert "uia_available" in text
    assert "allow_parent_sendinput" in text
    assert result["structuredContent"]["uia_available"] is True
    assert "wgc_supported_by_os" in result["structuredContent"]
