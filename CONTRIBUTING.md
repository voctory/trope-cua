# Contributing

Thanks for contributing to `trope-cua`.

By contributing, you agree that your contributions are licensed under the MIT
License in this repository.

## Development

- Build with `dotnet build trope-cua.sln`.
- Run formatting checks with `dotnet format --verify-no-changes --no-restore --verbosity minimal`.
- Run integration tests with `scripts\run-tests.ps1`.
- Keep changes small and focused. Avoid mixing behavior changes with broad
  refactors or formatting-only churn.

## Safety contract

This driver exists to automate target windows without stealing foreground focus
or moving the user's hardware cursor. Mutating routes must report their actual
route and lane, and must preserve the no-regression fields in action receipts.

Prefer background-safe routes first: UIA patterns, IA2/MSAA where safe, targeted
`HWND` messages for classic controls, and CDP for Chromium when configured.
Refuse unsafe fallbacks instead of reporting unverified delivery as success.

## Attribution

This project includes MIT-licensed macOS driver code from `trycua/cua`; see
`NOTICE` for attribution. Add file-level notices and preserve upstream license
text before copying substantial source from upstream projects.
