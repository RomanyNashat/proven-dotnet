#!/usr/bin/env bash
# One fix-eval run in a clean environment: a home folder holding only the arm's part of the harness, at a
# given git ref of this repo, and one `claude -p` with the fixture's task, word for word.
#
#   bash tests/evals/tools/run-round.sh <fixture> <arm> <ref> <run folder>
#     fixture   a folder under tests/evals with task.md and fixture.json (fix-sync, fix-labs, ...)
#     arm       with-proven | rules-only
#     ref       the git ref whose .claude/ is installed (main, a branch, a commit)
#     run folder an empty folder outside the repo; the service copy, the transcript and usage land there
#
# Model and effort are named, never taken from the CLI's defaults (round five's lesson): EVAL_MODEL
# (default claude-opus-5-5) and EVAL_EFFORT (default medium, as rounds four and five ran).
set -euo pipefail
FIXTURE="$1"; ARM="$2"; REF="$3"; RUN="$4"
REPO="$(cd "$(dirname "$0")/../../.." && pwd)"
MODEL="${EVAL_MODEL:-claude-opus-5-5}"; EFFORT="${EVAL_EFFORT:-medium}"
SRC="$REPO/tests/evals/$FIXTURE"
SERVICE="$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['service'])" "$SRC/fixture.json")"
[ -d "$RUN" ] && [ -n "$(ls -A "$RUN")" ] && { echo "run folder $RUN is not empty"; exit 1; }
mkdir -p "$RUN/home" "$RUN/work"

# The harness at REF, from a throwaway worktree, installed with the layer tool into the run's own home.
TREE="$(mktemp -d)"; trap 'git -C "$REPO" worktree remove --force "$TREE" >/dev/null 2>&1 || true' EXIT
git -C "$REPO" worktree add -q --detach "$TREE" "$REF"
python3 "$TREE/tools/layer.py" --claude-dir "$RUN/home/.claude" install "$TREE/.claude" >/dev/null
if [ "$ARM" = rules-only ]; then
  # Only the always-loaded instructions: core.md and the rule files. No skills, agents, commands or hooks.
  ( cd "$RUN/home/.claude" && find . -mindepth 1 -maxdepth 1 ! -name rules ! -name settings.json -exec rm -rf {} + )
  python3 - "$RUN/home/.claude/settings.json" <<'PY'
import json, sys
p = sys.argv[1]; s = json.load(open(p)); s.pop("hooks", None); json.dump(s, open(p, "w"), indent=2)
PY
elif [ "$ARM" != with-proven ]; then echo "arm must be with-proven or rules-only"; exit 1; fi
git -C "$REPO" rev-parse "$REF" > "$RUN/ref.txt"

cp -R "$SRC/$SERVICE" "$RUN/work/$SERVICE"
cp "$SRC/task.md" "$RUN/work/task.md"
TASK="$(cat "$SRC/task.md")"
if [ "$ARM" = with-proven ]; then
  # The harness's own route: the review command, then the TDD agent applies what it found.
  TASK="$TASK

Use /full-review on $SERVICE/ first, then have the tdd-guide agent apply its findings. Write your final list to report.md."
else
  TASK="$TASK

Write your final list to report.md."
fi

TOOLS='Read,Edit,Write,Glob,Grep,TodoWrite,Bash(dotnet *),Bash(ls *),Bash(mkdir *),Bash(cat *),Bash(find *),Bash(grep *),Bash(rm *),Bash(cp *),Bash(mv *)'
[ "$ARM" = with-proven ] && TOOLS="$TOOLS,Skill,Agent,Task"

cd "$RUN/work"
env -i PATH="$PATH" HOME="$RUN/home" DOTNET_CLI_HOME="$RUN/home" NUGET_PACKAGES="$RUN/home/.nuget/packages" \
  ANTHROPIC_BASE_URL="${ANTHROPIC_BASE_URL:-}" HTTPS_PROXY="${HTTPS_PROXY:-}" HTTP_PROXY="${HTTP_PROXY:-}" \
  NO_PROXY="${NO_PROXY:-}" SSL_CERT_FILE="${SSL_CERT_FILE:-}" NODE_EXTRA_CA_CERTS="${NODE_EXTRA_CA_CERTS:-}" \
  REQUESTS_CA_BUNDLE="${REQUESTS_CA_BUNDLE:-}" \
  claude -p "$TASK" --model "$MODEL" --effort "$EFFORT" --allowedTools "$TOOLS" \
    --output-format stream-json --verbose > "$RUN/transcript.jsonl" 2> "$RUN/stderr.txt" || echo "claude exited $?" >> "$RUN/stderr.txt"
python3 - "$RUN/transcript.jsonl" > "$RUN/usage.json" <<'PY'
import json, sys
last = None
for line in open(sys.argv[1]):
    try: e = json.loads(line)
    except ValueError: continue
    if e.get("type") == "result": last = e
print(json.dumps({k: last.get(k) for k in ("duration_ms", "num_turns", "total_cost_usd", "usage", "modelUsage", "is_error", "subtype")} if last else {}, indent=2))
PY
echo "done: $RUN"
