# Gauge Allure Report plugin

A [Gauge](https://gauge.org) reporting plugin that writes test execution results as [Allure](https://allurereport.org/)
result files, so you can generate Allure HTML reports from your Gauge specs.

It works with every Gauge language runner (Java, C#, JavaScript, Python, Ruby, …) and ships as a single native
executable, so no .NET runtime is needed on the test machine.

## Requirements

* Gauge 1.6.30 or later
* [Allure CLI](https://allurereport.org/docs/install/) 3.x to generate the HTML report
* Windows, Linux or macOS on x64 or arm64

## Quick start

1. Download the zip for your platform from the [releases page](https://github.com/system-verification/allure-report/releases) and install it:

   ```bash
   gauge install allure-report --file allure-report-<version>-<platform>.zip
   ```

2. Enable it in your project's `manifest.json`:

   ```json
   {
     "Language": "java",
     "Plugins": ["html-report", "allure-report"]
   }
   ```

3. Run your specs, then generate and open the report:

   ```bash
   gauge run specs
   allure generate reports/allure-results --open
   ```

## Configuration

Set these as environment variables or in `env/default/default.properties`. Relative paths are resolved against the
Gauge project root.

| Property | Default | Description |
|----------|---------|-------------|
| `ALLURE_RESULTS_DIR` | `<gauge_reports_dir>/allure-results` | Where the Allure result files are written. `ALLURE_REPORT_DIR` is accepted as a legacy alias. |
| `ALLURE_ENABLED` | `true` | Set to `false` to switch the plugin off without uninstalling it. |
| `ALLURE_CLEAN_RESULTS` | `true` | Delete the previous run's results when a run starts. Set to `false` if the directory is shared with other test frameworks. |
| `ALLURE_CREATE_CONFIG` | `false` | Create `allurerc.json` in the project root to enable run history (see [History](#history)). |
| `ALLURE_LINK_ISSUE_PATTERN` | – | URL template for `issue:` tags; `{}` is replaced with the tag value. |
| `ALLURE_LINK_TMS_PATTERN` | – | URL template for `tms:` tags; `{}` is replaced with the tag value. |

Example `env/default/default.properties`:

```properties
ALLURE_RESULTS_DIR = reports/allure-results
ALLURE_LINK_ISSUE_PATTERN = https://jira.example.com/browse/{}
```

## What is reported

| Gauge | Allure |
|-------|--------|
| Spec folder / spec | `parentSuite` / `suite` (and `package`) |
| Scenario, and each data table row | Test result; table row values become parameters |
| Steps and concepts | Nested steps; steps that never ran after a failure are shown as skipped |
| Screenshots | Image attachments |
| `Gauge.writeMessage` output, hook messages | Text attachments |
| Table parameters | CSV attachments |
| Failed step | `failed` |
| Failed scenario or step hook | `broken` |
| Failed before/after spec or suite hook | A separate `broken` result, so failing setup can't silently hide tests |
| Skipped scenario | `skipped`, with Gauge's skip reason |
| Scenario interrupted by an aborted run | `broken` |
| Parallel streams (`gauge run -n`) | `thread` label per runner, for the Allure timeline |
| Retries (`--max-retries-count`) | Retries of the same test |

### Tags

Spec and scenario tags become Allure `tag` labels. Tags written as `name:value` are mapped to Allure metadata instead:

| Tag | Allure |
|-----|--------|
| `severity:critical` | Severity (`blocker`, `critical`, `normal`, `minor`, `trivial`) |
| `owner:`, `lead:`, `epic:`, `feature:`, `story:`, `layer:` | Label with the same name |
| `issue:ABC-123` | Issue link, URL from `ALLURE_LINK_ISSUE_PATTERN` |
| `tms:TC-42` | Test case link, URL from `ALLURE_LINK_TMS_PATTERN` |

```
# Login
Tags: epic:Accounts, owner:alice

## Valid login
Tags: severity:critical, issue:ABC-123, smoke
```

## History

Allure 3 stores run history in a JSON Lines file configured in `allurerc.json`. The Allure CLI reads that file from
the directory it's run in. To see trends and previous runs in the History tab, add this to your project root:

```json
{
  "historyPath": "reports/allure-results/allure-history.jsonl",
  "appendHistory": true
}
```

Alternatively, set `ALLURE_CREATE_CONFIG = true` and the plugin creates the file for you. It also keeps `historyPath`
pointing at the results directory, unless you've added other settings to the file, which it then leaves alone.

Run `allure generate` from the project root after each run, and keep the results directory between runs (the history
file is stored in it).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Native AOT builds also need the platform's
[C++ build tools](https://aka.ms/nativeaot-prerequisites).

Run the tests:

```bash
dotnet test Allure.GaugePlugin.slnx
```

Build an installable zip:

```bash
# Linux / macOS / Git Bash
./build.sh                  # Native AOT, current platform
./build.sh linux-arm64      # Native AOT, specific platform
AOT=false ./build.sh        # without AOT (no C++ tools needed)
```

```cmd
:: Windows (cmd)
build.bat                   &:: Native AOT, win-x64
build.bat win-arm64         &:: Native AOT, specific platform
set AOT=false & build.bat   &:: without AOT (no C++ tools needed)
```

The scripts print the `gauge install` command for the zip they create.

### Gauge protocol

`src/Allure.GaugePlugin/GrpcMessages` holds Gauge's `.proto` files (from
[gauge-proto](https://github.com/getgauge/gauge-proto)), and the C# generated from them is committed in
`GrpcMessages/Generated`, so regular builds don't need `protoc`. After updating the `.proto` files, regenerate with:

```bash
dotnet build src/Allure.GaugePlugin -p:GenerateProto=true
```

### Project layout

| Path | Contents |
|------|----------|
| `src/Allure.GaugePlugin` | The plugin: gRPC server for Gauge's reporter API, Allure result writer, `plugin.json` |
| `test/Allure.GaugePlugin.Tests` | Unit tests that feed Gauge messages to the writer and check the Allure JSON |
| `.github/workflows` | CI (tests and an AOT build) and the release workflow |

## Releasing

1. Update `version` in `src/Allure.GaugePlugin/plugin.json` and commit.
2. Push a matching tag: `git tag v1.2.0 && git push origin v1.2.0`.

The release workflow checks the tag against `plugin.json`, runs the tests, builds every platform and attaches the zips
to the GitHub release.

## Uninstall

```bash
gauge uninstall allure-report
```

## License

Licensed under the [Apache License 2.0](LICENSE).

This plugin started from the [ReportPortal Gauge agent](https://github.com/reportportal/agent-net-gauge)
(Apache 2.0), from which it inherits its Gauge integration.
