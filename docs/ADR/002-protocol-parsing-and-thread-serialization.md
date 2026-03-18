# ADR 002: Explicit Protocol Constants and Serialized Per-GeminiThread Turns

- Status: Accepted
- Date: 2026-03-05

## Context

Gemini CLI now emits `stream-json` events such as `init`, `message`, `tool_use`, `tool_result`, `result`, and `error`.
Without strict parsing rules and synchronized turn execution, SDK behavior can diverge under concurrency and protocol evolution.
User rule in this repository explicitly forbids inline string literals for protocol token matching.

## Decision

1. Centralize protocol tokens in `GeminiProtocolConstants`.
2. Parse events/items only through constant-based switches.
3. Support the current `stream-json` contract first, while tolerating a limited set of legacy persisted fixtures/events where needed for backward-compatible tests.
4. Serialize execution per `GeminiThread` instance with `SemaphoreSlim`.

## Diagram

```mermaid
flowchart LR
  Line["stream-json line"] --> Parse["ThreadEventParser"]
  Parse --> Consts["GeminiProtocolConstants"]
  GeminiThread["GeminiThread.Run*Async"] --> Lock["SemaphoreSlim turn lock"]
  Lock --> Exec["GeminiExec.RunAsync"]
  Exec --> Parse
```

## Consequences

### Positive

- No magic literals in parser logic.
- Safer maintenance when protocol tokens change.
- Eliminates race conditions for same thread instance.
- Current runtime and test fixtures can evolve independently without hidden parser drift.

### Negative

- Additional constants maintenance when upstream adds new token names or retires old ones.

### Neutral

- Multi-thread concurrency is still allowed across different `GeminiThread` instances.

## Alternatives considered

- Keep inline string literals: rejected by project rule and maintainability concerns.
- Lock-free per-thread execution: rejected due shared thread state (`thread_id`, event stream aggregation).
