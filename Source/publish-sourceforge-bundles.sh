#!/usr/bin/env bash
# publish-sourceforge-bundles.sh — upload standalone platform bundles to SourceForge
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
BIN_ROOT="${REPO_ROOT}/bin"

cd "${SCRIPT_DIR}"

usage() {
  cat <<'EOF'
Usage: ./publish-sourceforge-bundles.sh [--rebuild] [--dry-run]

Builds and/or uploads the platform bundle zip files from TWX30/bin to SourceForge.

Required environment:
  SOURCEFORGE_API_KEY   SourceForge Release API key

Optional environment:
  SOURCEFORGE_PROJECT    Default: twx30
  SOURCEFORGE_USERNAME   Default: mosleymr
  SOURCEFORGE_REMOTE_DIR Default: /home/frs/project/<project>
  DISCORD_WEBHOOK_URL              Optional Discord webhook URL for one summary post after success
  DISCORD_WEBHOOK_USERNAME         Optional Discord username override
  DISCORD_WEBHOOK_AVATAR_URL       Optional Discord avatar URL override
  DISCORD_WEBHOOK_CONTENT          Optional extra Discord message content line
  DISCORD_WEBHOOK_THREAD_ID        Optional Discord thread_id query value
  DISCORD_WEBHOOK_THREAD_NAME      Optional Discord thread_name body value
EOF
}

REBUILD=0
DRY_RUN=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rebuild)
      REBUILD=1
      shift
      ;;
    --dry-run)
      DRY_RUN=1
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ $REBUILD -eq 1 ]]; then
  ./build-sourceforge-bundles.sh
fi

if [[ -z "${SOURCEFORGE_API_KEY:-}" ]]; then
  echo "SOURCEFORGE_API_KEY is required." >&2
  exit 1
fi

PROJECT="${SOURCEFORGE_PROJECT:-twx30}"
USERNAME="${SOURCEFORGE_USERNAME:-mosleymr}"
REMOTE_DIR="${SOURCEFORGE_REMOTE_DIR:-/home/frs/project/${PROJECT}}"
DISCORD_WEBHOOK_URL="${DISCORD_WEBHOOK_URL:-}"
DISCORD_WEBHOOK_USERNAME="${DISCORD_WEBHOOK_USERNAME:-}"
DISCORD_WEBHOOK_AVATAR_URL="${DISCORD_WEBHOOK_AVATAR_URL:-}"
DISCORD_WEBHOOK_CONTENT="${DISCORD_WEBHOOK_CONTENT:-}"
DISCORD_WEBHOOK_THREAD_ID="${DISCORD_WEBHOOK_THREAD_ID:-}"
DISCORD_WEBHOOK_THREAD_NAME="${DISCORD_WEBHOOK_THREAD_NAME:-}"
SUMMARY_FILE="$(mktemp /tmp/sourceforge-summary.XXXXXX)"
cleanup() {
  rm -f "$SUMMARY_FILE"
}
trap cleanup EXIT

if [[ -n "${RID_LIST:-}" ]]; then
  IFS=' ' read -r -a RIDS <<< "${RID_LIST}"
else
  RIDS=(
    osx-arm64
    osx-x64
    win-x64
    linux-x64
  )
fi

api_defaults_for_rid() {
  case "$1" in
    osx-arm64) printf '%s' "default=mac" ;;
    win-x64) printf '%s' "default=windows" ;;
    linux-x64) printf '%s' "default=linux" ;;
    *) printf '%s' "" ;;
  esac
}

for rid in "${RIDS[@]}"; do
  zip_path="${BIN_ROOT}/mtc-${rid}.zip"
  if [[ ! -f "${zip_path}" ]]; then
    echo "Missing bundle: ${zip_path}" >&2
    exit 1
  fi

  file_name="$(basename "${zip_path}")"
  remote_path="${REMOTE_DIR}/${file_name}"
  api_url="https://sourceforge.net/projects/TWX30/files/${file_name}"
  default_arg="$(api_defaults_for_rid "${rid}")"
  curl_args=(
    -fsS
    -H "Accept: application/json"
    -X PUT
    -d "api_key=${SOURCEFORGE_API_KEY}"
    -d "download_label=${file_name}"
  )
  if [[ -n "${default_arg}" ]]; then
    curl_args+=(-d "${default_arg}")
  fi

  echo "==> Uploading ${file_name} to SourceForge..."
  if [[ $DRY_RUN -eq 1 ]]; then
    echo "scp -o BatchMode=yes ${zip_path} ${USERNAME}@frs.sourceforge.net:${remote_path}"
    echo "curl -H 'Accept: application/json' -X PUT -d 'api_key=<redacted>' -d 'download_label=${file_name}'${default_arg:+ -d '${default_arg}'} ${api_url}"
    continue
  fi

  scp -o BatchMode=yes "${zip_path}" "${USERNAME}@frs.sourceforge.net:${remote_path}"

  curl "${curl_args[@]}" "${api_url}" >/tmp/sourceforge-upload-response.json

  echo "==> Updated SourceForge metadata for ${file_name}"

  python3 - "$SUMMARY_FILE" "$rid" "$file_name" "https://sourceforge.net/projects/TWX30/files/${file_name}/download" <<'PY'
import json
import sys

summary_file, rid, file_name, download_url = sys.argv[1:]
entry = {
    "rid": rid,
    "file_name": file_name,
    "download_url": download_url,
}

with open(summary_file, "a", encoding="utf-8") as fh:
    fh.write(json.dumps(entry, separators=(",", ":")) + "\n")
PY
done

if [[ $DRY_RUN -eq 0 && -n "$DISCORD_WEBHOOK_URL" ]]; then
  DISCORD_PAYLOAD="$(
    python3 - "$SUMMARY_FILE" "$DISCORD_WEBHOOK_USERNAME" "$DISCORD_WEBHOOK_AVATAR_URL" "$DISCORD_WEBHOOK_CONTENT" "$DISCORD_WEBHOOK_THREAD_NAME" <<'PY'
import json
import sys

summary_file, username, avatar_url, content, thread_name = sys.argv[1:]

entries = []
with open(summary_file, "r", encoding="utf-8") as fh:
    for line in fh:
        line = line.strip()
        if line:
            entries.append(json.loads(line))

fields = []
for entry in entries:
    fields.append({
        "name": entry["rid"],
        "value": f"[{entry['file_name']}]({entry['download_url']})",
        "inline": False,
    })

payload = {
    "embeds": [
        {
            "title": "TWX30 SourceForge Release Binaries Updated",
            "description": "Standalone release bundles are live on SourceForge.",
            "fields": fields,
        }
    ]
}

if content:
    payload["content"] = content
if username:
    payload["username"] = username
if avatar_url:
    payload["avatar_url"] = avatar_url
if thread_name:
    payload["thread_name"] = thread_name

print(json.dumps(payload, separators=(",", ":")))
PY
  )"

  DISCORD_WEBHOOK_FINAL_URL="$DISCORD_WEBHOOK_URL"
  if [[ -n "$DISCORD_WEBHOOK_THREAD_ID" ]]; then
    if [[ "$DISCORD_WEBHOOK_FINAL_URL" == *\?* ]]; then
      DISCORD_WEBHOOK_FINAL_URL="${DISCORD_WEBHOOK_FINAL_URL}&thread_id=${DISCORD_WEBHOOK_THREAD_ID}"
    else
      DISCORD_WEBHOOK_FINAL_URL="${DISCORD_WEBHOOK_FINAL_URL}?thread_id=${DISCORD_WEBHOOK_THREAD_ID}"
    fi
  fi

  echo "==> Sending Discord summary..."
  curl -fsS -X POST "$DISCORD_WEBHOOK_FINAL_URL" \
    -H "Content-Type: application/json" \
    --data "$DISCORD_PAYLOAD" >/dev/null
  echo "==> Discord summary sent."
fi
