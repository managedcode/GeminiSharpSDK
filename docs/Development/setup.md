# Development Setup

## Prerequisites

- .NET SDK `10.0.103` (see `global.json`)
- Gemini CLI available locally (`gemini` in PATH) for real runtime usage
- Git with submodule support

## Windows Gemini process lookup

- Runtime lookup order:
- npm-installed native vendor binary under `node_modules/@google/*/vendor/<target>/gemini/gemini.exe`
  - PATH candidates in order: `gemini.exe`, `gemini.cmd`, `gemini.bat`, `gemini`
- This allows both native npm optional packages and global npm shim installs to work on Windows.

## Bootstrap

```bash
git submodule update --init --recursive
dotnet restore ManagedCode.GeminiSharpSDK.slnx
```

## Solution projects

- `GeminiSharpSDK/GeminiSharpSDK.csproj` — core `ManagedCode.GeminiSharpSDK` package.
- `GeminiSharpSDK.Extensions.AI/GeminiSharpSDK.Extensions.AI.csproj` — optional `IChatClient` adapter package (`ManagedCode.GeminiSharpSDK.Extensions.AI`).
- `GeminiSharpSDK.Extensions.AgentFramework/GeminiSharpSDK.Extensions.AgentFramework.csproj` — optional Microsoft Agent Framework adapter package (`ManagedCode.GeminiSharpSDK.Extensions.AgentFramework`).
- `GeminiSharpSDK.Tests/GeminiSharpSDK.Tests.csproj` — core SDK tests (TUnit).
- `GeminiSharpSDK.Tests/AgentFramework/*` — Microsoft Agent Framework adapter tests (TUnit, same test project).

## Local validation

```bash
dotnet build ManagedCode.GeminiSharpSDK.slnx -c Release -warnaserror
dotnet test --solution ManagedCode.GeminiSharpSDK.slnx -c Release
dotnet format ManagedCode.GeminiSharpSDK.slnx
```

Focused run (TUnit/MTP):

```bash
dotnet test --project GeminiSharpSDK.Tests/GeminiSharpSDK.Tests.csproj -c Release -- --treenode-filter "/*/*/ThreadEventParserTests/*"
```

## Packaging check

```bash
dotnet pack GeminiSharpSDK/GeminiSharpSDK.csproj -c Release --no-build -o artifacts
```

## CI/workflows

- CI: `.github/workflows/ci.yml`
- Release: `.github/workflows/release.yml`
- CodeQL: `.github/workflows/codeql.yml`
- Gemini CLI sync watcher: `.github/workflows/gemini-cli-watch.yml`
- Real integration matrix: `.github/workflows/real-integration.yml`
