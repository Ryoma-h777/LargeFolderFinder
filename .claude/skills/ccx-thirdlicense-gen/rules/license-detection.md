# License Detection Rules

Defines how ccx-thirdlicense-gen discovers dependencies, determines their licenses, judges attribution obligation, and flags license-conflict concerns. Referenced by SKILL.md at runtime (Read tool).

This logic was migrated from `ccx-build-package/rules/license-scan.md` and generalized from a GPL/LGPL binary check into a broader concern-flagging model. This skill is independent — it does not share code or rule files with `ccx-build-package` or `ccx-security-check`.

## Step 1: Discover Dependency Files

Use the Glob tool to locate dependency manifests in the project:

| Ecosystem | Glob pattern | Package source |
|-----------|--------------|----------------|
| Node / JS / TS | `package.json` | npmjs.com |
| Python (pip) | `requirements.txt` | PyPI |
| Python (PEP 621) | `pyproject.toml` | PyPI |
| .NET / C# | `*.csproj` | NuGet |

- Exclude vendored/third-party trees from the scan root (e.g. `node_modules/`, `.venv/`, `bin/`, `obj/`).
- If no dependency files are found, record an empty package list. The orchestrator continues with "サードパーティ依存パッケージなし" (no third-party dependencies).

## Step 2: Extract Package List

For each discovered dependency file, Read it and extract package name + version:

- `package.json`: read `dependencies` and `devDependencies`. Prioritize runtime `dependencies` for distribution; note `devDependencies` separately (usually no attribution obligation, but record for completeness).
- `requirements.txt`: parse `name==version` / `name>=version` lines. Ignore comments (`#`) and editable installs (`-e`).
- `pyproject.toml`: read `[project.dependencies]` / `[tool.poetry.dependencies]`.
- `*.csproj`: read `<PackageReference Include="name" Version="version" />` elements.

Produce a `PackageLicenseInfo[]` list where each entry has: `name`, `version`, `license` (initially `UNKNOWN`), `attributionRequired` (initially `false`), `licenseText` (optional), `concern` (optional).

## Step 3: Determine License Per Package

For each package, determine its license type via WebSearch/WebFetch against the ecosystem source:

- Query pattern: `{package_name} {version} license`
- Preferred sources by ecosystem: PyPI project page, npmjs.com package page, NuGet.org package page, or the package's GitHub repository (LICENSE file / repository metadata).
- Record the identified license type (MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, LGPL-x, GPL-x, MPL-2.0, Proprietary, etc.).
- If the license cannot be determined, set `license = "UNKNOWN"`, record `「ライセンス種別未確認」`, and continue scanning the remaining packages (never abort the whole scan for one unknown).

**Do not** include source code or any secret in WebSearch queries — only package name and version.

## Step 4: Determine Attribution Obligation

Set `attributionRequired = true` for license types that require attribution: MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, MPL-2.0, LGPL-*, GPL-*.

Set `attributionRequired = false` (with a note) only for licenses explicitly confirmed to carry no attribution obligation (rare; verify before setting false). `UNKNOWN` packages are treated as `attributionRequired = true` (conservative default) until manually verified — flag this in the output.

## Step 5: License-Conflict Concern Detection

This step generalizes beyond a simple GPL/LGPL binary check. Evaluate each package against the following known concern patterns and attach a `concern` entry when matched:

| Pattern | Trigger | Concern text |
|---------|---------|---------------|
| Copyleft (strong) | GPL (any version) | `GPL: ソースコード開示義務を伴うため、配布形態によっては法的リスクがある` |
| Copyleft (weak) | LGPL (any version) | `LGPL: 静的リンク時はソース開示義務が生じ得る。リンク方式を確認すること` |
| Copyleft (network) | AGPL (any version) | `AGPL: ネットワーク経由の利用でもソース開示義務が生じ得る（SaaS 配布時は特に注意）` |
| Patent clause | Apache-2.0 with known patent disputes, or any license with an explicit patent retaliation clause | `特許条項: 配布者と利用者間の特許条件を確認すること` |
| Commercial restriction | License text or package metadata explicitly restricts commercial use (e.g. "non-commercial", "personal use only") | `商用利用制限の可能性: ライセンス条件で商用配布が制限されている可能性がある` |
| Unverified | `license = UNKNOWN` | `ライセンス種別未確認: 手動確認が必要` |

- A package may match zero, one, or multiple patterns; record all matches.
- Do **not** propose a workaround or fix for any concern — only record what was found (Out of Boundary).
- Ambiguous cases that do not clearly match a pattern above are not flagged as concerns; only flag confirmed pattern matches to avoid false positives that would erode trust in the report.

## Output of This Step

Return the completed `PackageLicenseInfo[]` (including `attributionRequired` and `concern` fields) to the orchestrator. This feeds both the concern report (Step 3 of SKILL.md flow) and the Notice Generator (final THIRD_PARTY_LICENSES content).
