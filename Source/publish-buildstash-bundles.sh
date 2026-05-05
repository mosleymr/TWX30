#!/usr/bin/env bash
# publish-buildstash-bundles.sh — upload TWX30 platform release bundles to Buildstash
set -euo pipefail

cd "$(dirname "$0")"

usage() {
  cat <<'EOF'
Usage: ./publish-buildstash-bundles.sh [--rebuild] [--dry-run]

Uploads the platform bundle zip files from Source/bin to Buildstash.

Required environment:
  BUILDSTASH_API_KEY   Buildstash application API token (not required for --dry-run)
  BUILDSTASH_STREAM    Exact Buildstash stream name

Optional environment:
  BUILDSTASH_PLATFORM              Default platform identifier (default: generic)
  BUILDSTASH_SOURCE                Upload source identifier (default: cli-upload)
  BUILDSTASH_LABELS                Comma-separated labels applied to all uploads
  BUILDSTASH_VERSION_MAJOR         Default: current year
  BUILDSTASH_VERSION_MINOR         Default: current month
  BUILDSTASH_VERSION_PATCH         Default: current day
  BUILDSTASH_VERSION_EXTRA         Optional prerelease label
  BUILDSTASH_VERSION_META          Default: current git short SHA
  BUILDSTASH_BUILD_NUMBER          Default: YYYYmmddHHMMSS
  BUILDSTASH_NOTES_PREFIX          Optional note prefix
  DISCORD_WEBHOOK_URL              Optional Discord webhook URL for one summary post after success
  DISCORD_WEBHOOK_USERNAME         Optional Discord username override
  DISCORD_WEBHOOK_AVATAR_URL       Optional Discord avatar URL override
  DISCORD_WEBHOOK_CONTENT          Optional extra Discord message content line
  DISCORD_WEBHOOK_THREAD_ID        Optional Discord thread_id query value
  DISCORD_WEBHOOK_THREAD_NAME      Optional Discord thread_name body value

Optional per-platform overrides use rid with dashes replaced by underscores:
  BUILDSTASH_PLATFORM_osx_arm64
  BUILDSTASH_PLATFORM_osx_x64
  BUILDSTASH_PLATFORM_win_x64
  BUILDSTASH_ARCH_osx_arm64
  BUILDSTASH_ARCH_osx_x64
  BUILDSTASH_ARCH_win_x64
  BUILDSTASH_LABELS_osx_arm64
  BUILDSTASH_LABELS_osx_x64
  BUILDSTASH_LABELS_win_x64
  BUILDSTASH_NOTES_osx_arm64
  BUILDSTASH_NOTES_osx_x64
  BUILDSTASH_NOTES_win_x64
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
  ./build-release-bundles.sh
fi

STREAM="${BUILDSTASH_STREAM:-}"
if [[ -z "$STREAM" ]]; then
  echo "BUILDSTASH_STREAM is required." >&2
  exit 1
fi

if [[ $DRY_RUN -eq 0 && -z "${BUILDSTASH_API_KEY:-}" ]]; then
  echo "BUILDSTASH_API_KEY is required unless --dry-run is used." >&2
  exit 1
fi

BUILDSTASH_SOURCE_ID="${BUILDSTASH_SOURCE:-cli-upload}"
DEFAULT_PLATFORM="${BUILDSTASH_PLATFORM:-generic}"
VERSION_MAJOR="${BUILDSTASH_VERSION_MAJOR:-$(date +%Y)}"
VERSION_MINOR="${BUILDSTASH_VERSION_MINOR:-$((10#$(date +%m)))}"
VERSION_PATCH="${BUILDSTASH_VERSION_PATCH:-$((10#$(date +%d)))}"
VERSION_EXTRA="${BUILDSTASH_VERSION_EXTRA:-}"
VERSION_META="${BUILDSTASH_VERSION_META:-$(git rev-parse --short HEAD 2>/dev/null || true)}"
BUILD_NUMBER="${BUILDSTASH_BUILD_NUMBER:-$(date +%Y%m%d%H%M%S)}"
NOTES_PREFIX="${BUILDSTASH_NOTES_PREFIX:-TWX30 standalone release bundle containing MTC, TWXC, and TWXD.}"
REMOTE_URL="$(git remote get-url origin 2>/dev/null || true)"
BRANCH_NAME="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
COMMIT_SHA="$(git rev-parse HEAD 2>/dev/null || true)"
SHORT_SHA="$(git rev-parse --short HEAD 2>/dev/null || true)"
DISCORD_WEBHOOK_URL="${DISCORD_WEBHOOK_URL:-}"
DISCORD_WEBHOOK_USERNAME="${DISCORD_WEBHOOK_USERNAME:-}"
DISCORD_WEBHOOK_AVATAR_URL="${DISCORD_WEBHOOK_AVATAR_URL:-}"
DISCORD_WEBHOOK_CONTENT="${DISCORD_WEBHOOK_CONTENT:-}"
DISCORD_WEBHOOK_THREAD_ID="${DISCORD_WEBHOOK_THREAD_ID:-}"
DISCORD_WEBHOOK_THREAD_NAME="${DISCORD_WEBHOOK_THREAD_NAME:-}"
SUMMARY_FILE="$(mktemp /tmp/buildstash-summary.XXXXXX)"
cleanup() {
  rm -f "$SUMMARY_FILE"
}
trap cleanup EXIT

if [[ "$REMOTE_URL" == git@github.com:* ]]; then
  REMOTE_URL="https://github.com/${REMOTE_URL#git@github.com:}"
fi
REMOTE_URL="${REMOTE_URL%.git}"

VC_HOST=""
VC_REPO_NAME=""
VC_COMMIT_URL=""
if [[ "$REMOTE_URL" == https://github.com/* ]]; then
  VC_HOST="github"
  VC_REPO_NAME="${REMOTE_URL#https://github.com/}"
  if [[ -n "$COMMIT_SHA" ]]; then
    VC_COMMIT_URL="${REMOTE_URL}/commit/${COMMIT_SHA}"
  fi
fi

get_override() {
  local prefix="$1"
  local rid="$2"
  local fallback="$3"
  local key="${prefix}_${rid//-/_}"
  printf '%s' "${!key:-$fallback}"
}

default_arch_for_rid() {
  case "$1" in
    osx-arm64) printf '%s' "arm64v8" ;;
    osx-x64) printf '%s' "x64" ;;
    win-x64) printf '%s' "x64" ;;
    *) printf '%s' "x64" ;;
  esac
}

default_labels_for_rid() {
  case "$1" in
    osx-arm64) printf '%s' "release-bundle,macos,osx-arm64" ;;
    osx-x64) printf '%s' "release-bundle,macos,osx-x64" ;;
    win-x64) printf '%s' "release-bundle,windows,win-x64" ;;
    *) printf '%s' "release-bundle,$1" ;;
  esac
}

RID_LIST=(
  osx-arm64
  osx-x64
  win-x64
)

for RID in "${RID_LIST[@]}"; do
  ZIP_PATH="bin/TWX30-${RID}.zip"
  if [[ ! -f "$ZIP_PATH" ]]; then
    echo "Missing bundle: $ZIP_PATH" >&2
    exit 1
  fi

  FILE_NAME="$(basename "$ZIP_PATH")"
  FILE_SIZE="$(stat -f %z "$ZIP_PATH")"
  PLATFORM="$(get_override BUILDSTASH_PLATFORM "$RID" "$DEFAULT_PLATFORM")"
  ARCH="$(get_override BUILDSTASH_ARCH "$RID" "$(default_arch_for_rid "$RID")")"
  LABELS="$(get_override BUILDSTASH_LABELS "$RID" "${BUILDSTASH_LABELS:-$(default_labels_for_rid "$RID")}")"
  NOTES="$(get_override BUILDSTASH_NOTES "$RID" "${NOTES_PREFIX} Bundle target: ${RID}. Commit: ${SHORT_SHA}.")"

  REQUEST_PAYLOAD="$(
    python3 - "$FILE_NAME" "$FILE_SIZE" "$PLATFORM" "$STREAM" "$BUILDSTASH_SOURCE_ID" "$VERSION_MAJOR" "$VERSION_MINOR" "$VERSION_PATCH" "$VERSION_EXTRA" "$VERSION_META" "$BUILD_NUMBER" "$ARCH" "$LABELS" "$NOTES" "$REMOTE_URL" "$BRANCH_NAME" "$COMMIT_SHA" "$VC_HOST" "$VC_REPO_NAME" "$VC_COMMIT_URL" <<'PY'
import json, sys

(
    filename,
    size_bytes,
    platform,
    stream,
    source_id,
    major,
    minor,
    patch,
    extra,
    meta,
    build_number,
    arch,
    labels,
    notes,
    repo_url,
    branch_name,
    commit_sha,
    vc_host,
    repo_name,
    commit_url,
) = sys.argv[1:]

payload = {
    "filename": filename,
    "structure": "file",
    "platform": platform,
    "stream": stream,
    "source": source_id,
    "version": f"{major}.{minor}.{patch}",
    "primary_file": {
        "filename": filename,
        "filetype": "zip",
        "content_type": "application/zip",
        "size_bytes": int(size_bytes),
    },
    "version_component_1_major": int(major),
    "version_component_2_minor": int(minor),
    "version_component_3_patch": int(patch),
    "custom_build_number": build_number,
    "architectures": [arch],
    "labels": [label.strip() for label in labels.split(",") if label.strip()],
    "notes": notes,
}

if extra:
    payload["version_component_extra"] = extra
if meta:
    payload["version_component_meta"] = meta
if repo_url:
    payload["vc_host_type"] = "git"
    payload["vc_repo_url"] = repo_url
if branch_name:
    payload["vc_branch"] = branch_name
if commit_sha:
    payload["vc_commit_sha"] = commit_sha
if vc_host:
    payload["vc_host"] = vc_host
if repo_name:
    payload["vc_repo_name"] = repo_name
if commit_url:
    payload["vc_commit_url"] = commit_url

print(json.dumps(payload, separators=(",", ":")))
PY
  )"

  echo "==> Buildstash upload request for ${RID}"
  if [[ $DRY_RUN -eq 1 ]]; then
    python3 - "$REQUEST_PAYLOAD" <<'PY'
import json, sys
payload = json.loads(sys.argv[1])
print(json.dumps(payload, indent=2))
PY
    continue
  fi

  REQUEST_RESPONSE="$(curl -fsS -X POST "https://app.buildstash.com/api/v1/upload/request" \
    -H "Authorization: Bearer ${BUILDSTASH_API_KEY}" \
    -H "Content-Type: application/json" \
    --data "$REQUEST_PAYLOAD")"

  REQUEST_FILE="$(mktemp /tmp/buildstash-request.XXXXXX)"
  VERIFY_FILE="$(mktemp /tmp/buildstash-verify.XXXXXX)"
  printf '%s' "$REQUEST_RESPONSE" > "$REQUEST_FILE"

  python3 - "$REQUEST_FILE" "$VERIFY_FILE" <<'PY'
import json
import sys

request_file, verify_file = sys.argv[1:]

with open(request_file, "r", encoding="utf-8") as fh:
    request_data = json.load(fh)

presigned = request_data.get("primary_presigned_data") or request_data["primary_file"]["presigned_data"]
headers = presigned.get("headers") or {}

def first(value):
    if isinstance(value, list):
        return str(value[0])
    return str(value)

upload_command = [
    "curl", "-fsS", "-X", "PUT", presigned["url"],
    "--upload-file", "__ZIP_PATH__",
]

for header_name in ("Content-Type", "Content-Length", "Content-Disposition", "x-amz-acl"):
    if header_name in headers:
        upload_command.extend(["-H", f"{header_name}: {first(headers[header_name])}"])

verify_payload = {
    "pending_upload_id": request_data["pending_upload_id"],
}

with open(verify_file, "w", encoding="utf-8") as fh:
    json.dump(
        {
            "upload_command": upload_command,
            "verify_payload": verify_payload,
        },
        fh,
    )
PY

  UPLOAD_CMD="$(
    python3 - "$VERIFY_FILE" "$ZIP_PATH" <<'PY'
import json
import shlex
import sys

verify_file, zip_path = sys.argv[1:]
with open(verify_file, "r", encoding="utf-8") as fh:
    data = json.load(fh)

parts = []
for token in data["upload_command"]:
    if token == "__ZIP_PATH__":
        token = zip_path
    parts.append(shlex.quote(token))

print(" ".join(parts))
PY
  )"

  eval "$UPLOAD_CMD" >/dev/null

  VERIFY_PAYLOAD="$(
    python3 - "$VERIFY_FILE" <<'PY'
import json
import sys

with open(sys.argv[1], "r", encoding="utf-8") as fh:
    data = json.load(fh)

print(json.dumps(data["verify_payload"], separators=(",", ":")))
PY
  )"

  VERIFY_RESPONSE="$(
    curl -fsS -X POST "https://app.buildstash.com/api/v1/upload/verify" \
      -H "Authorization: Bearer ${BUILDSTASH_API_KEY}" \
      -H "Content-Type: application/json" \
      --data "$VERIFY_PAYLOAD"
  )"
  printf '%s' "$VERIFY_RESPONSE" > "$VERIFY_FILE"

  python3 - "$VERIFY_FILE" "$RID" <<'PY'
import json, sys
data = json.load(open(sys.argv[1], "r", encoding="utf-8"))
rid = sys.argv[2]
print(f"==> Verified {rid}")
print(f"    Build ID: {data.get('build_id','')}")
print(f"    Build Info: {data.get('build_info_url','')}")
print(f"    Download: {data.get('download_url','')}")
if data.get("pending_processing"):
    print("    Pending processing: true")
PY

  python3 - "$VERIFY_FILE" "$SUMMARY_FILE" "$RID" <<'PY'
import json
import sys

verify_file, summary_file, rid = sys.argv[1:]
with open(verify_file, "r", encoding="utf-8") as fh:
    data = json.load(fh)

entry = {
    "rid": rid,
    "build_id": data.get("build_id", ""),
    "build_info_url": data.get("build_info_url", ""),
    "download_url": data.get("download_url", ""),
    "pending_processing": bool(data.get("pending_processing")),
}

with open(summary_file, "a", encoding="utf-8") as fh:
    fh.write(json.dumps(entry, separators=(",", ":")) + "\n")
PY

  rm -f "$REQUEST_FILE" "$VERIFY_FILE"
done

if [[ $DRY_RUN -eq 0 && -n "$DISCORD_WEBHOOK_URL" ]]; then
  DISCORD_PAYLOAD="$(
    python3 - "$SUMMARY_FILE" "$STREAM" "$VERSION_MAJOR" "$VERSION_MINOR" "$VERSION_PATCH" "$VERSION_EXTRA" "$VERSION_META" "$SHORT_SHA" "$DISCORD_WEBHOOK_USERNAME" "$DISCORD_WEBHOOK_AVATAR_URL" "$DISCORD_WEBHOOK_CONTENT" "$DISCORD_WEBHOOK_THREAD_NAME" <<'PY'
import json
import sys

(
    summary_file,
    stream,
    major,
    minor,
    patch,
    extra,
    meta,
    short_sha,
    username,
    avatar_url,
    content,
    thread_name,
) = sys.argv[1:]

entries = []
with open(summary_file, "r", encoding="utf-8") as fh:
    for line in fh:
        line = line.strip()
        if line:
            entries.append(json.loads(line))

version = f"{major}.{minor}.{patch}"
if extra:
    version += f"-{extra}"
if meta:
    version += f"+{meta}"

description_lines = [
    f"Stream: `{stream}`",
    f"Version: `{version}`",
]
if short_sha:
    description_lines.append(f"Commit: `{short_sha}`")

fields = []
for entry in entries:
    value = f"[Build]({entry['build_info_url']}) | [Download]({entry['download_url']})"
    if entry.get("pending_processing"):
        value += "\nProcessing: pending"
    fields.append({
        "name": entry["rid"],
        "value": value,
        "inline": False,
    })

payload = {
    "embeds": [
        {
            "title": "TWX30 Buildstash Publish",
            "description": "\n".join(description_lines),
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

  curl -fsS -X POST "$DISCORD_WEBHOOK_FINAL_URL" \
    -H "Content-Type: application/json" \
    --data "$DISCORD_PAYLOAD" >/dev/null

  echo "==> Discord notification sent"
fi
