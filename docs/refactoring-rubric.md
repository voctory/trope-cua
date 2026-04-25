# Refactoring Rubric

This repository is a Windows automation driver. Refactoring should make background-safe behavior easier to preserve, unsafe behavior harder to introduce, and failures easier to diagnose.

Use this document as a working rubric. It is not a style checklist for its own sake; it is the order of operations for changing the code without losing the driver contract.

## Current Baseline

Observed starting point:

- One production project: `src/CuaDriver.Win/CuaDriver.Win.csproj`.
- Current target: `net10.0-windows10.0.19041.0`.
- Nullable and implicit usings are enabled in the project file.
- Central package management is enabled through `Directory.Packages.props`; there are currently no direct NuGet package dependencies.
- Repo-level `global.json`, `Directory.Build.props`, `Directory.Packages.props`, and a committed NuGet lock file are present.
- Repo-level `.editorconfig` is present for formatting and baseline style rules.
- Integration coverage exists under `tests/integration` and is run through `scripts/run-tests.ps1`.

Do not treat this baseline as bad by default. It is the map for staged refactors.

## Refactoring Goals

The driver should keep these properties through every change:

- Mutating tools do not move the user's real cursor.
- Mutating tools do not steal foreground focus.
- Unsafe routes are explicit opt-ins, not hidden fallbacks.
- Tool receipts report the actual route, lane, cursor movement, foreground changes, and background safety.
- MCP prompts and tool schemas steer agents toward window-scoped, element-indexed, background-safe actions.
- Windows-specific constraints are documented near the code that owns them.
- Tests protect behavior before code is moved.

## Staged Plan

Prefer this order:

1. Strengthen the build and test safety net.
2. Add characterization tests around risky behavior.
3. Extract small route-specific helpers where large tools are doing too much.
4. Tighten visibility and types after behavior is covered.
5. Centralize package and analyzer policy.
6. Move code across folders/projects only when an ownership boundary is already clear.
7. Remove dead code and duplicate compatibility surfaces after callers are accounted for.

Avoid big-bang reshapes. A refactor commit should be reviewable without needing to understand unrelated behavior changes.

## Build Policy

The repo minimum is .NET 10, using a Windows target framework because desktop APIs are part of the product surface. Runtime or target framework changes should remain isolated from behavioral refactors.

Rules:

- Add `global.json` before depending on SDK-specific behavior.
- Add repo-level MSBuild defaults before turning on stricter enforcement.
- Do not combine a target framework migration with behavioral refactors.
- Keep self-contained install behavior tested after runtime or framework changes.
- Treat SDK and runtime updates as dependency changes: build, test, reinstall, and smoke the daemon.

Good eventual defaults:

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<AnalysisMode>Recommended</AnalysisMode>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
```

Turn `TreatWarningsAsErrors` on only after the current warning baseline is clean or intentionally scoped.

## Package Policy

Move package versions out of project files once there is more than one production or test project, or before adding more dependencies.

Target shape:

- `Directory.Packages.props` owns package versions.
- No floating versions in production code.
- NuGet lock files are committed once locked restore is enabled.
- Dependency changes are isolated commits.
- Security audit failures are treated as build failures once CI exists.

## Analyzer And Style Policy

Use analyzers to catch unsafe changes, not to create formatting churn.

Target shape:

- `.editorconfig` defines formatting and analyzer severities.
- `Directory.Build.props` centralizes analyzer and nullable policy.
- Suppressions include a reason and removal condition.
- Mechanical formatting commits are separate from behavior changes.

Initial analyzer work should focus on correctness rules first: nullability, disposal, async blocking, exception handling, and platform compatibility.

## Module Boundaries

The current folder structure already hints at useful ownership:

- `Tools`: MCP tool definitions and route orchestration.
- `Input`: parent-session HWND/input lanes and no-regression checks.
- `Browser`: browser classification and browser-native routes.
- `Capture`: target-window image acquisition.
- `Cursor`: visual agent cursor overlay.
- `HardCases`: child-session and restricted-capability lanes.
- `Recording`: trajectory persistence and replay.
- `Tooling`: shared tool abstractions, registry, state, and result shaping.

Refactor toward these boundaries:

- Tools decide what route is appropriate; route implementations should live outside the tool when reusable.
- Browser-specific safety rules belong in `Browser`, not scattered through every tool.
- Native Win32 interop stays behind narrow helpers.
- Cursor overlay code should not know tool semantics beyond movement, pulse, idle, and layering requests.
- Recording should observe tool calls; tools should not manually assemble recording artifacts.

Bad signs:

- A tool knows too many route internals.
- A helper named `Common`, `Util`, or `Manager` accumulates unrelated behavior.
- Browser foreground-safety logic is duplicated.
- Receipt construction is inconsistent between routes.
- A route can succeed without proving cursor and foreground stability.

## Type Discipline

Prefer explicit concepts over loose primitives where they reduce mistakes.

Candidates for stronger types:

- `window_id` / HWND identity.
- Tool route and lane names.
- Native versus resized screenshot coordinates.
- Screen coordinates versus window-local coordinates.
- Foreground/cursor regression results.
- Daemon instance identifiers.

Do not introduce value objects everywhere at once. Start where coordinate, window, or route confusion has already caused bugs.

## Async And Cancellation

The driver interacts with windows, UIA, CDP, files, subprocesses, and timers. Refactors should preserve cancellation and avoid blocking async flows.

Review carefully:

```text
.Result
.Wait()
.GetAwaiter().GetResult()
Task.Run(
Thread.Sleep(
async void
```

Preferred patterns:

- Pass `CancellationToken` through public async APIs.
- Keep `Async` suffixes for asynchronous methods.
- Use `Task.WhenAll` only when operations are genuinely independent.
- Do not hide blocking UIA or Win32 calls behind unnecessary `Task.Run`.

## Configuration

Configuration should be explicit and diagnosable.

Rules:

- Keep config models strongly typed.
- Save user config intentionally; do not mutate persistent settings as a side effect of read-only tools.
- Validate config values at the boundary.
- Keep unsafe flags named as unsafe or explicit opt-ins.
- Never persist secrets in repo config or test fixtures.

## Diagnostics And Logging

For this driver, structured tool output is part of observability.

Rules:

- Every mutating action returns a structured receipt.
- Failures should include a route-like reason that points to the missing lane or unsafe behavior.
- Diagnostic tools should return `structuredContent`, not only prose.
- Logs and receipts must not include secrets or sensitive personal data.
- Foreground and cursor guard failures should be easy to distinguish from delivery failures.

If a logging abstraction is added later, prefer structured templates over string interpolation.

## Tests

Tests should lock behavior, not private structure.

Current test tiers:

- Integration tests exercise the published executable and MCP JSON surfaces.
- Characterization tests should be added before moving high-risk route logic.

High-value tests to add before deeper refactors:

- Foreground no-regression test that keeps a separate app active while driving a target.
- Browser route refusal tests for unsafe UIA/browser fallbacks.
- Coordinate conversion tests for resized screenshots, zoom contexts, and DPI scaling.
- Cursor lifecycle tests for idle, fade, click pulse, and target-window layering.
- Daemon instance isolation tests for parallel background agents.
- Structured receipt tests for every mutating route.

Test command before committing runtime changes:

```powershell
scripts\run-tests.ps1
```

Docs-only changes can use `git diff --check`, but note explicitly when runtime tests were not run.

## Red-Flag Search List

These patterns deserve review during refactoring:

```text
.Result
.Wait()
.GetAwaiter().GetResult()
Task.Run(
Thread.Sleep(
async void
new HttpClient(
IServiceProvider
BuildServiceProvider
DateTime.Now
static mutable
catch (Exception)
throw ex;
Dictionary<string, object>
dynamic
object payload
allow_parent_sendinput
allow_parent_cursor
unsafe_allow_foreground
SetForegroundWindow
SetCursorPos
SendInput
```

Some are valid in this driver, especially Win32 APIs and explicit unsafe flags. The rule is that each occurrence should be owned, intentional, and covered by receipts or tests when it affects automation behavior.

## Scorecard

Use `0` for absent/risky, `1` for partial, and `2` for consistently enforced.

```text
Target framework policy
SDK pinning
Repo-wide build defaults
Central package management
Lock-file restore
Analyzer enforcement
EditorConfig/style enforcement
Nullable correctness
Warnings-as-errors readiness
Clear module ownership
Tool orchestration kept thin
Browser safety centralized
Input lanes isolated
Capture paths isolated
Cursor overlay isolated
Structured receipts everywhere
Foreground/cursor no-regression tests
Coordinate/DPI tests
Daemon parallel isolation tests
Async/cancellation correctness
Configuration validation
No secrets in repo
Install/runtime smoke coverage
CI required checks
```

## Definition Of Done

A refactored module is done when:

- Its owner and responsibility are obvious.
- Its public surface is minimal.
- It preserves the no-cursor/no-foreground contract.
- It reports structured success and failure information.
- It accepts cancellation for I/O or long-running work.
- It has behavior tests at the right level.
- It does not add unowned global state.
- It does not silently introduce parent-session hardware input.
- It can be understood without reading unrelated modules.
- It passes the repo test command or has a documented reason tests were not run.
