# Gate 3: Pre-Distribution Final Review Rules

This file defines the analysis rules for Gate 3 (review). When SKILL.md routes to Gate 3, read this file and execute every check in order.

---

## Prerequisites Check (Req 4.7)

Before running any checks, verify whether Gate 2 has been executed:

1. Check if `.kiro/specs/{feature}/security-report-test.md` exists using the Glob or Read tool.
2. **If the file does NOT exist**, display the following warning and continue execution (do NOT stop):

   ```
   ⚠️ Gate 2 (test) has not been run. Proceeding with review but results may be incomplete.
      Recommendation: Run `/ccx-security-check {feature} test` first for complete coverage.
   ```

3. Record `gate2_available: true/false` for use in Check 4 (Network Final Declaration).

---

## Check 1 — Undisclosed Data Collection (Req 4.1)

**Objective**: Detect any code that silently collects or transmits data without being declared in design.md.

### Step 1.1 — Grep for telemetry/analytics patterns

Run Grep across all source files identified from design.md's File Structure Plan for the following patterns (case-insensitive):

| Pattern | Description |
|---------|-------------|
| `telemetry` | Telemetry collection code |
| `analytics` | Analytics tracking code |
| `\btrack\b` | Tracking function calls |
| `\bcollect\b` | Data collection calls |
| `report.?back` | "Report back" style data sending |
| `phone.?home` | Phone-home style beaconing |
| `beacon` | Beacon requests |
| `heartbeat` | Heartbeat/ping data |
| `usage.?data` | Usage data collection |
| `event.?track` | Event tracking |

Example Grep call:
```
pattern: "(?i)(telemetry|analytics|\btrack\b|\bcollect\b|report.?back|phone.?home|beacon|heartbeat|usage.?data|event.?track)"
```

### Step 1.2 — Cross-reference with design.md declarations

For each hit found in Step 1.1:
- Read design.md to check if data collection/transmission to that destination is explicitly declared.
- If the destination URL/endpoint is NOT mentioned in design.md → flag as **CRITICAL**.
- If the pattern exists but sends data only to declared endpoints → flag as **HIGH** (undisclosed collection practice, even if endpoint is known).
- If the pattern is inside a comment or test file → flag as **INFO**.

### Step 1.3 — Severity assignment

| Condition | Severity |
|-----------|----------|
| Data sent to undeclared URL/endpoint | CRITICAL |
| Telemetry/analytics code exists but endpoint is declared | HIGH |
| Pattern in comments or test files only | INFO |
| No patterns found | PASS |

---

## Check 2 — PII Handling (Req 4.2)

**Objective**: Confirm that personally identifiable information (PII) is not leaked via logs, storage, or transmission.

### Step 2.1 — PII in log output

Grep for patterns where PII fields appear in log/print statements. Apply the following patterns across all source files:

| Pattern | PII Type | Severity |
|---------|----------|----------|
| `console\.log.*user\.` | User object in log | MEDIUM (WARNING) |
| `console\.log.*\.email` | Email in log | MEDIUM (WARNING) |
| `console\.log.*email` | Email variable in log | MEDIUM (WARNING) |
| `console\.log.*password` | Password in log | HIGH |
| `console\.log.*token` | Token in log | HIGH |
| `console\.log.*secret` | Secret in log | HIGH |
| `print.*email` | Email printed (Python) | MEDIUM (WARNING) |
| `print.*password` | Password printed (Python) | HIGH |
| `log\..*password` | Password in logger | HIGH |
| `logger\..*token` | Token in logger | HIGH |
| `logger\..*password` | Password in logger | HIGH |
| `console\.log.*address` | Address in log | MEDIUM (WARNING) |
| `console\.log.*phone` | Phone number in log | MEDIUM (WARNING) |
| `console\.log.*ip` | IP address in log | MEDIUM (WARNING) |
| `console\.log.*name` | Name in log | LOW |
| `log\.info.*email` | Email in info log | MEDIUM (WARNING) |
| `log\.debug.*email` | Email in debug log | MEDIUM (WARNING) |
| `logging\..*email` | Email in Python logging | MEDIUM (WARNING) |

**Explicit example** — `console.log(user.email)` must be detected and flagged:
- This matches the pattern `console\.log.*user\.` AND `console\.log.*\.email`
- Severity: **MEDIUM (WARNING)** — PII (email address) in log output
- Guidance: Remove `user.email` from the log call, or use a masked version such as `user.email.replace(/(.{2}).+(@.+)/, '$1***$2')`

### Step 2.2 — PII in storage

Grep for patterns where PII is written to files or databases without masking:

| Pattern | Concern |
|---------|---------|
| `fs\.writeFile.*email` | Email written to file (JS/TS) |
| `open\(.*\).*write.*email` | Email written to file (Python) |
| `INSERT.*email` | Email in SQL insert |
| `localStorage.*email` | Email in browser storage |

Flag as **HIGH** if PII is persisted without explicit encryption or masking.

### Step 2.3 — PII in transmission

Cross-reference with Check 1 findings. If network calls (detected in Check 1 or Gate 2) include PII fields in the payload or URL:

| Condition | Severity |
|-----------|----------|
| Password/token sent over HTTP (not HTTPS) | CRITICAL |
| Email/name/address in request body to undeclared endpoint | HIGH |
| Email/name in request body to declared endpoint without docs justification | MEDIUM |

### Step 2.4 — PII severity summary

| Finding Type | Severity |
|-------------|----------|
| Password/token/secret in any log | HIGH |
| Email in log output (e.g., `console.log(user.email)`) | MEDIUM (WARNING) |
| Name/address/phone/IP in log output | MEDIUM (WARNING) |
| PII stored without encryption | HIGH |
| PII sent to undeclared endpoint | HIGH |
| PII sent over non-HTTPS | CRITICAL |

---

## Check 3 — License and CVE Final Confirmation (Req 4.3)

**Objective**: Confirm all third-party library licenses are distribution-compatible and no known CVEs remain unresolved.

### Step 3.1 — Dependency detection

If Gate 2 report (`security-report-test.md`) is available, extract the dependency list from it. Otherwise, re-run dependency detection:

Use Glob to find dependency files:
- `package.json`
- `requirements.txt`
- `pyproject.toml`
- `*.csproj`
- `packages.config`
- `go.mod`
- `Cargo.toml`

Use Read to extract package names and versions from each found file.

### Step 3.2 — License classification

For each dependency, determine license type. Use WebSearch with query: `"{package_name} license type"`

Classify and act as follows:

| License | Classification | Action |
|---------|---------------|--------|
| MIT | ✅ OK | No action required |
| Apache 2.0 | ✅ OK | No action required |
| BSD 2-Clause / BSD 3-Clause | ✅ OK | No action required |
| ISC | ✅ OK | No action required |
| Unlicense / CC0 | ✅ OK | No action required |
| LGPL (any version) | ⚠️ NOTE | Flag for review — dynamic linking may be acceptable, static linking requires legal review |
| GPL v2 | 🚩 FLAG | Flag for legal review — copyleft may require source disclosure of the entire distributed tool |
| GPL v3 | 🚩 FLAG | Flag for legal review — copyleft applies to the entire distributed tool |
| AGPL | 🚩 FLAG (HIGH) | Copyleft extends to network use — strongly discouraged for distributed tools |
| Proprietary / Unknown | ⚠️ HIGH | Cannot verify distribution rights — must resolve before GO |

**GPL/LGPL flag criteria**:
- Any GPL-licensed (v2, v3) dependency → severity **HIGH**, require explicit legal review confirmation before GO.
- LGPL dependency used via dynamic linking → severity **MEDIUM** (note only).
- LGPL dependency statically linked or source-modified → severity **HIGH**.
- Unknown license → severity **HIGH** (cannot confirm distribution rights).

### Step 3.3 — CVE final confirmation

If Gate 2 ran the ecosystem-standard tool scan (`gate-2-test.md` Section 3-C: `dotnet list package --vulnerable`, `npm audit`, `pip-audit`), treat its findings as authoritative for C# / JavaScript / Python and re-run the same command via `Bash` only to confirm no new CVEs were published since Gate 2. For C++ dependencies (no ecosystem tool available) and for any package Gate 2 could not scan with a tool, use WebSearch — this remains a **best-effort** check, not a systematic scan:

Query format: `"{package_name} {version} CVE vulnerability site:nvd.nist.gov OR site:github.com/advisories OR site:security.snyk.io"`

| Condition | Action |
|-----------|--------|
| Known unpatched CRITICAL CVE | Severity: CRITICAL — NO-GO unless mitigated |
| Known unpatched HIGH CVE | Severity: HIGH — NO-GO unless mitigated |
| CVE exists but patched version available | Severity: HIGH — guidance: upgrade to patched version |
| No CVE found | Severity: INFO / PASS |
| WebSearch fails | Record "脆弱性情報の取得に失敗。手動確認を推奨" as WARNING, continue |

---

## Check 4 — Network Final Declaration (Req 4.6)

**Objective**: Derive a definitive "通信あり / 通信なし" verdict for the Gate 3 report.

### Step 4.1 — Derive from Gate 2 report (preferred)

If `security-report-test.md` exists:
- Read the file and locate the "ネットワーク通信" field or the network communication section.
- Extract the declared verdict: `通信あり` or `通信なし`.
- Use this value directly in the Gate 3 report.

### Step 4.2 — Fresh scan (fallback)

If Gate 2 report is not available, perform a fresh network scan using the patterns defined in `gate-2-test.md`:

Grep source files for network call patterns:

| Language | Pattern |
|---------|---------|
| Python | `import\s+(requests\|urllib\|aiohttp\|httpx)`, `requests\.(get\|post\|put\|delete)`, `socket\.connect` |
| JS/TS | `fetch\s*\(`, `axios\.(get\|post\|put\|delete)`, `new\s+XMLHttpRequest`, `new\s+WebSocket` |
| C# | `new\s+HttpClient`, `new\s+WebClient`, `HttpWebRequest\.Create`, `new\s+TcpClient` |
| C++ | `#include\s+<curl/curl\.h>`, `curl_easy_init\s*\(`, `boost::asio`, `WSAStartup\s*\(` |

- If any match is found → `通信あり`
- If no match is found → `通信なし`

### Step 4.3 — Record in report

The Gate 3 report MUST include the line:
```
**ネットワーク通信**: 通信あり / 通信なし
```
(fill in the derived value)

---

## GO / NO-GO Verdict (Req 4.4, 4.5)

After completing all checks (1–4), tally findings by severity.

### Verdict criteria

| Condition | Verdict |
|-----------|---------|
| Zero CRITICAL findings AND zero HIGH findings | **✅ GO** |
| One or more CRITICAL findings | **❌ NO-GO** |
| One or more HIGH findings | **❌ NO-GO** |
| Only MEDIUM / LOW / INFO findings | **✅ GO** (with warnings noted) |

### GO output

When verdict is GO, display. `ccx-build-package` is a separate, optional skill — check whether `.claude/skills/ccx-build-package/SKILL.md` exists (Glob) before including the specific command:

```
✅ GO — セキュリティチェック完了。配布パッケージの作成を進めてください。
次のステップ: /ccx-build-package   ← ccx-build-package が存在する場合のみ表示
```

Also include in the report:
- Total findings count by severity
- Confirmation that all checks passed at CRITICAL/HIGH level
- Any MEDIUM/LOW/INFO items as advisory notes

### NO-GO output

When verdict is NO-GO, display:

```
❌ NO-GO — 配布パッケージの作成を停止してください。
CRITICAL {N}件 / HIGH {N}件 を修正してから再実行してください。
再実行コマンド: /ccx-security-check {feature} review
```

Also include in the report:
- Explicit list of all CRITICAL and HIGH findings with file locations and remediation guidance
- Count summary: `CRITICAL: N件, HIGH: N件`

---

## Report Generation

After completing all checks, generate the Gate 3 report following `rules/report-format.md`.

### Check summary table (required)

| チェック項目 | 結果 |
|------------|------|
| 未申告データ収集 (Check 1) | ✅ / ❌ / ⚠️ |
| PII取り扱い (Check 2) | ✅ / ❌ / ⚠️ |
| ライセンス/CVE最終確認 (Check 3) | ✅ / ❌ / ⚠️ |
| ネットワーク通信最終判定 (Check 4) | 通信あり / 通信なし |
| Gate 2 実行状態 | 実行済み / ⚠️ 未実行 |

### Required report fields

- Gate name: `review`
- Feature name: `{feature}`
- Execution timestamp: ISO 8601
- Network declaration: `通信あり` or `通信なし`
- Final verdict: `GO` or `NO-GO`
- Gate 2 status: executed or not
- All findings with: severity, location (file path), description, remediation guidance

### Save path

```
.kiro/specs/{feature}/security-report-review.md
```

---

## PII Detection Quick Reference

The following patterns MUST trigger a WARNING (MEDIUM severity) finding titled "PIIログ出力":

```
console.log(user.email)          → WARNING: メールアドレスをログに出力しています
console.log(user.name)           → WARNING: 氏名をログに出力しています
console.log(user.address)        → WARNING: 住所をログに出力しています
console.log(user.phone)          → WARNING: 電話番号をログに出力しています
console.log(req.ip)              → WARNING: IPアドレスをログに出力しています
print(user['email'])             → WARNING: メールアドレスをログに出力しています
log.info(f"email: {user.email}") → WARNING: メールアドレスをログに出力しています
```

Guidance for all PII log findings:
> ログからPIIを削除するか、マスク処理（例: `user.email.replace(/(.{2}).+(@.+)/, '$1***$2')`）を適用してください。本番環境のログにPIIを含めないことを原則とします。

The following patterns MUST trigger a HIGH severity finding:

```
console.log(user.password)       → HIGH: パスワードをログに出力しています
console.log(apiKey)              → HIGH: APIキーをログに出力しています
console.log(token)               → HIGH: 認証トークンをログに出力しています
log.debug(secret)                → HIGH: シークレット値をログに出力しています
```
