#!/usr/bin/env bash
set -euo pipefail

# Usage:
#  scripts/git-commit-push-with-time.sh [branch] [message-prefix]
#
# Defaults:
#  branch: main
#  message-prefix: Auto-commit
#
# Behavior:
#  - cd -> repo root (assumed parent of scripts/)
#  - git add .
#  - git commit -a -m "<message-prefix> - <timestamp>" (only if there are changes)
#  - print commit short info and stats
#  - git push origin <branch>

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

branch="${1:-main}"
msg_prefix="${2:-Auto-commit}"
timestamp="$(date +'%Y-%m-%d %H:%M:%S %z')"

echo "Repository root: $repo_root"
echo "Branch: $branch"
echo "Commit message prefix: $msg_prefix"

# Ensure we're inside a git repo
if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "Error: not inside a git repository (cwd: $repo_root)" >&2
  exit 2
fi

status="$(git status --porcelain)"
if [ -n "$status" ]; then
  echo "Staging changes..."
  git add .

  full_msg="$msg_prefix - $timestamp"
  echo "Committing with message: $full_msg"
  git commit -a -m "$full_msg"

  echo
  echo "Commit info (short):"
  git --no-pager show --stat --pretty=format:'%h %an %ad %s' -1
else
  echo "No changes to commit. Proceeding to push current branch state."
fi

echo
echo "Pushing to origin/$branch..."
git push origin "$branch"

echo "Done."
