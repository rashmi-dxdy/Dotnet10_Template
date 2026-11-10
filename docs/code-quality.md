# Code quality

Every project in the solution gets the same analyzers, compiler settings and conventions automatically.
No project file needs to opt in.

## What runs, and where it is configured

| Tool | Version | Rule prefix | Responsible for |
|---|---|---|---|
| C# compiler (all warning waves, nullable) | SDK | `CSxxxx` | Language correctness, nullable flow analysis |
| .NET SDK code analysis (`latest-recommended`) | SDK | `CAxxxx` | Correctness, reliability, performance, security |
| .NET SDK code style (enforced in build) | SDK | `IDExxxx` | Code style and naming (`IDE1006`) |
| [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) | 3.0.321 | `MAxxxx` | API usage pitfalls (string comparison, culture, etc.) |
| [SonarAnalyzer.CSharp](https://rules.sonarsource.com/csharp/) | 10.35.0.4138 | `Sxxxx` | Bugs, code smells, security hotspots |
| [Roslynator.Analyzers](https://josefpihrt.github.io/docs/roslynator/analyzers) | 5.0.1 | `RCSxxxx` | Refactoring hints (default severities, mostly IDE-only) |
| [Roslynator.Formatting.Analyzers](https://josefpihrt.github.io/docs/roslynator/analyzers) | 5.0.1 | `RCS0xxx` | Formatting. Only `RCS0056` (line length) is enabled |
| `dotnet format` | SDK | `WHITESPACE`, `IMPORTS` | Whitespace, indentation, using order |

| File | Purpose |
|---|---|
| `Directory.Packages.props` | Analyzer versions, applied to every project as `GlobalPackageReference` |
| `Directory.Build.props` | `AnalysisLevel`, warning level, warnings as errors, code style in build, XML doc generation |
| `.editorconfig` | Formatting, code style, naming rules and **every analyzer severity**, each with its reason |
| `tests/.editorconfig` | Test-only overrides (underscores allowed in test method names) |

### Why some rules are switched off

Each problem should be reported by exactly one tool. When two analyzers report the same thing, one of them
is switched off in `.editorconfig`, with a comment naming the rule that owns it. The overlaps were found by
building a file full of deliberate violations, so the list reflects real duplicates.

- **One type per file** is not enforced (`MA0048` off). Small related types such as enums and records may
  live next to the type that uses them.
- **Documentation comments** are optional (`CS1591` off). Doc comments that *are* written are still checked.

### Line length

The default maximum line length is **120 characters** for every file type (`max_line_length` under `[*]`
in `.editorconfig`).

In C# files the limit is enforced in the build by `RCS0056` (`roslynator_max_line_length = 120`), from
`Roslynator.Formatting.Analyzers`. The SDK analyzers don't check line length, and Sonar's `S103` is not in
the NuGet analyzer. `RCS0056` has a code fix, so long lines are **wrapped automatically**:

| Where | How |
|---|---|
| CLI / CI | `./scripts/format.sh` or `./scripts/format.ps1` (`dotnet format` applies the fix across the solution) |
| Visual Studio | Click the `RCS0056` squiggle, press `Ctrl+.`, choose the wrap fix, then *Fix all occurrences in* Document / Project / Solution |
| Rider | `Alt+Enter` on the warning, choose the wrap fix, then *Fix in file/project/solution*. *Reformat Code* also wraps to `max_line_length` |
| VS Code (C# Dev Kit) | `Ctrl+.` on the warning |

The fix wraps as little as needed to fit: before `=>`, one argument per line, or at a method-chain `.`.
If the result reads badly (a long chain wrapped only once, for example), split it further by hand.

The column guide:

- **Rider** shows it from `max_line_length`, with no setup.
- **Visual Studio** has no built-in guide. Install the free *Editor Guidelines* extension, which reads
  `guidelines = 120` from `.editorconfig`.

To change the limit, update `max_line_length`, `roslynator_max_line_length` **and** `guidelines`.


### Why StyleCop.Analyzers is not used

Its only stable release (1.1.118) dates from 2019 and predates modern C#. In this repository it crashed on
C# 12 syntax and reported false positives for .NET 10 top-level programs. Most of its rules duplicate
`dotnet format`, the IDE code-style rules or Meziantou/Sonar. The rest (member ordering, blank-line layout)
are left to code review. Reconsider it when a stable 1.2+ release ships.

## Fixing violations

Warnings are errors, so any violation fails the build. Work through these steps in order:

1. **Run the formatter.** It fixes whitespace, using order and every rule that has an automatic code fix:

   ```bash
   ./scripts/format.sh        # or: ./scripts/format.ps1
   ```

2. **Read the rule.** Every diagnostic includes its ID and a help link, for example
   `MA0006: Use string.Equals instead of Equals operator (https://...MA0006.md)`. In Visual Studio, Rider
   and VS Code, the light-bulb (`Ctrl+.`) usually offers a fix.

3. **Fix the code.** Usual fixes for common rules:

   | Rule | Typical fix |
   |---|---|
   | `CS86xx` (nullable) | Handle `null` explicitly, use `is not null` checks, or correct the annotation (`string?`) |
   | `IDE0005` | Remove the unused `using` (fixed by `format`) |
   | `IDE0040` / `IDE1006` | Add the access modifier; rename to match the naming rules (`_camelCase` private fields) |
   | `IDE0161` | Use a file-scoped namespace: `namespace X;` (fixed by `format`) |
   | `CA1822` | Mark the member `static` |
   | `CA1861` | Move constant arrays passed as arguments into `static readonly` fields |
   | `MA0006` | `string.Equals(a, b, StringComparison.Ordinal)` instead of `==` |
   | `MA0007` | Add a trailing comma to the last item of a multi-line initializer (fixed by `format`) |

4. **If a rule is genuinely wrong for one specific place,** suppress it as narrowly as possible and give a
   reason:

   ```csharp
   [SuppressMessage("Design", "CA1031:Do not catch general exception types",
       Justification = "Top-level handler must log and swallow everything.")]
   ```

   or, for a few lines, `#pragma warning disable XX1234 // reason` ... `#pragma warning restore XX1234`.

5. **If a rule is wrong for the whole repository,** change its severity in `.editorconfig`, in the
   *Analyzer policy* section, with a comment explaining why. Discuss it in code review first. Don't use
   `<NoWarn>` in project files.

## Commands

| Task | Command |
|---|---|
| Fix formatting and auto-fixable issues | `./scripts/format.sh` / `./scripts/format.ps1` |
| Check formatting only | `./scripts/format.sh --verify-no-changes` |
| Full quality gate (restore, format check, build, test) | `./scripts/verify.sh` / `./scripts/verify.ps1` |
| Same, Debug configuration | `CONFIGURATION=Debug ./scripts/verify.sh` / `./scripts/verify.ps1 -Configuration Debug` |

The verify scripts run these commands, in this order:

```bash
dotnet restore Dotnet10Template.slnx
dotnet format Dotnet10Template.slnx --verify-no-changes --no-restore
dotnet build Dotnet10Template.slnx --no-restore --configuration Release
dotnet test --solution Dotnet10Template.slnx --no-build --configuration Release
```

`dotnet format` ships with the .NET SDK, so the repository needs no .NET local tool manifest and no Node.js.

On Linux/macOS, if the scripts aren't executable after cloning, run `bash scripts/verify.sh`, or mark them
once with `git update-index --chmod=+x scripts/*.sh`.
