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

## GitHub Actions

Two actions in this repository install the plugin and publish the report in a workflow:

```yaml
- name: Install Gauge
  run: curl -SsL https://downloads.gauge.org/stable | sh   # plus your language runner

- uses: system-verification/allure-report/setup@v1

- name: Run specs
  run: gauge run specs

- uses: system-verification/allure-report/report@v1
  if: always()
```

`setup` downloads the plugin release for the runner's platform, installs it and adds `allure-report` to
`manifest.json` (for that run only), so the plugin doesn't have to be installed locally. Gauge must be installed first.

| Input | Default | Description |
|-------|---------|-------------|
| `project-dir` | `.` | The Gauge project directory |
| `version` | `latest` | The plugin release to install, e.g. `v1.2.0` |
| `github-token` | `github.token` | Token used to download the release |

`report` generates the report with the Allure CLI (needs Node.js, which GitHub's runners have), uploads it as an
artifact and adds a summary table to the workflow run's page.

| Input | Default | Description |
|-------|---------|-------------|
| `project-dir` | `.` | The Gauge project directory |
| `results-dir` | `reports/allure-results` | The Allure results, relative to `project-dir` |
| `report-dir` | `reports/allure-report` | Where to generate the report, relative to `project-dir` |
| `single-file` | `true` | Generate one `index.html`, which opens straight from a downloaded artifact |
| `allure-version` | `3` | The Allure CLI version |
| `artifact-name` | `AllureReport` | The artifact name; empty skips the upload |
| `github-pages-url` | – | Where you publish `report-dir` yourself (e.g. GitHub Pages, in a later step); adds a link to the summary. `report-url` is accepted as a deprecated alias. |
| `aws-s3-bucket` | – | Upload the report to this private S3 bucket and link to it with a presigned URL (see below) |
| `aws-s3-prefix` | `<owner>/<repo>/<run id>-<attempt>` | The folder in the bucket |
| `aws-s3-link-expiry` | `604800` | Seconds the presigned link is valid (at most 7 days) |

The action outputs the link it put in the summary as `report-link`, e.g. to post it to a chat.

### Hosting the report in S3

GitHub Pages sites are public, even for private repositories. To keep reports private, `report` can upload the
single-file report to a private S3 bucket and link to it with a presigned URL: a link that gives read access to that
one file until it expires.

```yaml
- uses: aws-actions/configure-aws-credentials@v4
  if: always()
  with:
    aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
    aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
    aws-region: eu-north-1

- uses: system-verification/allure-report/report@v1
  if: always()
  with:
    aws-s3-bucket: my-allure-reports
```

Setting up AWS:

1. Create a bucket with Block Public Access on (the default). Add a lifecycle rule that deletes objects after e.g. 30
   days, so old reports are cleaned up.
2. Create an IAM user with this policy, and an access key for it. Store the key as repository secrets.

   ```json
   {
     "Version": "2012-10-17",
     "Statement": [{
       "Effect": "Allow",
       "Action": ["s3:PutObject", "s3:GetObject"],
       "Resource": "arn:aws:s3:::my-allure-reports/*"
     }]
   }
   ```

   `s3:GetObject` is needed because a presigned URL acts with the permissions of whoever signed it.

A presigned link can't outlive the credentials it was signed with. An IAM user's access key gives the full 7 days;
temporary credentials, such as from an OIDC role (`role-to-assume`), end the link when their session ends (at most 12
hours). After a link has expired, the report is still in the workflow run's artifact.

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
| Messages with an `allure-attachment` marker | File attachments (see [Attaching files](#attaching-files)) |
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

### Attaching files

Gauge can only attach screenshots, so other files (Playwright traces, videos, logs, …) are attached through a marker
in a message. Write a message containing:

```html
<!-- allure-attachment path="reports/login-trace.zip" name="Trace" type="application/vnd.allure.playwright-trace" -->
```

| Attribute | Required | Description |
|-----------|----------|-------------|
| `path` | yes | The file to attach. Relative paths are resolved against the Gauge project root. |
| `name` | no | The attachment name. Defaults to the file name. |
| `type` | no | The content type. Defaults to one based on the extension (e.g. `.webm` → `video/webm`, `.zip` → `application/zip`). |

The plugin copies the file into the results when the step, hook or scenario finishes, so the file must exist by then.
A message containing a marker is left out of the messages text attachment. The marker is an HTML comment, so it's
invisible in Gauge's HTML report: one message can carry both a link for the HTML report and the file for Allure.
Missing files are skipped.

Use the type `application/vnd.allure.playwright-trace` for Playwright traces: Allure opens them in the Playwright
trace viewer, and lists traces attached to the test itself (e.g. from an after-scenario hook) in a "Playwright Trace"
section at the top of the test.

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
to the GitHub release. It also points the major version tag (e.g. `v1`), which workflows use to reference the GitHub Actions, at the new release.

## Uninstall

```bash
gauge uninstall allure-report
```

## License

Licensed under the [Apache License 2.0](LICENSE).

This plugin started from the [ReportPortal Gauge agent](https://github.com/reportportal/agent-net-gauge)
(Apache 2.0), from which it inherits its Gauge integration.
