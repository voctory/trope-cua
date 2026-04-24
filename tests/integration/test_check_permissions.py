from driver_client import call

def test_check_permissions_smoke():
    result = call("check_permissions")
    text = result["content"][0]["text"]
    assert "uia_available" in text
    assert "allow_parent_sendinput" in text
