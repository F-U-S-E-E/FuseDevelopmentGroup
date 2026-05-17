# AGENTS.md

## Skill And Agent Routing
The `.claude/` directory ships skills and sub-agents from `Aaronontheweb/dotnet-skills` and
Microsoft's `dotnet/skills` marketplace. Most target modern .NET stacks that do not apply
to this Unity + .NET Framework 4.8 mod. Use the curated set below; ignore skipped categories
unless the user explicitly invokes them.

Workflow: skim repo patterns, consult the named skill, implement the smallest change, note conflicts.

Build and MSBuild:
- Diagnose a build failure: `binlog-generation`, `binlog-failure-analysis`, `msbuild-antipatterns`
- Slow build: `build-perf-diagnostics`, `eval-performance`, `incremental-build`, `build-parallelism`, `msbuild-server`
- Project-file organization / Directory.Build.*: `directory-build-organization`, `msbuild-modernization`
- Output or reference collisions: `check-bin-obj-clash`, `resolve-project-references`, `including-generated-files`

Performance and diagnostics:
- Scan for .NET perf anti-patterns: `analyzing-dotnet-performance`
- Trace / dump collection: `dotnet-trace-collect`, `dump-collect`
- .NET Framework CLR activation issues (mscoree.dll, wrong runtime): `clr-activation-debugging`
- Sealed types, readonly structs, `Span<T>`, hot-path allocation: `csharp-type-design-performance`

C# language and code:
- Modern patterns (records, nullable, pattern matching): `csharp-coding-standards`
  (caveat: `LangVersion` and net48 limit some C# 12+ features; favor patterns that compile here)
- Async, `Task` vs lock vs `Channel<T>`, Unity main-thread interop: `csharp-concurrency-patterns`
- Mod-facing API design and versioning: `csharp-api-design`
- Save/load formats, AOT-safe serializers: `serialization`
- Decompile Railroader / Unity / NuGet binaries: `ilspy-decompile`
- Native interop edge cases: `dotnet-pinvoke`
- Quick single-file C# experiments: `csharp-scripts`

Packages and tooling:
- NuGet / Central Package Management: `package-management`, `convert-to-cpm`
- Dev tools via `dotnet tool`: `local-tools`

Quality gates:
- After substantial new or LLM-authored code: `slopwatch`
- If a real test project lands: `crap-analysis`, `crap-score`, `coverage-analysis`, `test-anti-patterns`

Specialist sub-agents (via the `Agent` tool's `subagent_type`):
- MSBuild expert (configs, targets, evaluation): `msbuild`
- Project-file code review: `msbuild-code-review`
- Build performance investigation: `build-perf`
- Perf optimization: `optimizing-dotnet-performance`, `dotnet-performance-analyst`
- Threading / async race analysis: `dotnet-concurrency-specialist`

Skipped categories (do not consult unless explicitly invoked):
- Modern stacks irrelevant to a net48 Unity mod: Aspire, Akka.NET, EF Core / database,
  ASP.NET / web APIs, MAUI / mobile, Blazor, Playwright, Testcontainers, AI / ML / MCP,
  `Microsoft.Extensions.*` DI/Config, OpenTelemetry, email / MJML, DocFX, Roslyn generators
- All `migrate-*` skills (locked to net48 for Unity), `dotnet-aot-compat`,
  `thread-abort-migration`, `system-text-json-net11`
- Test-framework runners and migrations until a real test project exists: `run-tests`,
  `filter-syntax`, `platform-detection`, `dotnet-test-frameworks`, all `exp-*` skills,
  all `code-testing-*` agents, `migrate-mstest-*`, `migrate-vstest-to-mtp`,
  `migrate-xunit-to-xunit-v3`, `mtp-hot-reload`, `writing-mstest-tests`
- Unused sub-agents: `akka-net-specialist`, `docfx-specialist`,
  `roslyn-incremental-generator-specialist`, `template-engine`, `testability-migration`,
  `test-migration`, `test-quality-auditor`, `dotnet-benchmark-designer`
