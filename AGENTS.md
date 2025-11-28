# Repository Guidelines
This repo houses the Sparky Vintage Story mod and its Modified Nodal Analysis solver. Use this guide to orient quickly and keep contributions consistent.

## Project Structure & Module Organization
- `SparkyModSystem.cs`: Mod entry point; wires the solver into the game.
- `MNA/`: Core solver (Circuit, Node, Component subclasses like Resistor, VoltageSource, Transformer). See `MNA.md` for theory and architecture.
- `Sparky.Tests/`: NUnit tests covering solver correctness and regressions.
- `Sparky.Benchmarks/`: BenchmarkDotNet runner plus `benchmark.sh` helper; results under `BenchmarkDotNet.Artifacts/`.
- `assets/`, `resources/`, `modinfo.json`: Mod metadata and packaged content copied on Release builds.
- `apidocs/`: Vendored API docs excluded from builds; leave untouched unless regenerating docs.
- `tools/`: Python helpers for benchmark comparisons and trailers.

## Build, Test, and Development Commands
- Preferred dev shell: `nix develop` (sets .NET 8 and `VINTAGE_STORY` path).
  This is entered automatically, but any changes to flake.nix require human action post-edit.
- Build debug: `dotnet build`. Package for the game: `dotnet build -c Release` (creates `bin/Sparky.zip`).
- Tests: `dotnet test` (NUnit); coverage: `dotnet test -p:CollectCoverage=true`.
- Benchmarks: `dotnet run -c Release --project Sparky.Benchmarks/Sparky.Benchmarks.Runner.csproj --filter '*'` or `./benchmark.sh run`; compare CSVs with `./benchmark.sh compare base.csv new.csv`; `./benchmark.sh trailer` benchmarks current vs parent and appends a commit trailer.

## Coding Style & Naming Conventions
- C# `net8.0`, nullable enabled, `LangVersion` latest; 4-space indents, braces on new lines to match existing files.
- PascalCase for types/methods/properties, camelCase for locals/parameters, `UPPER_SNAKE` for constants.
- Use `var` when the type is obvious; prefer `readonly` where possible.
- Keep solver stamps and Newton iteration logic well-commented but concise; align terminology with `MNA.md`.
- Update design docs (*.md in the main directory) to align with changes.

## Testing Guidelines
- Tests live in `Sparky.Tests/*Tests.cs` and use NUnit `[Test]`; name tests with scenario + expectation (e.g., `TestVoltageDivider`).
- Prefer small circuits with explicit node naming and tolerance checks like `Within(1e-6)`; pin ground the same way the solver does.
- Add regression tests when touching component stamps, solve paths (dense vs sparse), caching, or transient logic.

## Commit & Pull Request Guidelines
- Commit messages: short, imperative, and scoped (e.g., “Add dense fallback”, “Update MNA docs”); one logical change per commit.
- PRs should summarize behavior changes, list tests/benchmarks run, and link issues when relevant. Include benchmark CSVs or `benchmark.sh compare` output for performance-sensitive changes, and note any in-game effects or asset updates.
