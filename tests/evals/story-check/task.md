`Referrals/` holds a service, `src/Referrals/`, and two stories about to be built in it, `stories/refer.md` and `stories/withdraw.md`. Nothing has been built for them yet beyond what is in `src/`.

Before the team starts, check each story: which cases do the story and the code leave open, so that someone has to decide them before we build? Look at these cases for each story: `empty` (nothing there yet), `maximum` (the most of something), `failure` (a dependency slow or down), `permission` (who may and who may not), `duplicates` (the same request twice, or two at once), `interruption` (stopped halfway), `existing-data` (records from before this story), `language` (Arabic and English) and `touches` (what else depends on what this changes).

For each story write `Referrals/.qa/<story>.json` (`refer.json`, `withdraw.json`), with one entry per case:

```json
{"items": [{"item": "empty", "verdict": "gap", "question": "the question to ask"},
           {"item": "maximum", "verdict": "stated", "quote": "the story's words"},
           {"item": "failure", "verdict": "code", "where": "src/Referrals/File.cs:12", "snippet": "the code"},
           {"item": "interruption", "verdict": "n/a", "reason": "why it doesn't apply"}]}
```

A case is a `gap` only when nobody has decided it yet: don't ask what the story or the code already settles. Don't change anything in `src/` or `stories/`.
