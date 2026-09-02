---
name: ccx-version-manage
description: Assign a version to an in-progress local development build. Runs independently of the distribution phase (ccx-build-package / Security Gate 3) at any time, advancing build number B under the shared 4-segment X.Y.Z.B scheme and stamping the root VERSION file and manifest.json. Targets the whole project (workspace); no feature name is required. Use when the user wants to number or bump a local build, or identify "which build am I running" during implementation. トリガー例:「バージョンを採番」「ビルド番号を上げる」「今どのビルドか分かるようにしたい」「ローカルビルドにバージョンを振る」「開発ビルドにバージョンを付けたい」
allowed-tools: Read, Write, Edit, Glob
argument-hint: "[major|minor|patch]"
---

# ccx-version-manage

## Overview

`/ccx-version-manage` assigns a version to an in-progress local development build.
It targets the **whole project (workspace)** and does not require a specific feature.
It runs **at any time**, independently of `ccx-build-package` (the distribution phase), and
does not need Security Gate 3 or any generated artifacts (README / THIRD_PARTY_LICENSES).

Versions use the same **4-segment `X.Y.Z.B` scheme (B = build/fix number)** as releases.
The single source of truth for the version is the **root `VERSION` file**. By default the
build number `B` is incremented by 1 and propagated to the `VERSION` file and to
`manifest.json` (when present). Because releases and development share the same 4-segment
scheme and the same `VERSION` file, it is always clear "which build am I looking at" during
local install and behavior checks, and that build can be shipped as-is as a patch release.

**Flow:**
1. Parse arguments (optional `target` only; no feature name)
2. Compute the version (per `rules/version-scheme.md`, based on the `VERSION` file)
3. Stamp `VERSION` / `manifest.json` and print runtime visibility guidance

## Usage

```
/ccx-version-manage [major|minor|patch]

例:
  /ccx-version-manage          # ビルド番号を +1（例: 0.1.0.2 → 0.1.0.3）
  /ccx-version-manage patch    # パッチを上げてビルド0から（例: 0.1.0.3 → 0.1.1.0）
  /ccx-version-manage minor    # マイナーを上げてビルド0から（例: 0.1.0.3 → 0.2.0.0）
```

**Arguments:**
- `target` (optional) — bump a semantic segment and reset the build number to 0 (`major` | `minor` | `patch`). When omitted, increment the build number `B` by 1.
- There is no feature-name argument (this skill targets the whole project).

## Rule files

- `rules/version-scheme.md` — numbering and multi-file propagation for the unified 4-segment scheme (based on the `VERSION` file)

## Output

- **Console**: the assigned version, stamped files, and runtime visibility guidance
- **Files**:
  - root `VERSION` (source of truth)
  - `manifest.json` (only when it exists and has a `version` field)

---

## Step 1: Parse Argument

- 1st positional argument → `target` (optional, `major` | `minor` | `patch`)
- No feature-name argument is accepted. Any positional argument is always interpreted as `target`.

If `target` is present but not one of `major` / `minor` / `patch`, display a warning and
fall back to the default build increment (do not stop):

```
⚠️  警告: target "<value>" は無効です（major | minor | patch のいずれか）。ビルド番号を +1 します。
```

> Note: This skill is **not** gated by Security Gate 3 — it is designed to run during
> implementation. Do not check for `security-report-review.md`.
>
> Note: This skill targets the **whole project**. It does not take a feature name and does
> not read any `.kiro/specs/{feature}/spec.json` — the version lives in the root `VERSION`
> file.

Display the startup header and proceed:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 ccx-version-manage
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
バージョンを採番します（target: <target または "build">）。
```

---

## Step 2: Compute and Stamp the Version

1. Read `.claude/skills/ccx-version-manage/rules/version-scheme.md`.
2. Apply its rules to:
   - read the current `version` from the root `VERSION` file (initializing to `0.1.0.0` if the
     file is absent or empty),
   - determine the next version (build increment by default, or a semantic bump per `target`),
   - propagate the confirmed 4-segment version to the root `VERSION` file and to
     `manifest.json` (only if present with a `version` field).
3. Retain the confirmed version string for Step 3.

Do not hardcode version logic in this file — the rules file is the single source of truth.

---

## Step 3: Report and Runtime Guidance

Display the stamped version and how to surface it at runtime:

```
✅ バージョンを採番しました: <version>

刻印先:
  - VERSION            （プロジェクトルート・正）
  - manifest.json      （存在時のみ）

挙動確認での見える化:
  ローカルビルド/インストール後、ツールが起動時ログや `--version` で
  VERSION ファイル（または manifest.json）の値を表示するようにしておくと、
  どのビルドを動かしているか一目で分かります。

補足:
  リリースと同じ 4桁スキーム・同じ VERSION ファイルを進めています。配布用の確定・
  パッケージ化を行うときは `/ccx-build-package` を実行してください。
```

---

## Output Description

Provide output in Japanese:

1. **採番結果**: 確定したバージョン `X.Y.Z.B`
2. **刻印先**: 更新した各ファイル（VERSION / manifest.json）
3. **見える化ガイド**: 実行時にバージョンを表示する運用の案内
4. **次アクション**: 継続開発は再度 `/ccx-version-manage`、配布は `/ccx-build-package`

---

## Safety & Fallback

### Error Scenarios

- **Invalid `target` (Step 1)**: show a warning and continue with a build increment (+1)
- **`VERSION` file missing/empty (Step 2)**: create it anew at `0.1.0.0` and continue (not an error)
- **Malformed `version` value (Step 2)**: show a warning, restart numbering from `0.1.0.0`, and continue
- **No `version` field in `manifest.json` (Step 2)**: show a warning, update only `VERSION`, and continue

### Boundary Notes

- This skill targets the whole project and takes no feature name. It does not read `.kiro/specs/`.
- This skill does not compile, build, or install. Local build/install is the developer's responsibility.
- It performs no security validation (Gate 3). Running at any time during implementation is prioritized.
- It advances the release-shared `VERSION` file (source of truth). Writes are limited to `VERSION` and `manifest.json` (its `version` field, when present).
- Distribution packaging, CHANGELOG, and Release generation are the responsibility of `ccx-build-package` (this skill only assigns the version).
