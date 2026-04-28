## Summary

<!-- What changed? Keep this concise and concrete. -->

## Related Issue

<!-- Link the bug report, enhancement request, or design discussion. -->

## Area

<!-- Check all that apply. -->

- [ ] Windows driver
- [ ] macOS driver
- [ ] MCP tools
- [ ] Input routing
- [ ] Browser automation
- [ ] Capture or screenshots
- [ ] Visual agent cursor
- [ ] Recording or replay
- [ ] Configuration
- [ ] Packaging or install
- [ ] Documentation
- [ ] Tests

## Safety Contract

<!-- Required for runtime changes. Leave unchecked only when not applicable and explain why below. -->

- [ ] Mutating actions do not move the user's real cursor.
- [ ] Mutating actions do not steal foreground focus.
- [ ] Unsafe routes are explicit opt-ins or refusals, not hidden fallbacks.
- [ ] Receipts report the actual route and lane.
- [ ] Receipts preserve `background_safe`, `cursor_moved`, and `foreground_changed`.
- [ ] Browser routes avoid unsafe UIA/provider fallbacks unless explicitly configured.

## Validation

<!-- Check every command you ran. Note any command you could not run and why. -->

- [ ] `dotnet build trope-cua.sln`
- [ ] `scripts\run-tests.ps1`
- [ ] `cd native/macos/trope-cua && swift build`
- [ ] `cd native/macos/trope-cua && ./scripts/test.sh`
- [ ] `git diff --check`
- [ ] Installed or restarted the affected daemon for runtime/install changes.
- [ ] Added or updated targeted regression coverage.
- [ ] Docs-only change; runtime tests are not applicable.

## Evidence

<!-- Include relevant receipts, logs, screenshots, recordings, or before/after output. Redact private data. -->

## Risks and Follow-Up

<!-- Call out platform gaps, unsupported targets, deferred tests, or known follow-up work. -->
