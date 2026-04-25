from driver_client import call


def test_direct_call_infers_structured_action_receipt():
    result = call("launch_app", {"name": "notepad"})
    assert result["isError"] is True
    assert result["structuredContent"]["ok"] is False
    assert result["structuredContent"]["route"] == "requires_background_launch_lane"
