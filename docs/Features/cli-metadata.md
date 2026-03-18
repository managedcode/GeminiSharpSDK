# Feature: Gemini CLI Metadata

Links:
Architecture: [docs/Architecture/Overview.md](../Architecture/Overview.md)
Modules: [GeminiClient.cs](../../GeminiSharpSDK/Client/GeminiClient.cs), [GeminiCliMetadataReader.cs](../../GeminiSharpSDK/Internal/GeminiCliMetadataReader.cs), [GeminiCliMetadata.cs](../../GeminiSharpSDK/Models/GeminiCliMetadata.cs)
Source of truth: local `gemini` CLI + upstream npm package metadata (`/gemini-cli`)

---

## Purpose

Expose runtime Gemini CLI metadata to SDK consumers:

- installed `gemini-cli` version
- default model configured in local Gemini config
- local model catalog currently cached by Gemini CLI
- update availability status vs latest published npm `/gemini-cli` version

---

## Scope

### In scope

- `GeminiClient.GetCliMetadata()` public API.
- `GeminiClient.GetCliUpdateStatus()` public API.
- Reading version from `gemini --version`.
- Reading default model from `~/.gemini/config.toml`.
- Reading model catalog from `~/.gemini/models_cache.json`.
- Reading latest published package version from `npm view /gemini-cli version`.
- Returning package-manager-appropriate update command (`bun` or `npm`) in update status.

### Out of scope

- Remote model discovery over network APIs.
- Mutating user Gemini config files.
- Replacing Gemini CLI model selection logic.

---

## Business Rules

- Metadata read is read-only and does not mutate local Gemini state.
- If thread-level web search settings are not specified, SDK does not emit `web_search` overrides.
- SDK option and metadata decisions are based on real Gemini CLI behavior, not TypeScript SDK surface.
- Update check failures (for example missing `npm`) must return actionable status messages and never silently fail.
- Update command text must not assume npm-only installs; SDK must emit `bun` update command when bun-managed install is detected.

---

## Diagram

```mermaid
flowchart LR
  Client["GeminiClient.GetCliMetadata()"] --> Version["gemini --version"]
  Client --> Update["GeminiClient.GetCliUpdateStatus()"]
  Client --> Config["~/.gemini/config.toml"]
  Client --> Cache["~/.gemini/models_cache.json"]
  Update --> Npm["npm view /gemini-cli version"]
  Version --> Metadata["GeminiCliMetadata"]
  Npm --> UpdateStatus["GeminiCliUpdateStatus"]
  Config --> Metadata
  Cache --> Metadata
```

---

## Verification

- Unit parsing/update-check coverage: [GeminiCliMetadataReaderTests.cs](../../GeminiSharpSDK.Tests/Unit/GeminiCliMetadataReaderTests.cs)
- CLI arg behavior: [GeminiExecTests.cs](../../GeminiSharpSDK.Tests/Unit/GeminiExecTests.cs)
