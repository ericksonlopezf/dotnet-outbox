<!-- Copyright © Erickson Lopez. MIT License. -->

# Quality Gates & Code Analysis Methodology

In `EricksonLopez.Outbox`, a passing test suite is merely the beginning. We enforce draconian quality gates to ensure architectural integrity, prevent regression, and secure the AOT-compatibility of the library.

## 1. Mutation Testing: The Ultimate Test Gate

While Code Coverage measures what lines of code were executed, **Mutation Testing** (via Stryker) measures whether your tests actually assert the correct behavior.

### How It Works
Stryker generates "Mutants" by altering the IL/source code of the library:
- Changing `if (count > 0)` to `if (count >= 0)`.
- Changing `return true;` to `return false;`.
- Removing `await` keywords.
- Swapping SQL strings (e.g., changing `state = 0` to `state = 1`).

If our test suite still passes after a mutation is introduced, the mutant "survives". This means we have a gap in our assertions. 

### Our Strict Thresholds (`stryker-config.json`)
- **Target (High)**: 100%. We aim to kill every single mutant.
- **Warning (Low)**: 98%.
- **Build Break**: 95%. If the mutation score drops below 95%, the GitHub Actions pipeline fails and the Pull Request is blocked.

### Managing False Positives
We use the `ignore-methods` configuration to prevent Stryker from mutating side-effects that cannot (or should not) be unit tested:
- `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`
- `ConfigureAwait`
- `string` mutations are disabled to prevent the build from failing just because an exception message was altered.

## 2. Static Analysis: SonarQube & Roslyn

Our static analysis pipeline operates at two levels:

### Level 1: Roslyn Analyzers (Real-Time IDE Checks)
We distribute our own `EricksonLopez.Outbox.Analyzers` package, enforcing compile-time architectural integrity directly in the developer's IDE:
- **`OUTBOX001`**: Event class missing `[OutboxMessage]` attribute.
- **`OUTBOX002`**: Message stored without transaction context.
- **`OUTBOX003`**: Invalid message type alias formatting.
- **`OUTBOX004`**: Outbox message type must not be an abstract class.
- **`OUTBOX005`**: Missing valid constructor or property accessors for serialization.
- **`OUTBOX006`**: Invalid attribute target usage.
- **`OUTBOX007`**: Incompatible broker configuration parameters.
- **`OUTBOX008`**: Unsafe async fire-and-forget inside handlers.
- **`OUTBOX009`**: Redundant serializer registration detected.
- **`OUTBOX010`**: Transaction context lifetime mismatch.
- **`OUTBOX011`**: Stale lease timeout configured too low.
- **`OUTBOX012`**: Missing idempotency configuration on consumer handler.
- **`OUTBOX013`**: Missing `[JsonSerializable]` attribute in NativeAOT serialization context.

### Level 2: SonarCloud (Pipeline Quality Gate)
Integrated into `ci.yml` via `dotnet-sonarscanner`, SonarCloud enforces over 300+ C# rules.
- **Focus Areas**: Async/await deadlocks, unhandled exception leaking, disposable resource leaks, and thread-safety violations.
- **Quality Gate**: SonarCloud gate requires **0 Bugs**, **0 Vulnerabilities**, and a **Maintainability Rating of A**.

## 3. Code Coverage — Coverlet

Code coverage is collected during every CI run via `coverlet.collector` using the
`XPlat Code Coverage` data collector.

**Configuration** (`coverlet.runsettings` + CI workflow override):
- Formats: `opencover` and `cobertura` (configured via `DataCollectionRunSettings` in CI; `coverlet.runsettings` defaults to `cobertura` only)
- Output: `./test-results/**/coverage.opencover.xml` and `./test-results/**/coverage.cobertura.xml`

Coverage is uploaded to **Codecov** after every CI run (`flags: unittests`) and
again after every publish gate (`flags: publish-gate`). The `fail_ci_if_error: false`
setting means a Codecov outage cannot block a release.

---

## 4. The NativeAOT Compiler Gate

NativeAOT compatibility is enforced by a dedicated workflow — `aot-smoke-test.yml` —
which runs on every push and PR alongside the main CI build.

### How It Works

The workflow performs a full NativeAOT publish of the `EricksonLopez.Outbox.AotSmokeTest`
project with these flags:

```bash
dotnet publish \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained \
  -p:PublishAot=true \
  -p:TreatWarningsAsErrors=true \
  -p:WarningLevel=5
```

Additionally, the environment variable `DOTNET_EnableAotCompilationWarningsAsErrors=true`
is set to convert ILC (Ahead-of-Time compiler) diagnostics into errors.

### Why a Dedicated Workflow?

NativeAOT publishing requires the ILC (ILCompiler) to statically analyze the full
dependency graph. This step is separate from a normal `dotnet build` — you cannot
detect AOT incompatibilities with a standard compilation. The dedicated workflow
publishes an actual NativeAOT binary and **executes it**, confirming the output is a
working program, not just a binary that compiles.

### What Gets Caught

When `PublishAot=true` and `TreatWarningsAsErrors=true` are set:
- **IL2026**: Usage of `Activator.CreateInstance`, `Type.GetMethod`, etc. → build fails.
- **IL3050**: Usage of `MakeGenericType`, `MakeGenericMethod` → build fails.
- Any dynamic serializer (e.g., `Newtonsoft.Json`) introduced as a transitive dependency → build fails.

> [!NOTE]
> The main CI build (`dotnet-build-test.yml`) does **not** use `/p:IsAotCompatible=true`
> or `/p:PublishAot=true`. AOT compatibility is exclusively validated by `aot-smoke-test.yml`.

