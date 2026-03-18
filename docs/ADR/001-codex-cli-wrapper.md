# ADR 001: Use Gemini CLI as SDK Transport

- Status: Accepted
- Date: 2026-03-05

## Context

Gemini is CLI-oriented and the current headless contract is the root command
`gemini --prompt ... --output-format stream-json`, which emits newline-delimited JSON events.
To keep behavior parity and reduce protocol drift, this .NET SDK needs a transport strategy aligned with real CLI behavior.

## Decision

Use the local Gemini CLI process as the only runtime transport layer for `ManagedCode.GeminiSharpSDK`.

- `GeminiExec` builds argument order and environment variables.
- `DefaultGeminiProcessRunner` starts process and streams stdout lines asynchronously.
- `ThreadEventParser` maps `stream-json` events to strongly typed events and items.

## Diagram

```mermaid
flowchart LR
  SDK["ManagedCode.GeminiSharpSDK"] --> Exec["GeminiExec"]
  Exec --> Cli["gemini --prompt ... --output-format stream-json"]
  Cli --> Json["stdout stream-json"]
  Json --> Parser["ThreadEventParser"]
  Parser --> Models["ThreadEvent / ThreadItem"]
```

## Consequences

### Positive

- High parity with upstream CLI behavior.
- No separate protocol server to maintain.
- Easy compatibility when Gemini CLI adds headless flags/events.

### Negative

- Requires `gemini` binary availability in environment.
- Runtime errors may come from external process failures.
- Unsupported headless-only option gaps must fail explicitly instead of being guessed by the SDK.

### Neutral

- SDK remains process-boundary integration; pure in-memory simulation is test-only via fake runner.

## Alternatives considered

- Direct HTTP protocol implementation: rejected due drift risk and higher maintenance.
- TCP/daemon transport abstraction now: deferred; may be revisited if CLI introduces stable server mode.
