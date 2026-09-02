# Content Collection Rules

Defines how ccx-readme-gen gathers the information needed for the README from the spec documents. Referenced by SKILL.md at runtime (Read tool). The gathered content is format-independent; the output extension is chosen separately by `format-selection.md`.

Migrated and extended from `ccx-build-package/rules/readme-gen.md` Step 1.

The source spec directory `{spec}` was auto-resolved in SKILL.md Step 1 (this skill takes no
feature argument). All `.kiro/specs/{spec}/...` reads below use that resolved directory.

## Step 1: Basic Info (name, version, developer)

- App name: derive from `.kiro/specs/{spec}/spec.json` (`feature_name`) or from a title/heading in `design.md`/`requirements.md` if a more user-facing name is given there.
- Version: read the version from the project-root `VERSION` file (the project-wide source of truth). If it is absent or empty, record `バージョン情報未確定` — do not fabricate a version number.
- Developer: look for author/organization info in `design.md`, `requirements.md`, or steering (`product.md`). If none is found, insert a placeholder comment for the developer to fill in:
  ```html
  <!-- 開発者名を記入してください -->
  ```

## Step 2: Overview

Read `design.md` (Overview section) and `requirements.md` (Introduction) to produce a 2–3 sentence description of what the tool provides or solves. Prefer the user-facing framing from requirements.md's Introduction over technical framing from design.md.

## Step 3: Usage

Derive key user-observable operations from requirements.md's acceptance criteria (the "When [event], the system shall [response]" statements describe user-visible operations). List them as a concise usage guide, not a full requirements restatement.

## Step 4: System Requirements

Read design.md's Technology Stack section to determine: target OS, required runtime (e.g. "Node.js 20+", ".NET 8"), and any other prerequisites explicitly stated.

## Step 5: Installation Necessity Judgment

Determine whether the tool requires an installation step:
- **Requires installation**: design.md describes a setup/build step, an installer, a package to install, or configuration files to place before first run.
- **No installation required**: design.md describes a single standalone executable or script that runs as-is.
- If this cannot be determined from design.md, ask the developer directly rather than guessing:
  ```
  このツールはインストール作業が必要ですか？（例: セットアップ手順、インストーラー、設定ファイル配置）
  ```

Record this judgment for the duplication-check and assembly steps — the Installation section is entirely omitted when installation is not required (see Requirement 3.3).

## Step 6: Other Important Information

Scan design.md and requirements.md for explicitly stated:
- Known limitations or constraints
- Cautions/warnings the developer flagged
- Support contact information

Collect these verbatim or lightly summarized; do not invent information that isn't present in the source documents.

## Output of This Step

Return a structured collection object to the orchestrator:
```
CollectedContent:
  appName: string
  version: string | "バージョン情報未確定"
  developer: string | null  // null → placeholder inserted
  overview: string
  usage: string[]
  systemRequirements: string[]
  installationRequired: boolean | "unknown"  // "unknown" → ask developer (Step 5)
  otherImportantInfo: string[]
```
