# Replaces the attachment $source with the text attachment $note, and adds links to the S3 copy to the test.
# Containers (befores/afters) have no links, so only their attachment is replaced.
def viewer: "https://trace.playwright.dev/?trace=" + ($url | @uri);

(.. | objects | select(.source? == $source)) |= { name: (.name + " (link)"), source: $note, type: "text/plain" }
| if $result == "true" then
    .links = (.links // [])
      + [{ name: ($name + " (download)"), url: $url, type: "link" }]
      + (if $type == "application/vnd.allure.playwright-trace"
         then [{ name: ($name + " (Playwright trace viewer)"), url: viewer, type: "link" }]
         else [] end)
  else . end
