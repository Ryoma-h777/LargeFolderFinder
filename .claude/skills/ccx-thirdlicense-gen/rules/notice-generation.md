# Notice Generation Rules

Defines how ccx-thirdlicense-gen renders `THIRD_PARTY_LICENSES.{ext}` in the confirmed format, and self-checks the result for compliance. Referenced by SKILL.md at runtime (Read tool).

## Step 1: Select Template by Confirmed Extension

Use the `PackageLicenseInfo[]` from license-detection and the confirmed `extension` from format-selection.

### `.txt` template
```
Third-Party Licenses
=====================

{package-name} {version}
-------------------------
License: {license-type}
Copyright: {copyright-notice}

License Text:
{verbatim-license-text}


(repeat per package, separated by a blank line and a rule of dashes)
```

### `.html` template
```html
<!doctype html>
<html lang="ja">
<head><meta charset="utf-8"><title>Third-Party Licenses</title></head>
<body>
<h1>Third-Party Licenses</h1>
<section>
  <h2>{package-name} {version}</h2>
  <p>License: {license-type}</p>
  <p>Copyright: {copyright-notice}</p>
  <pre>{verbatim-license-text}</pre>
</section>
<!-- repeat <section> per package -->
</body>
</html>
```
- Keep this self-contained (no external CSS/JS) so it can be shipped standalone or linked from the app.

### `.md` template
```markdown
# Third-Party Licenses

## {package-name} {version}
- License: {license-type}
- Copyright: {copyright-notice}
- License Text:
  ```
  {verbatim-license-text}
  ```

<!-- repeat ## section per package -->
```

### Other developer-specified extension
Follow the closest structural analog among the three above (txt for plain rendering targets, html for linked/web targets, md for registry-rendered targets), preserving the same required fields.

## Step 2: Populate Entries

For each package in `PackageLicenseInfo[]`:
- Always include the entry, even if `license = "UNKNOWN"` — render `License: 未確認（手動確認が必要）`.
- When `attributionRequired = true`, include the **verbatim** `licenseText`. Do not paraphrase, summarize, truncate, or reformat the license text itself (only the surrounding document structure — headings, spacing — may follow the template).
- When a `concern` was recorded (from license-detection Step 5), append it directly under that package's entry as a visible note (do not omit it from the notice — the concern belongs in the developer-facing report, not hidden).
- If the package list is empty, generate a minimal file stating: `このツールにサードパーティ依存パッケージはありません。`

## Step 3: Self-Check Before Returning

Before returning the generated content to the orchestrator, verify:

1. **Coverage**: every package with `attributionRequired = true` has a non-empty `licenseText` in the output. If any are missing, do not silently omit them — mark them `[要確認: ライセンス原文が取得できませんでした]` and continue (do not fabricate text).
2. **Non-alteration**: the `licenseText` blocks are copied verbatim from the source (Step 3 of license-detection) — confirm no summarization or rewording was introduced when assembling the template.
3. **Scope**: only packages with `attributionRequired = true`, `UNKNOWN`, or a recorded `concern` are mandatory entries; packages confirmed to have no attribution obligation may be omitted from verbatim-text sections but should still be listed briefly (name, version, license) for completeness.

If the self-check finds a violation (missing verbatim text where required, or an altered license text), regenerate the affected entry before returning — do not return a document with a known violation.

## Output of This Step

Return the completed `THIRD_PARTY_LICENSES.{ext}` content and the self-check result (pass, or list of entries that required regeneration) to the orchestrator for writing to disk and inclusion in the final report.
