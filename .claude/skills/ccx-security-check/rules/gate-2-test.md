# Gate 2: Post-Implementation Code Scan Rules

This file defines the complete execution procedure for Gate 2 (`test`). The AI must follow each section in order, collect findings, and produce a structured report.

---

## Prerequisites

Before scanning, identify the source files to analyze:

1. Read `.kiro/specs/{feature}/design.md` — locate the **File Structure Plan** section.
2. Extract every source file path listed there (e.g., `src/`, `.claude/skills/`, etc.).
3. Use `Glob` on those paths to enumerate actual files.
4. Also read design.md to extract **declared network endpoints / hosts** (save as `declared_hosts[]` for Section 2).

If no source files are found, record a WARNING finding:
> "解析対象ファイルが未発見。design.md の File Structure Plan を確認してください。"
Then continue to the dependency check (Section 3).

---

## Section 1 — Secret Scanning (Req 3.1)

Run the following Grep patterns against all source files identified in Prerequisites.

### 1-A. Generic Secret Pattern (HIGH severity)

Apply with `Grep` — case-insensitive, all source files:

```
(?i)(password|passwd|pwd|secret|api[_-]?key|token|auth|credential)\s*[:=]+\s*['"][^'"]{8,}['"]
```

- For every match: record `{ severity: HIGH, location: <file>:<line>, description: "汎用シークレットパターン検出", value_hint: "<matched key name only — do NOT echo the actual secret value>" }`
- **IMPORTANT**: Never include the actual secret value in the report. Record the matched key/variable name and file location only.

### 1-B. Service-Specific Secret Patterns (CRITICAL severity)

Apply each pattern with `Grep` across all source files:

| Pattern | Service | Severity |
|---------|---------|----------|
| `AKIA[0-9A-Z]{16}` | AWS Access Key ID | CRITICAL |
| `ghp_[A-Za-z0-9]{36}` | GitHub Personal Access Token | CRITICAL |
| `xox[baprs]-[0-9]{12}-[0-9]{12}-\w{24}` | Slack Token | CRITICAL |
| `-----BEGIN (RSA\|EC\|OPENSSH) PRIVATE KEY` | Private Key (PEM) | CRITICAL |
| `[a-zA-Z]{3,15}:\/\/[^\/\s:@]+:[^\/\s:@]+@` | URL with embedded credentials | CRITICAL |

- For every match: record `{ severity: CRITICAL, location: <file>:<line>, description: "<service> シークレット検出", value_hint: "<pattern name only>" }`
- **IMPORTANT**: Never echo the matched secret value. Record pattern type and location only.

### 1-C. Secret Scan Summary

After running 1-A and 1-B, record a check summary entry:

```
| シークレットスキャン | ✅ (0件) / ❌ (CRITICAL N件, HIGH N件) |
```

Any CRITICAL finding from Section 1 → **gate verdict = FAIL**.

---

## Section 2 — Network Communication Detection & Design Comparison (Req 3.2, 3.3, 3.6)

### 2-A. Language-Specific Grep Patterns

Run the following Grep patterns against all source files. Collect every match with file path and line number.

**Python** (files: `*.py`):
```
import\s+(requests|urllib|aiohttp|httpx)
```
```
requests\.(get|post|put|delete)
```
```
socket\.connect
```

**JavaScript / TypeScript** (files: `*.js`, `*.ts`, `*.mjs`, `*.cjs`):
```
fetch\s*\(
```
```
axios\.(get|post|put|delete)
```
```
new\s+XMLHttpRequest
```
```
new\s+WebSocket
```

**C#** (files: `*.cs`):
```
new\s+HttpClient
```
```
new\s+WebClient
```
```
HttpWebRequest\.Create
```
```
new\s+TcpClient
```

**C++** (files: `*.cpp`, `*.cc`, `*.cxx`, `*.h`, `*.hpp`):
```
#include\s+<curl/curl\.h>
```
```
curl_easy_init\s*\(
```
```
boost::asio
```
```
WSAStartup\s*\(
```

### 2-B. Extract Host / URL from Matches

For each match found in 2-A, attempt to identify the target host or URL from the surrounding 3 lines of context. Extract strings matching:
```
https?://[^\s'">\)]+
```
or hostname literals quoted nearby.

Build `detected_calls[]` — each entry: `{ file, line, pattern, host_or_url }`.

### 2-C. Comparison Against design.md Declarations

Use `declared_hosts[]` extracted from Prerequisites.

For each entry in `detected_calls[]`, classify:

- **「設計と一致」**: The host/URL is explicitly listed in design.md's network communication section.
- **「設計外」**: The host/URL is NOT listed in design.md, OR no host could be extracted but the call exists in a file that has no corresponding declaration.

Rules:
- If `declared_hosts[]` is empty (design.md declares no external communication) AND `detected_calls[]` is non-empty → ALL detected calls are **「設計外」** (CRITICAL).
- If a detected call has no extractable host/URL, classify it as **「設計外 (要確認)」** at MEDIUM severity.
- If design.md explicitly states "no external communication" and any network call is detected → CRITICAL.

### 2-D. Network Communication Finding Table

For every entry in `detected_calls[]`, produce:

```
| ファイル | 行 | 検出パターン | 検出ホスト/URL | 分類 | 重大度 |
|--------|----|------------|-------------|------|--------|
| src/api.py | 12 | requests.get | https://api.example.com | 設計と一致 | INFO |
| src/util.py | 45 | fetch( | https://unknown.io | 設計外 | CRITICAL |
```

This table **must** appear in the Gate 2 report (Req 3.6).

### 2-E. Network Check Summary

```
| ネットワーク通信検出 | ✅ / ❌ / ⚠️ |
| 設計外通信 | ✅ なし / ❌ N件 (CRITICAL) |
```

Any `detected_calls[]` entry classified as **「設計外」** → **gate verdict = FAIL** (CRITICAL severity).

Set the report field `network_declaration`:
- `"通信あり"` if `detected_calls[]` is non-empty.
- `"通信なし"` if `detected_calls[]` is empty.

---

## Section 3 — Dependency Vulnerability Check (Req 3.4)

### 3-A. Locate Dependency Files

Use `Glob` to find the following files anywhere in the project tree:

- `**/package.json` (exclude `node_modules/`)
- `**/requirements.txt`
- `**/pyproject.toml`
- `**/*.csproj`
- `**/packages.config`

If none found, record INFO finding: "依存ファイルが見つかりませんでした。スキップします。"

### 3-B. Extract Packages and Versions

For each dependency file found:

1. **package.json** — Read file; extract entries from `dependencies` and `devDependencies` as `{ name, version }`.
2. **requirements.txt** — Read file; parse each line as `package==version` or `package>=version`; extract name + version constraint.
3. **pyproject.toml** — Read file; extract `[tool.poetry.dependencies]` or `[project] dependencies` entries.
4. **.csproj / packages.config** — Read file; extract `<PackageReference Include="..." Version="..."/>` or `<package id="..." version="..."/>`.

Build `packages[]` list: `{ name, version, file }`.

### 3-C. Ecosystem-Standard Tool Scan (Primary Method — C# / JavaScript / Python)

**Rationale** (see `tech-selection.md` Adoption Decision): each language ecosystem's own officially-maintained tool is preferred over ad-hoc WebSearch lookups — it is already present in the developer's environment, is maintained by the language authority (Microsoft / npm Inc. / PyPA), and distributes trust across independent tool sources rather than a single third-party scanner.

Run the tool that matches each dependency file found in 3-A, via `Bash`:

| Dependency file | Command | Notes |
|-----------------|---------|-------|
| `*.csproj` / `packages.config` | `dotnet list package --vulnerable --include-transitive` | Requires .NET SDK 8.0+; run from the directory containing the `.csproj` / solution |
| `package.json` | `npm audit --production --json` | Run from the directory containing `package.json`; `--production` reduces false positives from dev-only tooling |
| `requirements.txt` / `pyproject.toml` | `pip-audit` (or `pip-audit -r requirements.txt`) | If `pip-audit` is not installed, record INFO: "pip-audit未インストール。`pip install pip-audit` を推奨" and fall back to 3-D for that file |

Parse each command's output (or JSON, for `npm audit`) into `{ name, version, cve_id, severity, fixed_version }` entries. If a command fails to run (tool not installed, no network access to the advisory feed, non-zero exit due to environment issues rather than found vulnerabilities), record an INFO finding noting the failure and fall back to 3-D (WebSearch) for that dependency file's packages.

Map each tool's native severity to the Finding Severity scale using Section 4-A / 3-E below (`dotnet list package --vulnerable` and `npm audit` report CVSS-equivalent severity levels; treat unmapped severities as MEDIUM pending manual confirmation).

### 3-C2. Prioritization for WebSearch Fallback (C++ and tool failures)

C++ has no ecosystem-standard vulnerability scanner. For C++ dependencies (Conan/vcpkg manifests, if present) and for any package where 3-C could not run, prioritize the following categories for the WebSearch-based lookup in 3-D — this is a **best-effort** check, not a systematic scan:

1. **Authentication / Authorization**: `passport`, `jsonwebtoken`, `pyjwt`, `authlib`, `oauth2`, `bcrypt`, `cryptography`, `openssl`, `System.IdentityModel`
2. **HTTP / Network**: `requests`, `urllib3`, `aiohttp`, `httpx`, `axios`, `node-fetch`, `got`, `RestSharp`, `System.Net.Http`
3. **XML / JSON parsing**: `lxml`, `xml2js`, `fast-xml-parser`, `Newtonsoft.Json`, `System.Text.Json`
4. **Template / Serialization**: `jinja2`, `pyyaml`, `pickle` (any version), `serialize`
5. All packages with a version that was released **more than 1 year ago** (estimate from version number if release date unknown).

### 3-D. CVE Lookup via WebSearch (C++ / fallback only)

For each package identified in 3-C2 (and others if time permits), run `WebSearch` with these query templates. Reference `OSV.dev` (`https://osv.dev/list?q={package_name}`) in addition to the sources below for C++ (Conan/vcpkg) packages, since NVD/GitHub Advisory coverage of C++ package managers is inconsistent.

**Primary query**:
```
"{package_name} {version} CVE vulnerability 2025"
```

**Secondary query** (if primary yields nothing):
```
"site:github.com/advisories {package_name}"
```

**Tertiary query** (for specific services):
```
"site:security.snyk.io/vuln {package_name}"
```

Reference sources (check in this order):
1. **NVD** — `https://nvd.nist.gov/vuln/search?query={package_name}`
2. **GitHub Advisory Database** — `https://github.com/advisories?query={package_name}`
3. **Snyk Vulnerability DB** — `https://security.snyk.io/vuln?search={package_name}`

**IMPORTANT**: Only include `{package_name}` and `{version}` in search queries. Never include source code content, file paths, or user data in WebSearch queries.

If WebSearch fails or times out, record: "脆弱性情報の取得に失敗。手動確認を推奨: `{package_name} {version}`"

### 3-E. CVE Severity Mapping

| CVE Severity (NVD/CVSS) | Finding Severity |
|------------------------|-----------------|
| CRITICAL (CVSS ≥ 9.0) | CRITICAL |
| HIGH (CVSS 7.0–8.9) | HIGH |
| MEDIUM (CVSS 4.0–6.9) | MEDIUM |
| LOW (CVSS < 4.0) | LOW |
| No CVE found | INFO (note: checked clean) |

For each CVE found, record:
```
{ severity: <mapped>, location: "<file> — <package>@<version>", 
  description: "<CVE-ID>: <brief description>", 
  guidance: "バージョンを <fixed_version> 以上にアップグレードしてください。" }
```

### 3-F. Dependency Check Summary

```
| 依存脆弱性チェック | ✅ / ❌ / ⚠️ |
| — スキャン方式 | dotnet/npm/pip-audit（該当言語） + WebSearch（C++・ベストエフォート） |
```

---

## Section 4 — Severity Classification & Verdict (Req 3.5, 3.6)

### 4-A. Severity Definitions

| Severity | Trigger Condition |
|----------|------------------|
| **CRITICAL** | (1) Secret literal found in source code (service-specific pattern matched) OR (2) Network call to an undeclared external host detected |
| **HIGH** | (1) Generic secret pattern matched (variable assignment with 8+ char value) OR (2) Known HIGH or CRITICAL CVE in a dependency |
| **MEDIUM** | (1) Known MEDIUM CVE in a dependency OR (2) Network call to a declared host but TLS (https://) not used |
| **LOW** | Known LOW CVE in a dependency OR minor network concern (e.g., localhost-only call without declaration) |
| **INFO** | Packages checked with no CVE found; network calls fully matching design declarations |

### 4-B. Verdict Rule

| Condition | Verdict |
|-----------|---------|
| One or more **CRITICAL** findings | **FAIL** |
| No CRITICAL findings (HIGH/MEDIUM/LOW/INFO only) | **PASS** (with acknowledgment of remaining findings) |

On **FAIL**: State the count of CRITICAL findings, list their locations, and instruct:
> "実装フェーズへ差し戻しを推奨します。修正後: `/ccx-security-check {feature} test`"

On **PASS with HIGH findings**: State:
> "CRITICALは検出されませんでした。HIGH {N}件について修正を推奨します。"

### 4-C. Mandatory Report Sections

The Gate 2 report MUST contain all of the following (per Req 3.6 and 5.x):

1. **チェックサマリー表** — one row per check (シークレットスキャン / ネットワーク通信検出 / 設計外通信 / 依存脆弱性チェック)
2. **ネットワーク通信一覧表** — every detected network call with `設計と一致 / 設計外` classification (required even if the table is empty — state "検出なし")
3. **検出結果** — each finding with severity, location, description, guidance
4. **最終判定** — PASS or FAIL with CRITICAL count
5. **次のアクション** — recommended next command

---

## Execution Checklist

Follow these steps in order:

- [ ] 1. Read design.md → extract File Structure Plan + declared hosts
- [ ] 2. Glob source files from File Structure Plan paths
- [ ] 3. Run Section 1-A (generic secret scan — Grep all source files)
- [ ] 4. Run Section 1-B (service-specific secret scan — 5 patterns)
- [ ] 5. Run Section 2-A (network pattern Grep — per language)
- [ ] 6. Run Section 2-B (extract hosts/URLs from matches)
- [ ] 7. Run Section 2-C (compare against design.md declared hosts → classify)
- [ ] 8. Run Section 2-D (build network comparison table)
- [ ] 9. Run Section 3-A (Glob dependency files)
- [ ] 10. Run Section 3-B (extract package names + versions)
- [ ] 11. Run Section 3-C (ecosystem-standard tool scan: `dotnet list package --vulnerable` / `npm audit` / `pip-audit`)
- [ ] 12. Run Section 3-C2 + 3-D (WebSearch CVE lookup for C++ packages and any 3-C failures)
- [ ] 13. Apply Section 4-A severity classification to all findings
- [ ] 14. Apply Section 4-B verdict rule
- [ ] 15. Generate report per Section 4-C + rules/report-format.md
- [ ] 16. Save to `.kiro/specs/{feature}/security-report-test.md`

---

## Worked Example: `password = "secret123"`

The following demonstrates how Section 1 detects a hardcoded secret:

**Source file** `src/config.py`:
```python
password = "secret123"
```

**Step 3** (Section 1-A Grep):
Pattern `(?i)(password|passwd|pwd|secret|api[_-]?key|token|auth|credential)\s*[:=]+\s*['"][^'"]{8,}['"]`
→ matches `password = "secret123"` (key name: `password`, value length: 9 chars ≥ 8)

**Finding produced**:
```
severity: HIGH
location: src/config.py:1
description: 汎用シークレットパターン検出 — 変数名 "password" に8文字以上のリテラル値が代入されています
guidance: 環境変数またはシークレット管理サービスを使用してください（例: os.environ.get("PASSWORD")）
value_hint: password (実際の値は記録しません)
```

**Verdict impact**: HIGH finding → if no other CRITICAL exists, verdict = PASS with acknowledgment. If this were an AWS key matching `AKIA[0-9A-Z]{16}`, severity would be CRITICAL → verdict = FAIL.

> **Completion condition verified**: A `password = "secret123"` pattern is detected as a HIGH finding (generic pattern). A service-specific match (e.g., `AKIA...`) would produce a CRITICAL finding.
