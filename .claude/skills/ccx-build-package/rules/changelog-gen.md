# Changelog Generation Rules

Defines how ccx-build-package builds `CHANGELOG.md` from git history in Keep a Changelog format. Referenced by SKILL.md at runtime (Read tool).

Format basis: [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) (see research.md).

## Step 1: Retrieve Commit Log

Use the Bash tool to read git history:

- Determine the commit range since the last packaged version:
  - If the existing `CHANGELOG.md` records a previous version tag/commit, gather commits since then.
  - If there is no previous CHANGELOG entry (first packaging), gather the full history.
- Suggested command (adjust range as needed):
  ```
  git log --pretty=format:"%s" {range}
  ```
- If git history is unavailable or the command fails, display a warning and continue with an empty changelog entry (do not abort):
  ```
  ⚠️  警告: git ログの取得に失敗しました。CHANGELOG のエントリは空になります。手動で追記してください。
  ```

## Step 2: Classify Commits into Keep a Changelog Categories

Map each commit message to one of the six standard categories:

| Category | Meaning | Commit hints |
|----------|---------|--------------|
| Added | New features | `feat:`, `add:` |
| Changed | Changes to existing functionality | `change:`, `refactor:`, `perf:` |
| Deprecated | Soon-to-be-removed features | `deprecate:` |
| Removed | Now-removed features | `remove:` |
| Fixed | Bug fixes | `fix:` |
| Security | Vulnerability fixes | `security:` |

- Commits carrying a recognizable Conventional Commits prefix map by the table above.
- Commits with no recognizable prefix fall back to the **Changed** category.
- Omit purely mechanical commits (e.g. merge commits, `chore:` with no user impact) from the changelog.

## Step 3: Format the New Version Entry

Produce a version block in Keep a Changelog format:

```markdown
## [{version}] - {YYYY-MM-DD}

### Added
- {item}

### Changed
- {item}

### Fixed
- {item}
```

- Version heading: `## [{confirmed-version}] - {ISO date}` (date format `YYYY-MM-DD`).
- Include only categories that have at least one item; omit empty categories.
- Use the confirmed version from the version-management step.
- This project does not use the `Unreleased` section — every generated entry is for a concrete confirmed version.

## Step 4: Write / Update CHANGELOG.md

- Target file: `CHANGELOG.md` at the project root.
- If it already exists: insert the new version block at the top (immediately after the `# Changelog` title, before the previous most-recent entry). Newest version first.
- If it does not exist: create it with the standard header, then the new version block:
  ```markdown
  # Changelog

  このプロジェクトの主な変更点を記録します。
  フォーマットは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に準拠します。

  ## [{version}] - {YYYY-MM-DD}
  ...
  ```

## Output of This Step

The root `CHANGELOG.md` now contains the new version entry at the top. The orchestrator later copies this file into `Release/{tool-name}-{version}/CHANGELOG.md` (package-assembly step).
