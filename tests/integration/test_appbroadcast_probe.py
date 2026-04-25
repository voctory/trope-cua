from driver_client import call


def test_appbroadcast_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe")

    structured = result["structuredContent"]
    assert "ok" in structured
    assert structured["appbroadcast_only"] is True
    assert "route" in structured
