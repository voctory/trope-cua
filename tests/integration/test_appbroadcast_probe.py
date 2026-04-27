from driver_client import call


def test_appbroadcast_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe")

    structured = result["structuredContent"]
    assert "ok" in structured
    assert structured["appbroadcast_only"] is True
    assert "route" in structured


def test_appbroadcast_keyboard_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe", {"probe": "keyboard_key", "key": "f24"})

    structured = result["structuredContent"]
    assert "ok" in structured
    assert structured["appbroadcast_only"] is True
    assert structured["key"] == "f24"
    assert "background_safe" in structured


def test_appbroadcast_keyboard_text_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe", {"probe": "keyboard_text", "text": "test", "press_enter": False})

    structured = result["structuredContent"]
    assert "ok" in structured
    assert structured["appbroadcast_only"] is True
    assert structured["text_length"] == 4
    assert "background_safe" in structured


def test_appbroadcast_mouse_click_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe", {"probe": "mouse_click", "x": 0, "y": 0})

    structured = result["structuredContent"]
    assert "ok" in structured
    assert structured["appbroadcast_only"] is True
    assert structured["x"] == 0
    assert structured["y"] == 0
    assert "background_safe" in structured


def test_appbroadcast_status_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe", {"probe": "broadcast_status"})

    structured = result["structuredContent"]
    assert "ok" in structured
    assert structured["route"] == "appbroadcasting.ui.getstatus"


def test_appbroadcast_plugins_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe", {"probe": "broadcast_plugins"})

    structured = result["structuredContent"]
    assert "ok" in structured
    assert structured["route"] == "appbroadcast.plugin_manager"


def test_appbroadcast_api_context_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe", {"probe": "api_context"})

    structured = result["structuredContent"]
    assert structured["ok"] is True
    assert structured["route"] == "appbroadcast.api_context"
    assert "types" in structured


def test_appbroadcast_gamebar_services_probe_returns_structured_diagnostics():
    result = call("appbroadcast_input_probe", {"probe": "gamebar_services", "timeout_ms": 100})

    structured = result["structuredContent"]
    assert "ok" in structured
    assert structured["route"] == "gamebarservicesmanager.servicescreated"
