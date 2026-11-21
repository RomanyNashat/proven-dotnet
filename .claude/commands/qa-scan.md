---
name: qa-scan
description: "Check a finished branch, story or not: criteria and edges proven by tests, mutation on changed lines, what to retest, a verdict."
argument-hint: "[path to story.md] [--base origin/main]"
allowed-tools: Read, Grep, Glob, Bash, mcp__proven-roslyn__workspace_status, mcp__proven-roslyn__find_callers, mcp__proven-roslyn__find_tests_for_symbol
---

Read-only: this command runs the tests and reads the code, and never edits either. It judges a story's
branch on evidence, with the scripts of `test-design` and `mutation-testing`. `S` below is
`~/.claude/skills/test-design/scripts`, and `M` is `~/.claude/skills/mutation-testing/scripts`.

1. **Find the story and its tests.** `story.md` is the argument. Without one, look for a `story.md` the
   branch changed (`git diff --name-only <base>`), then one in the test projects. **No story at all** is
   not a stop: infer the branch's criteria from its diff and what the code around it enforces
   (`test-design`, section 1), list them in the report marked inferred, and run steps 3 to 5 on the
   branch. The verdict is CAN'T SAY, since no test names an inferred criterion yet: hand the list to
   `qa-engineer`, which writes them into `story.md` with their tests. Edge cases (`EC` lines) are traced
   with the criteria.
2. **Are the criteria proven?**
   ```bash
   dotnet test <test project> --logger "trx;LogFileName=qa.trx" --results-directory .qa
   python "$S/trace_matrix.py" story.md <test folder> --trx .qa/qa.trx --json > .qa/trace.json
   ```
3. **Would the tests notice a wrong line?** Stryker on the branch, scored on the lines it changed
   (`mutation-testing`):
   ```bash
   cd <test project> && ~/.dotnet/stryker/dotnet-stryker --since:<base> --reporter json --output ../.qa/stryker
   python "$M/mutation_report.py" .qa/stryker/reports/mutation-report.json --since <base> --json > .qa/mutation.json
   ```
   If Stryker can't be installed or run here, say so and go on without it: the verdict will be CAN'T SAY,
   never YES.
4. **What else to retest.** `python "$S/changed_members.py" --since <base>` lists the members the branch
   changed. For each one, `find_callers` gives the code that depends on it, and `find_tests_for_symbol`
   the tests that already cover those callers (text search only if Roslyn isn't there, as `core.md` says).
   A caller outside the story with no test is a retest item.
5. **The cases that go wrong most often here.** For each that applies to the story, name the test that
   covers it, or list it as a gap:
   - the clinic's date against UTC midnight;
   - Hijri against Gregorian;
   - Arabic text against a length limit;
   - a double submit, or two requests at once;
   - another person's id;
   - an old app version;
   - an empty setting;
   - a dependency down.

   The answer is a test's name, not an opinion.
6. **The verdict:**
   ```bash
   python "$S/qa_verdict.py" --trace .qa/trace.json --mutation .qa/mutation.json [--accept story.accepted.txt]
   ```
   - **YES:** every criterion is proven, and every survivor on the changed lines is accepted with a reason
     in `story.accepted.txt` (`File.cs:19 why`). The developer writes that file; this command never does.
   - **NO:** it names each reason.
   - **CAN'T SAY:** evidence is missing.

## Report
- **The verdict line, first.**
- **The criteria:** each one with its status and tests.
- **The changes no test noticed:** file and line, what the code was and what it became, and the test that
  would catch it. A missed edge needs a boundary test; an outcome nobody asserts needs an assertion on the
  state or the side effect.
- **The retest list:** each caller outside the story, and whether a test covers it.
- **The gaps** from step 5, each with the case to add.
- **What wasn't measured,** and why.

Leave `.qa/` out of git (it is output). Don't fix anything: the gaps go to `qa-engineer`, the code to
`tdd-guide`.
