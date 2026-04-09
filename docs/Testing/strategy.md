# Testing Strategy

## Goal

Verify `ManagedCode.GeminiSharpSDK` behavior against real Gemini CLI contracts, with deterministic automated tests for both baseline and additive C# capabilities.

## Test levels used in this repository

- Primary: TUnit behavior tests in `GeminiSharpSDK.Tests`
- Optional CI matrix: cross-platform Gemini CLI smoke verification (`.github/workflows/real-integration.yml`)

## Principles

- Test observable behavior, not implementation details.
- Use the real installed `gemini` CLI for process interaction tests; do not use `FakeGeminiProcessRunner` doubles.
- Treat `gemini` as a prerequisite for real integration runs and install it in CI/local setup before running those tests.
- CI validates Gemini CLI smoke behavior on Linux/macOS/Windows without requiring login: CLI must be discoverable and invokable.
- Smoke coverage validates both `gemini --help` and headless `gemini --prompt ... --output-format stream-json`.
- Cross-platform CI smoke also validates isolated-profile unauthenticated behavior through a headless prompt run, proving binary discovery + process launch without relying on local credentials.
- Real integration runs must use existing Gemini CLI login/session; test harness does not use API key environment variables.
- Auth-required real Gemini tests run under a shared parallel limiter so local full-suite execution does not deadlock or stall on concurrent headless CLI sessions.
- Auth-required real Gemini thread/session tests must also run in unique git sandboxes under `tests/.sandbox/*` and keep those directories on disk for the duration of local test history, because Gemini CLI caches visited project paths in `~/.gemini/projects.json` and can fail later startup if a referenced sandbox disappears mid-suite.
- Multi-turn real Gemini tests must allocate cancellation budgets per turn instead of sharing one timeout across the whole conversation, so a slow first turn does not starve the second turn and create flaky resume/thread assertions.
- Real integration model selection must be explicit: set `GEMINI_TEST_MODEL`, define `model` in `~/.gemini/config.toml`, or rely on a recent local Gemini session that already recorded the active model in the current profile.
- Cover error paths and cancellation paths.
- Keep protocol parser coverage for all supported event/item kinds.
- Keep a large-stream parser performance profile test to catch regressions.

## Commands

- build: `dotnet build ManagedCode.GeminiSharpSDK.slnx -c Release -warnaserror`
- test: `dotnet test --solution ManagedCode.GeminiSharpSDK.slnx -c Release`
- coverage: `dotnet test --solution ManagedCode.GeminiSharpSDK.slnx -c Release -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml`
- gemini smoke subset: `dotnet test --project GeminiSharpSDK.Tests/GeminiSharpSDK.Tests.csproj -c Release -- --treenode-filter "/*/*/*/GeminiCli_Smoke_*"`
- ci/release non-auth full run: `dotnet test --solution ManagedCode.GeminiSharpSDK.slnx -c Release -- --treenode-filter "/*/*/*/*[RequiresGeminiAuth!=true]"`

Smoke subset is an additional gate and does not replace full-solution test execution.

TUnit on Microsoft Testing Platform does not support `--filter`; run focused tests with `-- --treenode-filter "/*/*/<ClassName>/*"`.

## Test map

- Client lifecycle and concurrency: [GeminiClientTests.cs](../../GeminiSharpSDK.Tests/Unit/GeminiClientTests.cs)
- `GeminiClient` API surface behavior: [GeminiClientTests.cs](../../GeminiSharpSDK.Tests/Unit/GeminiClientTests.cs)
- GeminiThread run/stream/failure behavior: [GeminiThreadTests.cs](../../GeminiSharpSDK.Tests/Unit/GeminiThreadTests.cs)
- CLI arg/env/config behavior: [GeminiExecTests.cs](../../GeminiSharpSDK.Tests/Unit/GeminiExecTests.cs)
- CLI metadata parsing behavior: [GeminiCliMetadataReaderTests.cs](../../GeminiSharpSDK.Tests/Unit/GeminiCliMetadataReaderTests.cs)
- Cross-platform Gemini CLI smoke behavior: [GeminiCliSmokeTests.cs](../../GeminiSharpSDK.Tests/Integration/GeminiCliSmokeTests.cs)
- Real process integration behavior: [GeminiExecIntegrationTests.cs](../../GeminiSharpSDK.Tests/Integration/GeminiExecIntegrationTests.cs)
- Real Gemini CLI integration behavior (local login required): [RealGeminiIntegrationTests.cs](../../GeminiSharpSDK.Tests/Integration/RealGeminiIntegrationTests.cs)
- Protocol parser behavior: [ThreadEventParserTests.cs](../../GeminiSharpSDK.Tests/Unit/ThreadEventParserTests.cs)
- Protocol parser large-stream performance profile: [ThreadEventParserPerformanceTests.cs](../../GeminiSharpSDK.Tests/Performance/ThreadEventParserPerformanceTests.cs)
- Serialization and schema temp file behavior: [TomlConfigSerializerTests.cs](../../GeminiSharpSDK.Tests/Unit/TomlConfigSerializerTests.cs), [OutputSchemaFileTests.cs](../../GeminiSharpSDK.Tests/Unit/OutputSchemaFileTests.cs)
