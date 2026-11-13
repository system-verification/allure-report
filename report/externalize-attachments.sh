#!/usr/bin/env bash
# Moves the attachments in RESULTS_DIR that are larger than LIMIT_MB to S3 (TARGET) and links to them with presigned
# URLs instead: on the test, and as a small text attachment in the file's place. The single-file report embeds every
# attachment, and with large ones (e.g. Playwright traces) it gets too big for a browser to open.
set -euo pipefail

limit=$((LIMIT_MB * 1024 * 1024))
hours=$((EXPIRY / 3600))
shopt -s nullglob

for file in "$RESULTS_DIR"/*-attachment*; do
  size=$(wc -c < "$file")
  if [ "$size" -le "$limit" ]; then
    continue
  fi

  source=$(basename "$file")
  # The plugin names attachments <GUID>-attachment<ext>, and the prefix is per run, so keys don't collide.
  aws s3 cp "$file" "$TARGET/$source" --only-show-errors
  url=$(aws s3 presign "$TARGET/$source" --expires-in "$EXPIRY")
  note="${source%%-attachment*}-attachment-link.txt"
  name=
  type=

  for json in "$RESULTS_DIR"/*-result.json "$RESULTS_DIR"/*-container.json; do
    if ! grep -qF "\"$source\"" "$json"; then
      continue
    fi
    if [ -z "$name" ]; then
      name=$(jq -r --arg source "$source" 'first(.. | objects | select(.source? == $source) | .name)' "$json")
      type=$(jq -r --arg source "$source" 'first(.. | objects | select(.source? == $source) | .type)' "$json")
    fi
    result=false
    if [[ "$json" == *-result.json ]]; then
      result=true
    fi
    jq --arg source "$source" --arg note "$note" --arg name "$name" --arg type "$type" --arg url "$url" \
      --arg result "$result" -f "$(dirname "$0")/externalize.jq" "$json" > "$json.tmp"
    mv "$json.tmp" "$json"
  done

  {
    echo "$name ($((size / 1024 / 1024)) MB) is stored outside the report, in S3."
    echo "The links are valid for $hours hours from when the report was generated."
    echo
    echo "Download:"
    echo "$url"
    if [ "$type" = "application/vnd.allure.playwright-trace" ]; then
      echo
      echo "Open in the Playwright trace viewer (needs a CORS rule on the bucket, see the README):"
      echo "https://trace.playwright.dev/?trace=$(jq -rn --arg url "$url" '$url | @uri')"
    fi
  } > "$RESULTS_DIR/$note"
  rm "$file"
  echo "Moved $name ($source, $((size / 1024 / 1024)) MB) to $TARGET/$source"
done
