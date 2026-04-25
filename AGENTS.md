# Repository Guidelines

This repo is the Windows port of `cua-driver`: a .NET MCP/server driver for background-safe UI automation, target-window capture, and the visual agent cursor overlay.

## Collaboration & Git Safety

- Other agents or humans may be working in this repo at the same time. Do not run destructive git operations that could discard or rewrite their work, including `git reset --hard`, `git clean -fd`, force checkouts/restores, or `git push --force`, unless explicitly requested and coordinated.
- Keep commits atomic. One commit should represent one logical change that is independently reviewable.
- Stage and commit only the files owned by the current task. Use explicit pathspecs for `git add` and `git commit`; do not sweep unrelated dirty files into a commit.
- Before committing, inspect staged changes with `git diff --cached --stat` and unstage or avoid unrelated pre-staged files.
- If subagents are used, give each one a narrow, explicit ownership set before it edits files, and keep concurrent ownership disjoint.
- Completed subagent work should report the commit SHA, changed files, validation commands, and any follow-up risks before the subagent is closed.

## Repository Layout

- `src/CuaDriver.Win/`: .NET Windows driver, MCP tools, UIA tree, input lanes, capture, cursor overlay, config, and recording.
- `src/CuaDriver.Win/Cursor/`: visual agent cursor overlay and motion/rendering logic.
- `src/CuaDriver.Win/Tools/`: tool definitions and tool-level route orchestration.
- `src/CuaDriver.Win/Input/`: targeted window-message and input route implementations.
- `src/CuaDriver.Win/Browser/`: browser-specific safe routes and CDP helpers.
- `src/CuaDriver.Win/Capture/`: target-window screenshot/capture code.
- `src/CuaDriver.Win/HardCases/`: child-session/AppBroadcast scaffolding and probes.
- `tests/integration/`: pytest integration checks against the built executable.
- `docs/`: design, safety, capture, and hard-case implementation notes.
- `scripts/`: build, install, uninstall, and test scripts.

## Quick Commands

- Build: `dotnet build cua-driver-win.sln`
- Publish: `scripts\build.ps1`
- Publish self-contained: `scripts\build.ps1 -SelfContained`
- Run tests: `scripts\run-tests.ps1`
- Install user-level MCP binary: `scripts\install.ps1 -SelfContained`
- Start daemon: `%LOCALAPPDATA%\Programs\CuaDriverWin\cua-driver-win.exe serve`

## Engineering Guidelines

- Preserve the no-regression contract for mutating actions: do not move the real cursor, do not steal foreground focus, and report the actual route/lane in receipts.
- Prefer public, background-safe routes first: UIA patterns, IA2/MSAA where safe, targeted `HWND` messages for classic controls, and CDP for Chromium when configured.
- Refuse unsafe parent-session fallbacks rather than reporting unverified delivery as success.
- Keep MCP-facing prompts, tool descriptions, and docs aligned with `docs/agent-routing.md`: agents should reuse windows, snapshot a specific `(pid, window_id)`, prefer `element_index`, and treat foreground/parent-cursor flags as explicit user opt-ins.
- Keep browser-specific guardrails strict. Browser UIA providers can foreground targets unexpectedly, so only allow routes that are known background-safe or explicitly configured.
- Keep cursor work visually aligned with the official CUA feel while respecting Windows DPI, layered-window, and z-order constraints.
- Do not introduce broad refactors while fixing one route or one visual behavior; split mechanical cleanup from behavior changes.

## Testing Guidelines

- For driver/tool changes, run `dotnet build cua-driver-win.sln` while iterating.
- Before committing runtime changes, run `scripts\run-tests.ps1`.
- For install/runtime behavior changes, reinstall with `scripts\install.ps1 -SelfContained` and restart the installed daemon before reporting the result.
- Pair bug fixes with targeted regression coverage when the repo has a practical seam for it.
- If validation cannot be run, state that clearly in the final response and explain why.

## Commit & Pull Request Guidelines

Imported from `/trope`: commit titles use the form `<area>(<type>): <imperative summary>` and should stay under roughly 72 characters.

- Use area prefixes aligned to this repo: `cursor`, `tools`, `input`, `browser`, `capture`, `recording`, `config`, `docs`, `tests`, `scripts`, `repo`.
- Reserve `repo` for meta-repo maintenance only, such as contributor policy, tooling, or repository-wide housekeeping.
- Common types: `fix`, `feat`, `refactor`, `test`, `docs`, `chore`, `ci`.
- Keep commits atomic: one logical change; split by area when possible; avoid mixing refactors/formatting with behavior changes.
- Bugs should include a regression test when practical; keep the fix and test in the same commit when they touch the same area.
- Use the commit body for context, issue references, and observable behavior changes when the title is not enough.

Examples:

```text
cursor(fix): Smooth gaussian bloom falloff
tools(fix): Refuse unsafe browser text fallback
input(refactor): Split HWND key routing helpers
tests(test): Cover permission probe output
docs(chore): Refresh capture lane notes
repo(chore): Add agent contribution guidelines
```
