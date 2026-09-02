# Version Management Rules

Defines how ccx-build-package reads, increments, and propagates the version number across
project files. Referenced by SKILL.md at runtime (Read tool). The version is a
**project-wide** value whose single source of truth is the project-root `VERSION` file —
there is no per-feature version.

## Version Format

The unified scheme is 4-segment `X.Y.Z.B`. A 3-segment `X.Y.Z` is tolerated on read and
treated as `X.Y.Z.0`.

| Segment | Name | Position |
|---------|------|----------|
| 1st | Major | `X` |
| 2nd | Minor | `Y` |
| 3rd | Patch | `Z` |
| 4th | Build | `B` |

## Segment Semantics (Promotion Conditions)

The canonical definition of these conditions lives in `.kiro/steering/distribution.md`
("Unified 4-Segment Scheme"); they are restated here so this skill is self-contained.
`ccx-version-manage` (which advances the same `VERSION` file during development) restates
the same table in its own rules.

| Segment | 名前 | インクリメントする条件 | インクリメント時のリセット |
|---------|------|----------------------|--------------------------|
| `X` | Major | 大型／破壊的アップデート。開発初期（リリース前）は `0`、最初の正式リリースで `1` に上げる。以後も **開発者の明示的承認でのみ** 上げる。 | `Y` `Z` `B` を 0 |
| `Y` | Minor | 実装マイルストーンが完了したとき。 | `Z` `B` を 0 |
| `Z` | Patch | 不具合修正・小さな要望への対応。 | `B` を 0 |
| `B` | Build | 開発中に修正して再ビルドするたび。 | — |

`ccx-build-package` はリリース時に走り、リリース意図に一致するセグメントを上げる（実装完了 → `Y`、
修正 → `Z`、機能変更のない再パッケージ → `B`。`X` は承認時のみ）。`ccx-version-manage` は実装中に走り、
既定では再ビルドごとに `B` を上げ、任意で `patch` / `minor` / `major` を指定して上位を上げる。

## Step 1: Read Current Version

The canonical source is the project-root `VERSION` file (a single line holding `X.Y.Z.B`).

1. Read the project-root `VERSION` file and take its trimmed content as the current version.
2. If it is 3-segment (`X.Y.Z`), treat it as `X.Y.Z.0`.
3. If the file does **not** exist or is empty (this is the first time the project is packaged),
   treat the current version as absent and set the initial version to `0.1.0.0` (see Step 2,
   first-packaging path).
4. If the value is present but does not match `X.Y.Z` or `X.Y.Z.B`, display a warning and
   restart numbering from `0.1.0.0`:
   ```
   ⚠️  警告: VERSION ファイルの値 "{value}" が不正な形式です。0.1.0.0 から採番を再開します。
   ```

> Do not read the version from any `.kiro/specs/{feature}/spec.json` — the version is
> project-wide and lives only in the `VERSION` file (and, mirrored, `manifest.json`).

## Step 2: Determine Next Version

Advance the segment that matches the **release intent**, following the Segment Semantics
table above. Reset all lower segments to 0 (except a Build bump, which resets nothing).

- **First packaging** (version was absent): `0.1.0.0`.
- **Major** (developer-approved — see Step 3): `X`+1, reset `Y` `Z` `B` to 0.
  - example: `1.2.4.3` → `2.0.0.0`
- **Minor** (an implementation milestone completed / feature re-release): `Y`+1, reset `Z` `B` to 0.
  - example: `1.2.4.3` → `1.3.0.0`
- **Patch** (bug fix or small request): `Z`+1, reset `B` to 0.
  - example: `1.2.4.3` → `1.2.5.0`
- **Build** (re-package with no functional change): `B`+1.
  - example: `1.2.4.3` → `1.2.4.4`
- If the release intent is unclear, default to a **Build** bump (`B`+1).

## Step 3: Major Version Suggestion (propose only, never auto-apply)

Evaluate whether a Major version bump may be warranted. Signals include: the developer declares the tool "complete", or a large/breaking update is described.

- If a Major bump seems warranted, display a suggestion and ask the developer to decide. Do **not** apply a Major bump automatically:
  ```
  💡 メジャーバージョン更新の提案: {reason}
     メジャーバージョンを {current-major} → {suggested-major} に更新しますか？
     （更新する場合、マイナー・パッチ・ビルドは 0 にリセットされます。確定はユーザーの承認が必要です）
  ```
- Only if the developer explicitly approves, set the version to `{suggested-major}.0.0.0` and skip the increment from Step 2.

## Step 4: Propagate the Confirmed Version

Once the next version is confirmed:

1. Write the confirmed version into the project-root `VERSION` file using the Write tool
   (overwrite), followed by a trailing newline. This is the canonical single-line file a
   running tool reads to report its version, and the shared source of truth with
   `ccx-version-manage`.
2. Check whether a `manifest.json` exists in the project (Glob for `manifest.json`). If it exists:
   - Look for a `version` field within it and update it to the same confirmed version using the Edit tool.
   - If `manifest.json` exists but contains no recognizable `version` field, display a warning and skip it (do not invent a field):
     ```
     ⚠️  警告: manifest.json に version フィールドが見つかりませんでした。VERSION のみ更新します。
     ```
3. The `VERSION` file and `manifest.json` (when present) must both carry the identical
   confirmed version string after this step. Do **not** write the version into any
   `.kiro/specs/{feature}/spec.json`.

> Development builds during implementation are handled by `ccx-version-manage`, which advances the **same** `VERSION` file and the same 4-segment `X.Y.Z.B` scheme (B = build/fix number) without packaging or a security gate. Release and development are one continuous version sequence; this step records the build being distributed. The last released version is captured by `CHANGELOG.md` and `Release/`.

## Output of This Step

Return the confirmed version string to the orchestrator so downstream steps (release directory naming `{tool-name}-{version}`, CHANGELOG entry, package zip name) all use the same value.
