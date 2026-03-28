# Release Automation v1

## Goal

Automatically create a GitHub Release when a pull request is merged into `main`.

## Trigger

- GitHub Actions event: `pull_request.closed`
- Conditions:
  - `pull_request.merged == true`
  - base branch is `main`

## Versioning Strategy

- Semantic Versioning with Git tag format: `vX.Y.Z`
- Exactly one PR label is required:
  - `major`
  - `minor`
  - `patch`
- If no label or multiple labels are found, the workflow fails.
- Next version is computed from the latest existing `v*.*.*` tag.
- If no release tag exists, base version is `v0.0.0`.

## Release Format

- Title: `vX.Y.Z: <PR title>`
- Description:
  - PR title and URL
  - PR body
  - commit list included in the PR

## Safety Rules

- Workflow permissions are minimal:
  - `contents: write`
  - `pull-requests: read`
- `concurrency` is enabled (`release-main`) to prevent duplicate version creation on near-simultaneous merges.
- If the computed tag already exists, the workflow fails and does not create a duplicate release.

## Files

- `.github/workflows/release-on-pr-merge.yml`
- `.github/pull_request_template.md`
