# README Format Selection Rules

Defines how ccx-readme-gen decides the output **extension** for `README`. Referenced by SKILL.md at runtime (Read tool). This is a proposal step — the format is never finalized without the developer's explicit confirmation.

This mirrors the distribution-shape approach used by `ccx-thirdlicense-gen/rules/format-selection.md` (each skill restates the shared idea for self-containment per tech.md "Skill Independence"; there is no cross-skill dependency).

## Decision Model: Two Axes

- **Axis 1 (primary) — main viewing channel**: where and how the recipient actually reads the README.
- **Axis 2 (constraint) — images / rich presentation**: whether the README needs screenshots or images. This axis never picks the extension by itself; it constrains what the channel allows (its main effect is excluding `.txt` and shaping how images are shipped).

## Step 1: Identify the Main Viewing Channel

From the resolved spec's `design.md` (Overview, Technology Stack, 対象ユーザー) and `requirements.md`, classify the channel:

| Channel | Meaning / signals |
|---------|-------------------|
| `renderer` | Read where Markdown is **rendered**: GitHub / package registries (npm, PyPI, NuGet) / docs sites that render md / editor previews (VSCode). Typical when the product itself is distributed as source or text files to engineers. |
| `browser` | Read in a **browser**: linked or embedded from a web service UI (footer/settings), **or the recipient opens the distributed file directly (double-click)** — the common case for non-engineer zip distribution, since every machine renders `.html` in a browser while raw `.md` opens as plain text. |
| `plaintext` | Read as **raw plain text**: Notepad-level environments, non-engineer local reading with no rendering available or expected. |
| `known-other` | The channel is known but fits none of the above. |
| `unknown` | The channel cannot be determined from the spec documents. |

If **multiple channels** apply (e.g. published on GitHub *and* bundled in a zip for non-engineers), do not guess — ask the developer which channel is the most important one and classify by that answer.

## Step 2: Determine Whether Images Are Needed

Judge from the spec documents and `.kiro/steering/distribution.md` README Standards whether this README needs screenshots/images (e.g. non-engineer audience with step-by-step installation screenshots). Record `imagesNeeded: true | false`.

## Step 3: Propose the Extension (channel × images)

| Channel | imagesNeeded | Proposal |
|---------|--------------|----------|
| `renderer` | no | `.md` |
| `renderer` | yes | `.md` — **and tell the developer the image files must ship alongside** (md references images by relative path, so the README is no longer a single file; the images must be added to the distribution package next to `README.md`) |
| `browser` | any | `.html` — self-contained single file; when images are used, embed them as data URIs (no external asset files) |
| `plaintext` | no | `.txt` |
| `plaintext` | yes | **Conflict** — a plain-text channel cannot display images. Present both resolutions and let the developer choose: (a) switch to `.html`(opens rendered in a browser anywhere), or (b) keep `.txt` and drop the images |
| `known-other` | any | Propose the best-fitting extension with explicit reasoning so the developer can override it |
| `unknown` | — | Ask the developer directly (Step 4 wording) |

Rationale for the mapping:
- `.md` — rendered in place by the channel itself; engineers also tolerate raw md.
- `.html` — renders on any machine via the default browser; the only self-contained option when images are needed outside a renderer channel.
- `.txt` — only correct when the reader will see raw text (Notepad-level); raw `.md` symbols (`#`, `|`) confuse non-engineers there, plain prose does not. Never propose `.txt` when images are needed (see conflict row).

## Step 4: Ambiguous or Missing Signals

If Step 1 could not determine the channel, ask the developer instead of guessing:

```
README の主な閲覧導線が判別できませんでした。どの形で読まれる想定か教えてください。
- GitHub・レジストリ等、Markdown が整形表示される場所で読む（推奨: .md）
- ブラウザで読む（サービスからリンク／配布ファイルをダブルクリックで開く）（推奨: .html）
- メモ帳等でプレーンテキストのまま読む（推奨: .txt ※画像は使えません）
- その他（導線を教えてください。適切な形式を提案します）
```

## Step 5: Developer Confirmation (mandatory)

Regardless of which path above was taken — including the "clear" rows of the Step 3 table — always present the proposal and its reasoning and wait for confirmation before finalizing:

```
📄 README の出力形式を提案します: {extension}
   理由: {channel と画像要否に基づく理由。md+画像なら画像同梱の注意、
        plaintext+画像なら矛盾の説明と二択を含める}
   この形式で生成してよいですか？変更する場合は希望の形式を教えてください。
```

- Never finalize a format without this confirmation step, even when the signal is unambiguous.
- If the developer requests a different extension, use it without further debate — the developer's judgment on this UX decision is authoritative.

## Output of This Step

Return to the orchestrator for use by the Readme Assembler (`readme-assembly.md`):
```
FormatSelectionResult:
  extension: string            // e.g. "md", "html", "txt"
  imagesNeeded: boolean
  shippingNote?: string        // e.g. md+images: "画像ファイルを README と併せて配布物に同梱すること"
```
