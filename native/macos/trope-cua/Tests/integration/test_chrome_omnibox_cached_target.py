"""Integration test: Chrome omnibox typing reuses the last AX text target.

Regression coverage for this workflow:
  1. Snapshot Chrome and click the address/search bar by element_index.
  2. Type text with type_text_chars without passing element_index.
  3. Submit with press_key without passing element_index.

The driver should remember the clicked AXTextField and refocus it before
posting character/key events. Without that cached target, Chrome accepts the
AXPress but the subsequent pid-routed keyboard events do not land in the
omnibox on an initially opened window.

Run:
    scripts/test.sh test_chrome_omnibox_cached_target
"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from driver_client import DriverClient, default_binary_path, resolve_window_id

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
_FIXTURE_URL = (
    "file://"
    + os.path.abspath(os.path.join(_THIS_DIR, "fixtures", "interactive.html"))
)
_EXPECTED_TITLE = "Trope CUA Test Page"


def _tool_text(result: dict) -> str:
    for item in result.get("content", []):
        if item.get("type") == "text":
            return item.get("text", "")
    return ""


def _find_element_index(tree: str, label: str) -> int | None:
    for line in tree.split("\n"):
        if label in line:
            match = re.search(r"\[(\d+)\]", line)
            if match:
                return int(match.group(1))
    return None


def _find_omnibox_index(tree: str) -> int | None:
    index = _find_element_index(tree, "Address and search bar")
    if index is not None:
        return index
    for line in tree.split("\n"):
        if "AXTextField" not in line:
            continue
        match = re.search(r"\[(\d+)\]", line)
        if match:
            return int(match.group(1))
    return None


def _window_title(client: DriverClient, pid: int, window_id: int) -> str:
    result = client.call_tool("list_windows", {"pid": pid})
    for window in result["structuredContent"]["windows"]:
        if window.get("window_id") == window_id:
            return window.get("title", "")
    return ""


def _chrome_pids_for_profile(profile_dir: str) -> list[int]:
    result = subprocess.run(
        ["ps", "ax", "-o", "pid=", "-o", "command="],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    )
    pids: list[int] = []
    for line in result.stdout.splitlines():
        if profile_dir not in line or "Google Chrome" not in line:
            continue
        if "Google Chrome Helper" in line:
            continue
        pid_text = line.strip().split(None, 1)[0]
        try:
            pids.append(int(pid_text))
        except ValueError:
            pass
    return pids


class ChromeOmniboxCachedTargetTests(unittest.TestCase):
    """Initial Chrome omnibox text entry works after an AX text-field click."""

    _chrome_pid: int
    _window_id: int
    _profile_dir: str

    @classmethod
    def setUpClass(cls) -> None:
        cls.binary = default_binary_path()
        cls._profile_dir = tempfile.mkdtemp(prefix="trope-cua-chrome-")
        chrome_exe = (
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
        )
        if not os.path.exists(chrome_exe):
            raise unittest.SkipTest("Google Chrome is not installed")

        subprocess.run(
            [
                "open",
                "-na",
                "Google Chrome",
                "--args",
                f"--user-data-dir={cls._profile_dir}",
                "--no-first-run",
                "--no-default-browser-check",
                "--force-renderer-accessibility",
                "chrome://newtab/",
            ],
            check=True,
        )

        deadline = time.monotonic() + 8.0
        cls._chrome_pid = 0
        while time.monotonic() < deadline:
            pids = _chrome_pids_for_profile(cls._profile_dir)
            if pids:
                cls._chrome_pid = pids[0]
                break
            time.sleep(0.25)

        if cls._chrome_pid == 0:
            raise RuntimeError("Chrome test process did not appear")

        deadline = time.monotonic() + 8.0
        cls._window_id = 0
        while time.monotonic() < deadline:
            with DriverClient(cls.binary) as c:
                try:
                    cls._window_id = resolve_window_id(c, cls._chrome_pid)
                    break
                except RuntimeError:
                    pass
            time.sleep(0.25)

        if cls._window_id == 0:
            raise RuntimeError("Chrome test window did not appear")

    @classmethod
    def tearDownClass(cls) -> None:
        for pid in _chrome_pids_for_profile(getattr(cls, "_profile_dir", "")):
            try:
                os.kill(pid, 15)
            except ProcessLookupError:
                pass
        time.sleep(0.5)
        shutil.rmtree(getattr(cls, "_profile_dir", ""), ignore_errors=True)

    def test_click_type_and_return_reuse_cached_omnibox(self) -> None:
        with DriverClient(self.binary) as c:
            deadline = time.monotonic() + 8.0
            tree = ""
            omnibox = None
            while time.monotonic() < deadline:
                snapshot = c.call_tool(
                    "get_window_state",
                    {
                        "pid": self._chrome_pid,
                        "window_id": self._window_id,
                    },
                )
                tree = snapshot.get("structuredContent", snapshot).get(
                    "tree_markdown", ""
                )
                omnibox = _find_omnibox_index(tree)
                if omnibox is not None:
                    break
                time.sleep(0.25)
            self.assertIsNotNone(omnibox, tree)

            c.call_tool(
                "click",
                {
                    "pid": self._chrome_pid,
                    "window_id": self._window_id,
                    "element_index": omnibox,
                },
            )
            c.call_tool(
                "type_text_chars",
                {
                    "pid": self._chrome_pid,
                    "text": _FIXTURE_URL,
                },
            )
            submitted = c.call_tool(
                "press_key",
                {
                    "pid": self._chrome_pid,
                    "key": "return",
                },
            )

            self.assertIn("Focused cached AXTextField", _tool_text(submitted))

            deadline = time.monotonic() + 8.0
            title = ""
            while time.monotonic() < deadline:
                title = _window_title(c, self._chrome_pid, self._window_id)
                if _EXPECTED_TITLE in title:
                    break
                time.sleep(0.25)

        self.assertIn(_EXPECTED_TITLE, title)


if __name__ == "__main__":
    unittest.main(verbosity=2)
