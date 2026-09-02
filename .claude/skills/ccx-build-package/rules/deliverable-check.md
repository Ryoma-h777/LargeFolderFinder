# Deliverable Check Rules

Defines how ccx-build-package confirms that `THIRD_PARTY_LICENSES.*` and `README.*` — generated upstream by the independent skills `ccx-thirdlicense-gen` and `ccx-readme-gen` — already exist before proceeding to packaging. Referenced by SKILL.md at runtime (Read tool).

Both deliverables use a **variable extension** chosen by the upstream skill based on distribution shape; this skill must not hardcode a specific extension for either.

Both deliverables are **project-wide** and live at the **project root** (the upstream skills
target the whole project, not a single feature).

This rule file intentionally contains **no generation logic**. It only checks for file existence and reports the exact upstream command to run when a deliverable is missing (tech.md "Skill independence" principle — this skill does not duplicate or depend on the internals of `ccx-thirdlicense-gen`/`ccx-readme-gen`).

## Step 1: Check THIRD_PARTY_LICENSES

Use the Glob tool with the pattern `THIRD_PARTY_LICENSES.*` at the project root (the extension varies — `ccx-thirdlicense-gen` selects txt/html/md/other based on distribution shape; do not hardcode an extension).

- If **found**: record the exact matched path (including its actual extension) for later use by the Package Assembler.
- If **not found**: display this error and **stop immediately**:
  ```
  エラー: プロジェクトルートに THIRD_PARTY_LICENSES.* が見つかりません。
  先に `/ccx-thirdlicense-gen` を実行してサードパーティライセンス表記を生成してください。
  ```

## Step 2: Check README

Use the Glob tool with the pattern `README.*` at the project root (the extension varies — `ccx-readme-gen` selects md/html/other based on distribution shape; do not hardcode an extension). If multiple `README.*` files exist, prefer the most recently generated one and note the ambiguity.

- If **found**: record the exact matched path (including its actual extension) for later use by the Package Assembler.
- If **not found**: display this error and **stop immediately**:
  ```
  エラー: プロジェクトルートに README.* が見つかりません。
  先に `/ccx-readme-gen` を実行して README を生成してください。
  ```

## Step 3: Proceed

If both deliverables were found, proceed to the next pipeline step (Changelog Generator) with the two confirmed paths retained for the Package Assembler.

## Output of This Step

Return to the orchestrator:
```
DeliverableCheckResult:
  status: "proceed" | "stop"
  thirdPartyLicensesPath?: string   // e.g. "THIRD_PARTY_LICENSES.md"
  readmePath?: string               // e.g. "README.md" (extension varies)
  missingItem?: "third-party-licenses" | "readme"
```
