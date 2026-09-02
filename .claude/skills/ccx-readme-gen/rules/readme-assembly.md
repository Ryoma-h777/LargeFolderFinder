# README Assembly Rules

Defines how ccx-readme-gen assembles the final `README.{ext}` from collected content and duplication-check decisions, in the format confirmed by `format-selection.md`. Referenced by SKILL.md at runtime (Read tool).

Output structure follows `.kiro/steering/distribution.md` (README Standards). Migrated and extended from `ccx-build-package/rules/readme-gen.md` Steps 3-5.

The confirmed `FormatSelectionResult` (from `format-selection.md`: `extension`, `imagesNeeded`, optional `shippingNote`) selects the emit format in Step 5. The section content and ordering below are format-independent; only the final rendering (Step 5) and the license-reference snippet (Step 3) differ per format.

## Step 1: Determine Target Audience

Same as the original `readme-gen.md` logic: look for an explicit target-user statement in design.md/requirements.md ("対象ユーザー: 非エンジニア" etc.). If not stated, ask the developer. This affects tone (Step 4).

## Step 2: Assemble Sections

Build these sections (rendered per the confirmed format in Step 5), applying duplication-check results where relevant:

1. **Tool name and version** (`<h1>`) — from `CollectedContent.appName` / `.version`.
2. **Overview** — from `CollectedContent.overview`.
3. **Target users** — from Step 1.
4. **System requirements** — if `DuplicationResult.systemRequirements.status == "duplicate-confirmed"`, use `guidanceText`; otherwise use `CollectedContent.systemRequirements`.
5. **Installation instructions** — **omit this section entirely** if `content-collection.md` determined installation is not required. Otherwise, if `DuplicationResult.installation.status == "duplicate-confirmed"`, use `guidanceText`; otherwise use full numbered steps with screenshot placeholders (HTML: `<!-- スクリーンショット: {手順の説明} -->`; Markdown: `<!-- スクリーンショット: {手順の説明} -->` on its own line).
6. **Usage guide** — if `DuplicationResult.usage.status == "duplicate-confirmed"`, use `guidanceText`; otherwise use `CollectedContent.usage`.
7. **Uninstallation** (when applicable) — only include if the tool requires installation and an uninstall procedure is described in design.md; otherwise omit.
8. **Developer information** — from `CollectedContent.developer` (or the placeholder comment).
9. **License** — see Step 3.
10. **Other important information** — from `CollectedContent.otherImportantInfo`, only if non-empty.

## Step 3: License Section

- State the tool's own license (name/type) if known from steering or spec docs; otherwise note "ライセンス情報は開発者が追記してください".
- **Third-party license reference**: use Glob to check for the project-root `THIRD_PARTY_LICENSES.*` (README and THIRD_PARTY_LICENSES ship together at the project root, and are copied side by side into `Release/` by `ccx-build-package`). Reference it by its actual filename/extension, rendered per the confirmed README format:
  - Found — HTML: `<p>第三者ライセンスについては <a href="THIRD_PARTY_LICENSES.{ext}">THIRD_PARTY_LICENSES.{ext}</a> をご確認ください。</p>`
  - Found — Markdown: `第三者ライセンスについては [THIRD_PARTY_LICENSES.{ext}](THIRD_PARTY_LICENSES.{ext}) をご確認ください。`
  - Found — Plain text: `第三者ライセンスについては、同梱の THIRD_PARTY_LICENSES.{ext} をご確認ください。`
  - Not found — HTML: `<p>第三者ライセンス情報は現在未生成です。<code>/ccx-thirdlicense-gen</code> の実行後に確認できるようになります。</p>`
  - Not found — Markdown: `第三者ライセンス情報は現在未生成です。\`/ccx-thirdlicense-gen\` の実行後に確認できるようになります。`
  - Not found — Plain text: `第三者ライセンス情報は現在未生成です。/ccx-thirdlicense-gen の実行後に確認できるようになります。`

## Step 4: Apply Audience-Appropriate Tone

Same as original logic:
- **Non-engineer**: plain polite Japanese, step-by-step with screenshot placeholders, avoid/annotate jargon, add short troubleshooting notes.
- **Engineer**: technical terminology acceptable, more concise steps.

## Step 5: Emit in the Confirmed Format

Render the assembled sections in the confirmed `extension` and write to the project-root `README.{ext}`:

- **`.html`** (browser channel): a single self-contained file — inline minimal CSS, no external asset dependencies, and **images embedded as data URIs** (the file must render standalone when double-clicked). Semantic headings (`<h1>` for tool name, `<h2>` per section).
- **`.md`** (renderer channel): GitHub-flavored Markdown. `#` for the tool name, `##` per section, GFM tables where the HTML version used tables, fenced code blocks for commands. Images referenced by **relative path**; when `imagesNeeded`, surface the `shippingNote` from format-selection in the final report so the developer ships the image files next to `README.md`. Screenshot placeholders as HTML comments on their own line.
- **`.txt`** (plaintext channel, no images): plain prose with no markup symbols. Tool name and version on the first lines, section titles as short lines followed by a rule of dashes, numbered steps as `1.` `2.` …, no tables (use labeled lines instead: `対象OS: Windows 11`). Never include images or `![...]` syntax.
- **Other developer-specified extension**: follow the closest structural analog (html for rich/linked targets, md for registry-rendered targets, txt for raw-text targets), preserving all mandatory sections.

Do not emit more than one README file — only the confirmed extension is written.

## Validation (self-check before returning)

Confirm the mandatory distribution.md items are present: tool name+version, overview, target users, license summary. Sections legitimately omitted per duplication-check or installation-not-required are not violations — only flag genuinely missing mandatory content.
