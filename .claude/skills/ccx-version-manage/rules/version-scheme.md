# Version Scheme (Unified 4-Segment `X.Y.Z.B`)

Referenced by SKILL.md at runtime (Read tool). Defines how `ccx-version-manage` advances
the version during local development. The version is a **project-wide** value — there is no
per-feature version.

## One Field, One Scheme

Release and development share **one** canonical version source (the project-root `VERSION`
file) and **one** format: `X.Y.Z.B`.

### Segment Semantics (Promotion Conditions)

The canonical definition of these promotion conditions lives in
`.kiro/steering/distribution.md` ("Unified 4-Segment Scheme"). They are restated here so
this skill is self-contained (per tech.md "Skill Independence" — shared definitions are
recorded in steering, never referenced from another skill's rules):

| Segment | 名前 | インクリメントする条件 | リセット |
|---------|------|----------------------|---------|
| `X` | Major | 大型／破壊的アップデート。開発初期（リリース前）は `0`、最初の正式リリースで `1`。以後も **開発者の明示的指定でのみ** 上げる。 | `Y` `Z` `B` を 0 |
| `Y` | Minor | 実装マイルストーンが完了したとき。 | `Z` `B` を 0 |
| `Z` | Patch | 不具合修正・小さな要望への対応。 | `B` を 0 |
| `B` | Build | 開発中に修正して再ビルドするたび。 | — |

A version like `0.1.1.0` is a valid **release**; `0.1.1.1` is simply the next build/fix,
which may itself be released as a patch. There is **no separate dev-only version** — local
dev builds and releases are one continuous sequence. `ccx-version-manage` advances it during
implementation (without packaging or a security gate), **by default incrementing `B`**;
`ccx-build-package` advances the same `VERSION` file when cutting a distribution and records
that build in `CHANGELOG.md` / `Release/`.

## Step 1: Read Current Version

The canonical source is the project-root `VERSION` file (a single line holding `X.Y.Z.B`).

1. Read the project-root `VERSION` file.
2. If the file is **absent or empty** (first build): initialize to `0.1.0.0` and skip to Step 3.
3. If it holds a 3-segment `X.Y.Z`: treat it as `X.Y.Z.0` (append build segment `0`).
4. If it holds a 4-segment `X.Y.Z.B`: use it as-is.
5. If the content is present but matches neither form: warn and restart from `0.1.0.0`:
   ```
   ⚠️  警告: VERSION ファイルの値 "<value>" が不正な形式です。0.1.0.0 から採番を再開します。
   ```

> The `VERSION` file is the single source of truth. Do not read the version from any
> `.kiro/specs/{feature}/spec.json` — the version is project-wide, not per-feature.

## Step 2: Determine the Next Version

Read the optional `target` argument (`major` | `minor` | `patch`). When it is omitted,
the default action is a **build increment**.

| `target` | Action | Example (`1.2.3.4` →) |
|----------|--------|-----------------------|
| _(none)_ | `B` += 1 | `1.2.3.5` |
| `patch`  | `Z` += 1, `B` = 0 | `1.2.4.0` |
| `minor`  | `Y` += 1, `Z` = 0, `B` = 0 | `1.3.0.0` |
| `major`  | `X` += 1, `Y` = 0, `Z` = 0, `B` = 0 | `2.0.0.0` |

The result is always a 4-segment `X.Y.Z.B` string.

## Step 3: Propagate the Confirmed Version

Write the confirmed version to exactly the following targets:

1. **Root `VERSION` file** (Write tool; overwrite): the version string followed by a
   trailing newline. This is the canonical single-line file a running tool reads to
   report its version, and the source of truth for the next increment.
2. **`manifest.json`** — only if it exists **and** already contains a `version` field
   (Glob to locate, then Edit): set `version` to the same string.
   - If `manifest.json` exists but has no recognizable `version` field, warn and skip:
     ```
     ⚠️  警告: manifest.json に version フィールドが見つかりませんでした。VERSION のみ更新します。
     ```

All updated files must carry the identical confirmed version string after this step.
Do **not** write the version into any `.kiro/specs/{feature}/spec.json` — that would
reintroduce a per-feature version that this project no longer uses.

## Relationship to `ccx-build-package`

Both skills operate on the **same** project-root `VERSION` file and the same `X.Y.Z.B`
scheme, so their increments are interchangeable:

- `ccx-version-manage` advances the build number (or a chosen semantic segment) during
  implementation — no security gate, no packaging. Use it to version and test locally.
- `ccx-build-package` advances the same version when producing a distribution (per its own
  `rules/version-management.md`), then writes `CHANGELOG.md` and `Release/`.

The last **released** version is recorded by `CHANGELOG.md` and `Release/{tool}-{version}/`,
not by a separate field — the `VERSION` file always holds the latest build, dev or release.
