#!/usr/bin/env bash
# Carry on a run that stopped before it finished (a usage limit, a crash), from its own session: the same
# home, folder, model, effort and tools, so nothing it did is redone. Its output goes to the next
# transcript-<n>.jsonl beside the first.
#
#   bash tests/evals/tools/resume-run.sh <run folder> <arm>
set -euo pipefail
RUN="$1"; ARM="$2"
MODEL="${EVAL_MODEL:-claude-opus-5-5}"; EFFORT="${EVAL_EFFORT:-medium}"
SID="$(python3 - "$RUN/transcript.jsonl" <<'PY'
import json, sys
for line in open(sys.argv[1]):
    try: e = json.loads(line)
    except ValueError: continue
    if e.get("type") == "system" and e.get("subtype") == "init": print(e["session_id"]); break
PY
)"
n=2; while [ -f "$RUN/transcript-$n.jsonl" ]; do n=$((n+1)); done
TOOLS='Read,Edit,Write,Glob,Grep,TodoWrite,Bash(dotnet *),Bash(python3 *),Bash(ls *),Bash(mkdir *),Bash(cat *),Bash(find *),Bash(grep *),Bash(rm *),Bash(cp *),Bash(mv *)'
[ "$ARM" = with-proven ] && TOOLS="$TOOLS,Skill,Agent,Task"
cd "$RUN/work"
env -i PATH="$PATH" HOME="$RUN/home" DOTNET_CLI_HOME="$RUN/home" NUGET_PACKAGES="$RUN/home/.nuget/packages" \
  ANTHROPIC_BASE_URL="${ANTHROPIC_BASE_URL:-}" HTTPS_PROXY="${HTTPS_PROXY:-}" HTTP_PROXY="${HTTP_PROXY:-}" \
  NO_PROXY="${NO_PROXY:-}" SSL_CERT_FILE="${SSL_CERT_FILE:-}" NODE_EXTRA_CA_CERTS="${NODE_EXTRA_CA_CERTS:-}" \
  REQUESTS_CA_BUNDLE="${REQUESTS_CA_BUNDLE:-}" \
  claude -p "The run stopped on a usage limit. Carry on with the task from where you stopped; don't redo work that's done. Any agent that was cut off: launch it again." \
    --resume "$SID" --model "$MODEL" --effort "$EFFORT" --allowedTools "$TOOLS" \
    --output-format stream-json --verbose > "$RUN/transcript-$n.jsonl" 2>> "$RUN/stderr.txt" || echo "claude exited $? (resume $n)" >> "$RUN/stderr.txt"
echo "$RUN/transcript-$n.jsonl"
