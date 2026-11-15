#!/usr/bin/env bash
# The production eval for /story-check: headless, in a clean home with only the harness (no Jira connector),
# on the sample story and its code. Two cases:
#   file   /story-check story.md      must end with a report story_check.py accepts
#   jira   /story-check PROJ-123      no connector: must ask for the story's text and write no report
#
#   bash tests/evals/story-check-prod/run.sh <file|jira> <ref> <empty run folder>
# Model and effort are named: EVAL_MODEL (default claude-opus-5-5), EVAL_EFFORT (default medium).
set -euo pipefail
CASE="$1"; REF="$2"; RUN="$3"
REPO="$(cd "$(dirname "$0")/../../.." && pwd)"
MODEL="${EVAL_MODEL:-claude-opus-5-5}"; EFFORT="${EVAL_EFFORT:-medium}"
[ -d "$RUN" ] && [ -n "$(ls -A "$RUN")" ] && { echo "run folder $RUN is not empty"; exit 1; }
mkdir -p "$RUN/home" "$RUN/work"
TREE="$(mktemp -d)"; trap 'git -C "$REPO" worktree remove --force "$TREE" >/dev/null 2>&1 || true' EXIT
git -C "$REPO" worktree add -q --detach "$TREE" "$REF"
python3 "$TREE/tools/layer.py" --claude-dir "$RUN/home/.claude" install "$TREE/.claude" >/dev/null
git -C "$REPO" rev-parse "$REF" > "$RUN/ref.txt"
cp -R "$REPO/tests/storycheck/fixture/." "$RUN/work/"
case "$CASE" in
  file) TASK="/story-check story.md" ;;
  jira) rm "$RUN/work/story.md"; TASK="/story-check PROJ-123" ;;
  *) echo "case must be file or jira"; exit 1 ;;
esac
TOOLS='Read,Write,Edit,Glob,Grep,Skill,Bash(python3 *),Bash(python *),Bash(ls *),Bash(mkdir *),Bash(cat *),Bash(find *),Bash(grep *)'
cd "$RUN/work"
env -i PATH="$PATH" HOME="$RUN/home" \
  ANTHROPIC_BASE_URL="${ANTHROPIC_BASE_URL:-}" HTTPS_PROXY="${HTTPS_PROXY:-}" HTTP_PROXY="${HTTP_PROXY:-}" \
  NO_PROXY="${NO_PROXY:-}" SSL_CERT_FILE="${SSL_CERT_FILE:-}" NODE_EXTRA_CA_CERTS="${NODE_EXTRA_CA_CERTS:-}" \
  claude -p "$TASK" --model "$MODEL" --effort "$EFFORT" --allowedTools "$TOOLS" \
    --output-format stream-json --verbose > "$RUN/transcript.jsonl" 2> "$RUN/stderr.txt" || echo "claude exited $?" >> "$RUN/stderr.txt"
python3 "$REPO/tests/evals/story-check-prod/grade.py" "$CASE" "$RUN" | tee "$RUN/grade.txt"
