# Contributing

Thanks for contributing to `trope-cua`.

By contributing, you agree that your contributions are licensed under the MIT
License in this repository.

## Development

Trope CUA carries separate native implementations for Windows and macOS.
Develop and validate on the platform you are changing.

Windows:

```powershell
dotnet build trope-cua.sln
.\scripts\build.ps1
dotnet format --verify-no-changes --no-restore --verbosity minimal
.\scripts\run-tests.ps1
```

macOS:

```bash
cd native/macos/trope-cua
swift build
./scripts/test.sh
./scripts/install-local.sh
```

- macOS builds, installs, and permission checks must run on macOS.
- Windows integration tests must run on Windows.
- Docs-only changes can use `git diff --check`.
- Keep changes small and focused. Avoid mixing behavior changes with broad
  refactors or formatting-only churn.

## Safety contract

These drivers exist to automate target windows without stealing foreground
focus or moving the user's hardware cursor. Mutating routes must report their
actual route and lane, and must preserve the no-regression fields in action
receipts.

Prefer background-safe routes first: UIA patterns, IA2/MSAA where safe, targeted
`HWND` messages for classic controls, and CDP for Chromium when configured.
Refuse unsafe fallbacks instead of reporting unverified delivery as success.

## Attribution

This project includes MIT-licensed macOS driver code from `trycua/cua`; see
`NOTICE` for attribution. Add file-level notices and preserve upstream license
text before copying substantial source from upstream projects.
