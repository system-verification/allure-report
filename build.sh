#!/bin/bash
set -e

if [ -z "$1" ]; then
    case "$(uname -s)" in
        MINGW*|MSYS*|CYGWIN*) RUNTIME="win-x64" ;;
        Darwin*)               RUNTIME="osx-arm64" ;;
        *)                     RUNTIME="linux-x64" ;;
    esac
else
    RUNTIME="$1"
fi

PROJECT="src/Allure.GaugePlugin"
OUT_DIR="$PROJECT/bin/Release/net10.0/$RUNTIME"
PUBLISH_DIR="$OUT_DIR/publish"

AOT="${AOT:-true}"

echo "Publishing for $RUNTIME (AOT=$AOT)..."
dotnet publish "$PROJECT" -r "$RUNTIME" --self-contained -c Release -p:PublishAot=$AOT

VERSION=$(grep -o '"version"[[:space:]]*:[[:space:]]*"[^"]*"' "$PROJECT/plugin.json" | head -1 | grep -o '"[^"]*"$' | tr -d '"')
ZIP_FILE="$OUT_DIR/allure-report-$VERSION-$RUNTIME.zip"
rm -f "$ZIP_FILE"

echo "Creating $ZIP_FILE..."
(cd "$PUBLISH_DIR" && zip -r "../allure-report-$VERSION-$RUNTIME.zip" .)

echo ""
echo "Done. Install with:"
echo "  gauge install allure-report --file $ZIP_FILE"
