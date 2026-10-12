# Turns the Allure report's summary.json into Markdown for the GitHub workflow run summary.
# Usage: jq -r --arg url <report url or ""> --arg expires <seconds the link is valid, or ""> -f summary.jq summary.json

def capitalize: (.[:1] | ascii_upcase) + .[1:];
def duration: (. / 1000 | floor) as $s
  | if $s < 60 then "\($s) s" else "\($s / 60 | floor) min \($s % 60) s" end;
def validity: if $expires != "" then " (link valid until \(now + ($expires | tonumber) | strftime("%Y-%m-%d %H:%M UTC")))" else "" end;

.stats as $s
| (.status // "unknown") as $status
| "### Allure Report: \($status | capitalize)",
  "",
  "| Total | Passed | Failed | Broken | Skipped | Unknown | Duration |",
  "|---|---|---|---|---|---|---|",
  "| \($s.total // 0) | \($s.passed // 0) | \($s.failed // 0) | \($s.broken // 0) | \($s.skipped // 0) | \($s.unknown // 0) | \(.duration // 0 | duration) |",
  if $url != "" then "", "[Open the report](\($url))\(validity)" else empty end
