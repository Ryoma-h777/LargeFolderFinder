# Format Selection Rules

Defines how ccx-thirdlicense-gen infers the distribution shape of the tool and proposes an output file extension for `THIRD_PARTY_LICENSES`. Referenced by SKILL.md at runtime (Read tool). This is a proposal step — the format is never finalized without the developer's explicit confirmation.

## Step 1: Gather Signals

This skill takes no feature argument. To read design signals, auto-resolve a source spec:
Glob `.kiro/specs/*/spec.json`; if exactly one spec exists, use its directory; if several
exist, use the primary spec from `roadmap.md` when indicated, otherwise ask the developer
which spec's `design.md` best describes the distribution shape (or skip to Step 4 if none
applies). If no spec exists at all, proceed to Step 4.

Read the resolved `.kiro/specs/{spec}/design.md` if it exists (Overview, Technology Stack sections). Look for signals of distribution shape:

| Signal | Points toward |
|--------|---------------|
| "Web サービス", "API", "サーバー", "ブラウザ", frontend/backend layered Technology Stack with a server component | Web service |
| "デスクトップアプリ", "CLI", "実行ファイル", "インストーラー", single-binary Technology Stack | Standalone application |
| No design.md, or Technology Stack does not clearly indicate either | Unclear — escalate to Step 4 |

If `design.md` does not exist, or the signals are ambiguous, do not guess — proceed to Step 4 (ask the developer directly).

## Step 2: Propose Format for Clear Cases

### Standalone application
- Suggested extension: `.txt` (plain text)
- Reason: `スタンドアロンアプリケーションでは、実行ファイルと同じディレクトリに配置される単純なテキストファイルが最もアクセスしやすい`

### Web service
- Suggested extension: `.html`
- Reason: `Web サービスでは、サービス画面（フッターや設定画面等）からリンクで到達できる HTML 形式が自然にアクセスできる`

## Step 3: Propose Format for Special Cases

When the tool shape is neither a clean standalone app nor a web service (e.g. a library/SDK distributed to other developers, a CLI tool distributed via a package registry that already renders Markdown, a browser extension), propose the most fitting format with an explicit rationale. Common fits:

- Package/library distributed via a registry that renders Markdown (npm, PyPI) → `.md`, reason: `パッケージレジストリの UI で Markdown がそのまま読みやすく表示されるため`
- Any other shape not covered above → propose the extension you judge most appropriate, and always state the reasoning so the developer can override it

## Step 4: Ambiguous or Missing Signals

If Step 1 could not determine a clear shape, ask the developer directly instead of guessing:

```
配布形態が判別できませんでした。THIRD_PARTY_LICENSES の出力形式について教えてください。
- スタンドアロンアプリケーション（推奨: .txt）
- Web サービス（推奨: .html）
- その他（配布形態を教えてください。適切な形式を提案します）
```

## Step 5: Developer Confirmation (mandatory)

Regardless of which path above was taken, always present the proposal and its reasoning to the developer and wait for confirmation before finalizing:

```
📄 THIRD_PARTY_LICENSES の出力形式を提案します: {extension}
   理由: {reason}
   この形式で生成してよいですか？変更する場合は希望の形式を教えてください。
```

- Never finalize a format without this confirmation step, even when the signal is unambiguous.
- If the developer requests a different extension, use it without further debate — the developer's judgment on this UX decision is authoritative.

## Output of This Step

Return the confirmed `extension` (e.g. `txt`, `html`, `md`, or another developer-specified value) to the orchestrator for use by the Notice Generator.
