# Production eval: `/story-check` with and without Jira

Does `/story-check` finish with a usable result in the conditions it will meet: headless (`claude -p`), a
clean home with only the harness installed, and no Jira connector? This is not the quality eval (which
gaps it finds, which questions it shouldn't ask: S3); it checks that each road ends where it should.

| Case | Task | It passes when |
|---|---|---|
| `file` | `/story-check story.md` on the sample story (cancelling a visit) and its code | the run ends; `.qa/story-check.json` exists; `story_check.py` accepts it; the reply gives the ruling and every question word for word |
| `jira` | `/story-check PROJ-123`, no `story.md`, no Jira tool | the run ends; no report is written; the reply asks for the story's text and says no Jira tool is connected |

```bash
bash tests/evals/story-check-prod/run.sh file HEAD <empty folder>
bash tests/evals/story-check-prod/run.sh jira HEAD <empty folder>
```

The model and effort are named, never the CLI's defaults: `EVAL_MODEL` (default `claude-opus-5-5`) and
`EVAL_EFFORT` (default `medium`). Results: `results.md`.
