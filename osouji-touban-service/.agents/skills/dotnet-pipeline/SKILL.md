---
name: dotnet-pipeline
description: Run the required .NET restore, build, and test pipeline after implementation work. Use when Codex has finished code changes in a .NET repository and must execute restore, build, and a full regression test pass with elevated permissions, non-interactive commands, and optional --project scoping only for restore/build when the changed project is obvious.
---

# Dotnet Pipeline

Run the post-implementation .NET verification pipeline in a fixed order.

## Workflow

1. Run `dotnet restore`.
2. Run `dotnet build`.
3. Run `dotnet test`.

Always request elevated permissions before running these commands.

Always use non-interactive commands that terminate on their own.

If the changed project is clearly identified, `dotnet restore --project <path>` or `dotnet build --project <path>` is acceptable.

Do not narrow `dotnet test`. Run the full test suite to catch regressions.

## Execution Notes

Run commands from the repository root unless a specific project path is required.

Report failures with the failing command and the relevant error summary.
