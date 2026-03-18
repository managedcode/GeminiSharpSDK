# ADR 001: Use Gemini CLI as SDK Transport

- Status: Accepted
- Date: 2026-03-05

## Context

Gemini is CLI-oriented and communicates via `gemini exec --json` with JSONL events.
To keep behavior parity and reduce protocol drift, this .NET SDK needs a transport strategy aligned with real CLI behavior.

## Decision

Use the local Gemini CLI process as the only runtime transport layer for `ManagedCode.GeminiSharpSDK`.

- `GeminiExec` builds argument order and environment variables.
- `DefaultGeminiProcessRunner` starts process and streams stdout lines asynchronously.
- `ThreadEventParser` maps JSONL protocol to strongly typed events and items.

## Diagram

```mermaid
flowchart LR
  SDK["ManagedCode.GeminiSharpSDK"] --> Exec["GeminiExec"]
  Exec --> Cli["gemini exec --json"]
  Cli --> Jsonl["stdout JSONL"]
  Jsonl --> Parser["ThreadEventParser"]
  Parser --> Models["ThreadEvent / ThreadItem"]
```

## Consequences

### Positive

- High parity with upstream CLI behavior.
- No separate protocol server to maintain.
- Easy compatibility when Gemini CLI adds flags/events.

### Negative

- Requires `gemini` binary availability in environment.
- Runtime errors may come from external process failures.

### Neutral

- SDK remains process-boundary integration; pure in-memory simulation is test-only via fake runner.

## Alternatives considered

- Direct HTTP protocol implementation: rejected due drift risk and higher maintenance.
- TCP/daemon transport abstraction now: deferred; may be revisited if CLI introduces stable server mode.
