# Research Process — ccx-tech-selection

This document defines the methodology for Step 4 (Research Candidates) of the
`ccx-tech-selection` skill. The Research Engine MUST follow every rule in this
file when collecting information about technology candidates.

---

## Query Templates

Use the following search query templates for each candidate. Replace `{name}`
with the exact package or framework name (e.g., `React`, `fastapi`, `pytest`).

| Purpose | Query Template |
|---------|---------------|
| Latest stable version | `{name} latest stable version` |
| License | `{name} license` |
| Recent releases | `{name} recent releases` |
| Known issues | `{name} known issues` |

**Usage rules:**
- Run all four queries per candidate unless an earlier query already yields the
  needed data (e.g., the official docs page already shows version + license on
  the same page — one fetch is sufficient).
- When a query returns no useful result, try the alternate form
  `{name} site:github.com` or `{name} site:npmjs.com` before recording
  "情報取得不可" (unavailable).

---

## Source Priority

Evaluate and prefer information sources in the following order (highest priority
first):

1. **Official documentation** — the project's own website or docs site
   (e.g., `docs.python.org`, `react.dev`, `fastapi.tiangolo.com`)
2. **Package repository** — the canonical registry for the ecosystem
   (e.g., PyPI for Python, npmjs.com for Node.js, crates.io for Rust,
   Maven Central for Java)
3. **GitHub repository** — the project's primary source repository
   (`github.com/{org}/{repo}`)
4. **Community sources** — Stack Overflow, Reddit, official blogs, or
   reputable technical blogs (used only when no higher-priority source is
   available)

**Recording rule:** Always record the actual URL used when a URL is obtainable.
When only the source name is available (e.g., the tool could not retrieve a
URL), record the source name (e.g., `"PyPI"`, `"npmjs.com"`) as the
`sourceReference` field value.

---

## 4-Item Collection Rule

For every candidate that has `status: "needs-comparison"`, collect exactly the
following four data items:

| # | Item | Expected Value Format | Notes |
|---|------|-----------------------|-------|
| 1 | **Latest stable version** | Version string (e.g., `3.11.4`, `18.2.0`) | Use the latest non-pre-release tag. If only pre-release versions exist, record the latest and note it is pre-release. |
| 2 | **Release activity in the last 6 months** | `"アクティブ"` or `"非アクティブ"` | Mark as `"アクティブ"` if at least one release (including patch/minor) was published within the 6 months before the current date. Otherwise mark `"非アクティブ"`. |
| 3 | **License type** | SPDX identifier string (e.g., `MIT`, `Apache-2.0`, `GPL-3.0`) | Use the SPDX short identifier when possible. If the license is non-standard, record its common name. |
| 4 | **Known major issues** | Plain-text description, or `"なし"` if none found | Focus on issues that could affect adoption: security advisories, breaking-change freeze, abandoned maintenance, incompatibility with common dependencies. |

All four items MUST be recorded for each candidate even if the value is
`"情報取得不可"` (unavailable). Never skip an item silently.

---

## Parallel Dispatch Policy

The Research Engine determines how to dispatch Agent subagents based on the
count of `needs-comparison` categories identified in Step 3. **`confirmed`
categories are NOT counted in this policy.**

| Condition | Dispatch Strategy |
|-----------|------------------|
| `needs-comparison` category count ≥ 2 | Dispatch one Agent subagent **per category** in parallel. Each subagent researches all candidates within its assigned category. |
| `needs-comparison` category count = 1 AND candidate count ≥ 2 | Dispatch one Agent subagent **per candidate** within that single category in parallel. |
| `needs-comparison` category count = 1 AND candidate count = 1 | No parallel dispatch. Research the single candidate inline (no subagent overhead). |
| `needs-comparison` category count = 0 | No research dispatch. The skill proceeds in confirmation-only mode (Step 3 special branch). |

**Important constraints:**
- `confirmed` categories are fully excluded from the above count. For example,
  if 2 categories are `confirmed` and 1 is `needs-comparison` with 3 candidates,
  apply the "count = 1 AND candidate count ≥ 2" rule (candidate-level parallel).
- Each subagent receives: the candidate name(s) to research, the query templates
  from the Query Templates section above, and the source priority order.
- Subagents return results conforming to the `CandidateResearchResult` interface
  defined in design.md.

---

## Failure Handling

When a data item cannot be retrieved for a candidate, apply the following rules:

1. **Record "情報取得不可"** in the corresponding field of `CandidateResearchResult`
   (e.g., `latestVersion: "情報取得不可"`).
2. **Continue researching** all remaining items for the same candidate and all
   other candidates. Never abort the entire research phase because one item or
   one candidate is unavailable.
3. **Retry once** with an alternate query or source before recording unavailable
   (see the alternate query forms in the Query Templates section).
4. **Record the failure source** in `sourceReference` as `"情報取得不可"` only
   when no source could be reached at all. If a source was reached but the
   specific data was not found, record that source's URL or name.
5. **Do not invent or estimate values.** If information is unavailable, it MUST
   be recorded as `"情報取得不可"`, never as a guess.

**Propagation to the Report Generator:**
- The Report Generator (Step 5) will render `"情報取得不可"` literally in the
  comparison table cells and the Adoption Decision section. The presence of
  unavailable data does not block report generation.
