#!/usr/bin/env bash
# publish-github-mtc-binaries.sh — build MTC release binaries, copy them into
# repo-root releases/MTC/<rid>, commit only those binaries, and push to GitHub.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

BUILD_FIRST=1
PUSH_AFTER_COMMIT=1
REMOTE_NAME="${MTC_GITHUB_RELEASE_REMOTE:-origin}"
COMMIT_MESSAGE="${MTC_GITHUB_RELEASE_MESSAGE:-Update interim MTC binaries}"

usage() {
  cat <<'EOF'
Usage: ./publish-github-mtc-binaries.sh [--no-build] [--no-push] [--message TEXT] [--remote NAME]

Builds the standalone MTC release binaries for:
  - osx-arm64
  - osx-x64
  - win-x64

Copies them into:
  - releases/MTC/osx-arm64/MTC
  - releases/MTC/osx-x64/MTC
  - releases/MTC/win-x64/MTC.exe

Then stages only those three binaries, creates one git commit, and pushes the
current branch to the selected remote.

Options:
  --no-build       Reuse the current Source/bin/MTC outputs.
  --no-push        Create the commit but do not push it.
  --message TEXT   Commit message to use.
  --remote NAME    Git remote to push to (default: origin).
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      BUILD_FIRST=0
      shift
      ;;
    --no-push)
      PUSH_AFTER_COMMIT=0
      shift
      ;;
    --message)
      if [[ $# -lt 2 ]]; then
        echo "Missing value for --message" >&2
        exit 1
      fi
      COMMIT_MESSAGE="$2"
      shift 2
      ;;
    --remote)
      if [[ $# -lt 2 ]]; then
        echo "Missing value for --remote" >&2
        exit 1
      fi
      REMOTE_NAME="$2"
      shift 2
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

CURRENT_BRANCH="$(git -C "${REPO_ROOT}" branch --show-current)"
if [[ -z "${CURRENT_BRANCH}" ]]; then
  echo "Not on a named git branch. Refusing to publish interim binaries." >&2
  exit 1
fi

if ! git -C "${REPO_ROOT}" remote get-url "${REMOTE_NAME}" >/dev/null 2>&1; then
  echo "Git remote '${REMOTE_NAME}' does not exist." >&2
  exit 1
fi

if [[ ${BUILD_FIRST} -eq 1 ]]; then
  "${SCRIPT_DIR}/build-mtc.sh"
fi

install_release_binary() {
  local src="$1"
  local dest="$2"
  local tmp="${dest}.tmp.$$"

  mkdir -p "$(dirname "${dest}")"
  rm -f "${tmp}"
  cp "${src}" "${tmp}"
  chmod 755 "${tmp}" 2>/dev/null || true
  mv -f "${tmp}" "${dest}"
}

copy_release_binary() {
  local rid="$1"
  local bin_name="$2"
  local src="${SCRIPT_DIR}/bin/MTC/${rid}/${bin_name}"
  local dest="${REPO_ROOT}/releases/MTC/${rid}/${bin_name}"

  if [[ ! -f "${src}" ]]; then
    echo "Missing built binary: ${src}" >&2
    exit 1
  fi

  install_release_binary "${src}" "${dest}"
  echo "==> Synced ${rid}: ${dest}"
}

copy_release_binary "osx-arm64" "MTC"
copy_release_binary "osx-x64" "MTC"
copy_release_binary "win-x64" "MTC.exe"

git -C "${REPO_ROOT}" add --force -- \
  "releases/MTC/osx-arm64/MTC" \
  "releases/MTC/osx-x64/MTC" \
  "releases/MTC/win-x64/MTC.exe"

if git -C "${REPO_ROOT}" diff --cached --quiet -- \
  "releases/MTC/osx-arm64/MTC" \
  "releases/MTC/osx-x64/MTC" \
  "releases/MTC/win-x64/MTC.exe"; then
  echo "No interim MTC binary changes to commit."
  exit 0
fi

git -C "${REPO_ROOT}" commit -m "${COMMIT_MESSAGE}"

if [[ ${PUSH_AFTER_COMMIT} -eq 1 ]]; then
  git -C "${REPO_ROOT}" push "${REMOTE_NAME}" "${CURRENT_BRANCH}"
else
  echo "Commit created on ${CURRENT_BRANCH}; push skipped (--no-push)."
fi
