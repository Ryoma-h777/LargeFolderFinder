# Duplication Check Rules

Defines how ccx-readme-gen decides whether a README item (usage, system requirements, installation) duplicates an existing in-app guidance channel, and what to do when it does. Referenced by SKILL.md at runtime (Read tool).

This is a new capability (not present in the original `ccx-build-package/rules/readme-gen.md`) — it exists specifically to avoid redundant documentation when the app already guides the user well internally.

## Step 1: Detect In-App Guidance Signals

For each of the three checkable items (usage, system requirements/environment, installation), scan `design.md` and `requirements.md` for explicit mentions of in-app guidance, such as:
- "ヘルプ画面", "初回起動ガイド", "オンボーディング", "チュートリアル", "アプリ内ヘルプ"
- "in-app help", "onboarding flow", "first-run wizard", "built-in tutorial"

A signal only counts if it is **explicitly tied to the specific item** being checked (e.g. a mention of an onboarding flow that walks through usage counts for "usage", but does not automatically count for "installation" unless the onboarding explicitly covers installation too).

## Step 2: Classify Each Item

For each of the three items, classify as:

- **`duplicate-confirmed`**: The spec documents explicitly state that an in-app guidance channel exists and covers this item well. Example: design.md states "初回起動時にセットアップウィザードが表示され、必要な設定を案内する" → installation is `duplicate-confirmed`.
- **`unique`**: No in-app guidance is described for this item; README must carry the full detail.
- **`unclear`**: The spec documents mention *something* related but it's ambiguous whether it fully covers the item, or there's no clear statement either way.

**Default to `unique` or `unclear` over `duplicate-confirmed`** whenever there is doubt. A false "duplicate-confirmed" causes real information loss for the end user; a false "unique" only costs a little redundancy. This asymmetry means the classifier must lean conservative.

## Step 3: Handle Each Classification

### `duplicate-confirmed`
Replace the detailed content for this item with a short guidance-pointer sentence, e.g.:
```
使い方の詳細はアプリ内のヘルプ画面をご確認ください。
```
Do not omit the item entirely — always leave a pointer so the user knows where to look.

### `unique`
Include the full collected content for this item as normal (from content-collection.md).

### `unclear`
Do not guess. Ask the developer directly:
```
{item}（使い方 / 動作環境 / インストール手順）について、アプリ内に十分な説明導線がありますか？
あれば README では簡潔な案内に留め、なければ詳細を記載します。
```
Wait for the developer's answer before finalizing this item's content.

## Output of This Step

Return a per-item decision to the orchestrator:
```
DuplicationResult:
  usage: { status: "duplicate-confirmed" | "unique" | "unclear", guidanceText?: string }
  systemRequirements: { status: ..., guidanceText?: string }
  installation: { status: ..., guidanceText?: string }
```

Note: `installation` is only evaluated when `content-collection.md` Step 5 determined installation is required. If installation is not required, skip this item's duplication check entirely (the section is omitted regardless).
