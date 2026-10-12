# Gauge Allure 3 plugin

Gauge Allure Report plugin creates [Allure](https://allurereport.org/) test JSON result files that can be
used to generate rich HTML reports with Allure CLI. The format of result files is based on the [Allure result schema](https://allurereport.org/).


## Installation

* Download the plugin from [Releases](https://github.com/system-verification/allure-report/releases)
```
gauge install allure-report --file allure-report-$VERSION-$OS.$ARCH.zip
```

## Generating an HTML report

After running your specs, generate and open the HTML report with the Allure CLI (v3+):

```
allure generate reports/allure-results --open
```

# Configuration

The plugin is configured with environment variables or properties in `env/default/default.properties`:
`ALLURE_RESULTS_DIR`, `ALLURE_ENABLED`, `ALLURE_CLEAN_RESULTS`, `ALLURE_CREATE_CONFIG`,
`ALLURE_LINK_ISSUE_PATTERN` and `ALLURE_LINK_TMS_PATTERN`. Relative paths are resolved against the Gauge project root.

See the [repository README](../../README.md) for details, tag mapping and history setup.

# License

This program is licensed under:

[Apache License, Version 2.0](https://www.apache.org/licenses/LICENSE-2.0.txt)
