#!/usr/bin/env bash
# Launches DiffHacker, captures the screen, and closes it. macOS and Linux.
#
# Evidence, not a gate. `tests/e2e` is what proves the application works; this exists so a
# human can see how WKWebView and WebKitGTK each render the same bundle. A failure here must
# never fail the build.
set -uo pipefail

HOST_EXECUTABLE="${1:?usage: screenshot.sh <host-executable> <output.png> [settle-seconds]}"
OUTPUT_PATH="${2:?usage: screenshot.sh <host-executable> <output.png> [settle-seconds]}"
SETTLE_SECONDS="${3:-8}"

mkdir -p "$(dirname "$OUTPUT_PATH")"

"$HOST_EXECUTABLE" &
HOST_PID=$!

cleanup() {
  if kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    sleep 2
    kill -9 "$HOST_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

sleep "$SETTLE_SECONDS"

case "$(uname -s)" in
  Darwin)
    # -x suppresses the shutter sound; needs a logged-in GUI session, which the hosted
    # macOS runners provide.
    screencapture -x "$OUTPUT_PATH"
    ;;
  Linux)
    # Runs under xvfb-run, so the root window is the whole virtual display.
    import -window root "$OUTPUT_PATH"
    ;;
  *)
    echo "Unsupported platform: $(uname -s)" >&2
    exit 1
    ;;
esac

echo "Screenshot written to $OUTPUT_PATH"
