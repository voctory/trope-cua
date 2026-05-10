from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def test_native_pick_prefers_uia_selection_before_msaa_default_action():
    source = (ROOT / "src" / "CuaDriver.Win" / "Tools" / "ClickTool.cs").read_text(encoding="utf-8")
    method_start = source.index("private static async Task<ActionReceipt> InvokeNativeElementActionAsync")
    method_end = source.index("    private static bool PrefersUiaAction", method_start)
    native_action_source = source[method_start:method_end]

    selection_route = native_action_source.index("UiAutomationActions.PrefersSelectionItem(action)")
    selection_action = native_action_source.index("UiAutomationActions.TrySelectItem(element, allowTransientForeground)")
    selection_stop_guard = native_action_source.index("selectionReceipt.ShouldStopFallback")
    msaa_route = native_action_source.index("MsaaActions.DoDefaultActionAtElement")

    assert selection_route < msaa_route
    assert selection_action < msaa_route
    assert selection_stop_guard < msaa_route
