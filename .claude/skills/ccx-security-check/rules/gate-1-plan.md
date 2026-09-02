# Gate 1: Design-Time Security Analysis Rules

This file contains the AI execution instructions for **Gate 1 (plan)**. When SKILL.md
routes to Gate 1, follow every step in this document exactly to analyze the target
feature's `design.md` and produce a structured security report.

Reference `rules/report-format.md` for the output format you must produce.

---

## Prerequisites

Before starting the analysis, confirm:
1. You have read `design.md` for the target feature (full content).
2. You have read `rules/report-format.md` so you know the exact report structure.

If `design.md` does not exist, stop immediately and output:
```
エラー: design.md が見つかりません。
先に `/kiro-spec-design {feature-name}` を実行して設計書を生成してください。
```

---

## OWASP Top 10 2025 Alignment

Gate 1 addresses the following OWASP Top 10 2025 categories through its four checks:

| OWASP Category | Gate 1 Check |
|----------------|-------------|
| A01 – Broken Access Control | Check 3 (input validation), Check 4 (attack vectors) |
| A02 – Cryptographic Failures | Check 2 (credential exposure), Check 1 (network comm.) |
| A03 – Injection | Check 4 (SQLi, CMDi, path traversal, XSS) |
| A04 – Insecure Design | All four checks (design-phase gate) |
| A05 – Security Misconfiguration | Check 2 (hardcoded credentials), Check 1 (undeclared comm.) |
| A06 – Vulnerable & Outdated Components | INFO note if dependency versions are mentioned |
| A07 – Identification & Authentication Failures | Check 2 (credential storage design) |
| A08 – Software and Data Integrity Failures | Check 1 (external communication declaration) |
| A09 – Security Logging & Monitoring Failures | Check 3 (output handling design) |
| A10 – Server-Side Request Forgery (SSRF) | Check 1 (external communication), Check 4 (attack vectors) |

---

## Analysis Steps

Execute the four checks below **in order**. For each check, record:
- Whether the check passed, failed, or has warnings
- Any findings (with severity, location, description, and guidance)

---

### Check 1 — Network Communication Declaration (Req 2.1)

**Purpose**: Detect any external network communication described in the design and
determine the "通信あり / 通信なし" verdict that MUST appear in the report header.

**Step-by-step**:

1. Search `design.md` for any of the following signals (case-insensitive):
   - Explicit URLs or hostnames: `http://`, `https://`, `ws://`, `wss://`, domain names
   - API references: words like `API`, `endpoint`, `REST`, `GraphQL`, `gRPC`, `webhook`
   - Network library mentions: `requests`, `axios`, `fetch`, `HttpClient`, `socket`, `WebSocket`, `urllib`, `aiohttp`, `httpx`, `curl`
   - External service names: specific named services (e.g., "OpenAI API", "Slack", "GitHub", "AWS")
   - Infrastructure terms implying outbound communication: `CDN`, `proxy`, `reverse proxy` used as an outbound target

2. Determine network verdict:
   - **"通信あり"**: At least one signal from step 1 was found in `design.md`.
   - **"通信なし"**: No signals found — the design describes a fully local/offline tool.

3. Check for undeclared communication risk:
   - If `design.md` mentions network libraries or HTTP client code patterns in the
     implementation plan **but does not declare what external endpoints are called**,
     this is an undeclared communication risk → raise a **HIGH** finding.
   - If communication is clearly declared with destination and purpose, no finding needed.

**Findings for this check**:

| Condition | Severity | Finding Title |
|-----------|----------|--------------|
| External comm. detected, destination declared | — (no finding) | N/A |
| External comm. implied by library usage but no destination declared | HIGH | 外部通信の宣言不足 |
| No external communication at all | — (no finding, verdict = 通信なし) | N/A |

---

### Check 2 — Credential Exposure Risk (Req 2.2)

**Purpose**: Detect design descriptions that imply credentials (API keys, passwords,
tokens, secrets) will be hardcoded directly into source code or configuration files
tracked in version control.

**Step-by-step**:

1. Search `design.md` for credential-related terms:
   - `api_key`, `API key`, `apiKey`, `API_KEY`
   - `password`, `passwd`, `パスワード`
   - `token`, `secret`, `credential`, `auth`
   - `private key`, `秘密鍵`, `証明書`

2. For each occurrence, examine the surrounding context (the sentence or paragraph):

   **CRITICAL pattern — hardcoding implied**:
   Any of the following contexts indicates the design intends to hardcode the credential:
   - "store in `config.json`", "write to `settings.py`", "put in `constants.js`", or similar
     references to a source-code or plain config file without mentioning exclusion from VCS
   - Direct assignment examples in design text: `` api_key = "sk-..." ``, `` password = "..." ``,
     `` token = "hardcoded_value" ``
   - "ファイルに保存", "コードに書く", "定数として定義" without env/secret-manager qualification
   - Design explicitly says to embed the value in a source file, `.json`, `.yaml`, `.toml`,
     `.ini`, or `.env.example` where the actual secret would be committed

   **Safe pattern — no finding needed**:
   - "store in environment variable", "use `os.getenv()`", "read from `process.env`"
   - "secret manager", "AWS Secrets Manager", "HashiCorp Vault", "Azure Key Vault"
   - "`.env` file (excluded from VCS)", "environment variable", "環境変数", "秘密管理"
   - The credential term appears only in a documentation/explanation context without
     implying the actual secret value will be in code

3. If a CRITICAL pattern is found, create a finding per occurrence.

**Findings for this check**:

| Condition | Severity | Finding Title |
|-----------|----------|--------------|
| Design implies hardcoding credential into source/config file | CRITICAL | 認証情報ハードコードリスク |
| Credential mentioned but safe storage method specified | — (no finding) | N/A |
| Credential storage method not mentioned at all (ambiguous) | LOW | 認証情報の保管方式が未定義 |

---

### Check 3 — Input Validation Coverage (Req 2.3)

**Purpose**: Confirm that the design addresses input validation for user-facing inputs
and output escaping for any output destinations.

**Step-by-step**:

1. Determine if the tool described in `design.md` accepts **user input**:
   - CLI arguments, flags, or interactive prompts
   - Web form inputs, query parameters, request bodies
   - File paths or filenames provided by the user
   - Configuration values supplied at runtime by the user

2. If user input exists, check whether `design.md` mentions:
   - Input validation, sanitization, or schema checking
   - Terms: `validate`, `sanitize`, `schema`, `type check`, `バリデーション`, `サニタイズ`
   - If **no mention** of any validation for user-facing inputs → **HIGH** finding.

3. Determine the tool's **output destinations**:
   - HTML/web output rendered in a browser
   - CLI/terminal output
   - Database writes
   - File writes
   - Log output

4. For each output destination, check whether `design.md` mentions output escaping
   or encoding appropriate to that destination:
   - HTML output → HTML escaping / CSP / template auto-escaping
   - SQL writes → parameterized queries / ORM usage
   - CLI output → no raw injection concern (lower risk), but check for shell quoting
   - Log output → PII scrubbing mentioned?
   - If **no mention** of output escaping for web/database outputs → **MEDIUM** finding.

**Findings for this check**:

| Condition | Severity | Finding Title |
|-----------|----------|--------------|
| User input present but no validation design anywhere | HIGH | 入力検証設計の欠如 |
| Output to web/DB but no escaping/parameterization mentioned | MEDIUM | 出力エスケープ設計の不足 |
| Input validation mentioned but incomplete | MEDIUM | 入力検証設計の不十分 |
| No user input in the tool | — (no finding, note as INFO) | N/A |

---

### Check 4 — Attack Vector Coverage (Req 2.4)

**Purpose**: Verify that the design addresses relevant injection and traversal attack
vectors based on the tool type described in `design.md`.

**Step-by-step**:

1. Identify the tool's **attack surface** from `design.md`:
   - **Has user input?** (CLI args, web forms, API inputs, file paths)
   - **Accesses a database?** (SQL, NoSQL)
   - **Executes system commands?** (subprocess, shell calls, `exec`, `eval`)
   - **Processes file paths from user?** (file open, directory listing, file write)
   - **Renders output in a browser?** (web UI, HTML template, Electron)
   - **Calls external APIs with user-supplied data?**

2. Apply **only the attack vectors relevant** to the detected attack surface:

   | Attack Vector | Applies When | Check |
   |---------------|-------------|-------|
   | SQL Injection (SQLi) | Design mentions database access + user input | Does design mention parameterized queries, ORM, or input sanitization before DB calls? |
   | Command Injection (CMDi) | Design mentions subprocess, shell, `exec`, system calls + user input | Does design restrict or sanitize input used in shell commands? |
   | Path Traversal | Design mentions file I/O using user-supplied paths | Does design validate or normalize file paths? |
   | Cross-Site Scripting (XSS) | Design mentions browser rendering, HTML templates, or web UI | Does design mention HTML escaping, CSP, or template auto-escape? |
   | SSRF | Design mentions outbound HTTP calls using user-supplied URLs | Does design restrict allowed URL patterns or destinations? |
   | Open Redirect | Design mentions URL redirects based on user input | Does design validate redirect targets? |

3. For each **applicable** attack vector:
   - If the design **explicitly addresses** it → no finding.
   - If the design **partially addresses** it → MEDIUM finding.
   - If the design **completely ignores** it and the attack surface clearly exists → MEDIUM finding (HIGH only if the risk is particularly severe given the tool's context).

4. For attack vectors that are **not applicable** (e.g., no database → SQLi is irrelevant):
   - Do **not** create findings. Do not penalize the design for not mentioning them.

**Findings for this check**:

| Condition | Severity | Finding Title |
|-----------|----------|--------------|
| Applicable vector completely unaddressed, high-risk context | HIGH | {Vector}対策が設計に未記載（高リスク） |
| Applicable vector completely unaddressed, moderate-risk context | MEDIUM | {Vector}対策が設計に未記載 |
| Applicable vector partially addressed | MEDIUM | {Vector}対策が不十分 |
| Vector not applicable to this tool | — (no finding) | N/A |

---

## Severity Classification

Apply the following classification to every finding raised in the four checks above.

| Severity | Label | Gate 1 Criteria |
|----------|-------|----------------|
| 1 (highest) | `CRITICAL` | Hardcoded credential pattern described in design (e.g., "store the API key in config.json", direct secret value in code example) |
| 2 | `HIGH` | Undeclared external communication; missing input validation for user-facing inputs |
| 3 | `MEDIUM` | Missing output escaping; attack vector not addressed but risk is low given the tool type; incomplete validation design |
| 4 | `LOW` | Security best practice not mentioned but not a direct vulnerability (e.g., no mention of logging policy, no mention of dependency pinning) |
| 5 (lowest) | `INFO` | Informational observation; no action required |

---

## Verdict Rules (Req 2.5, 2.6)

After completing all four checks, apply these rules to determine the final verdict:

- **FAIL**: One or more CRITICAL **or** HIGH findings were raised across any check.
- **PASS**: All findings are MEDIUM, LOW, or INFO severity only — or no findings at all.

The verdict and the network communication status ("通信あり" / "通信なし") from
Check 1 MUST both appear in the report header (see `rules/report-format.md`).

---

## Output

After completing all four checks:

1. Compose the full report following `rules/report-format.md` exactly.
2. Use the check names below in the Check Summary Table:

   | Row | チェック項目 (Japanese label) |
   |-----|------------------------------|
   | 1 | 外部通信の宣言 |
   | 2 | 認証情報のリスク |
   | 3 | 入力検証設計 |
   | 4 | 攻撃ベクター考慮 |

3. Save the report to `.kiro/specs/{feature-name}/security-report-plan.md`.
4. Display the verdict and next-action message in the conversation.
