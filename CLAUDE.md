# Working on proven-dotnet

This file is for sessions that change this repo. It is not installed.

## Versions: X.Y.Z
- **Z:** a fix or a build step. **Y:** a finished feature or phase. **X:** only when updating needs
  something from the user (a manual step, a renamed or removed command, a changed habit).
- Until v1.0.0 the public interface can still change.

## Git
- Work on a branch, open a PR, and wait for CI to be green before merging.
- Commits carry no AI attribution trailers.

## Content rules
- The code a skill shows is tested: it lives under `tests/` and runs in CI against real dependencies,
  and the skill marks the block with `<!-- sample: path -->`.
- **The bar is production-ready, senior-level code.** Every tested skill gets two more kinds of test
  (`[Trait("Kind", "Story")]`, `[Trait("Kind", "Production")]`, see `tests/SkillSamples.Tests/Production/`):
  - a **story test**: a real situation written as Given / When / Then ("the service was down when the
    report was due; it comes back; the report runs once");
  - a **production test**: the same code under a slim image's conditions (no ICU, no tzdata, UTC). CI runs
    these in their own step, and each one checks the conditions first.
  A skill listed in `tests/story-and-production.txt` must keep both; `check-skill-samples.py` fails
  otherwise and lists the skills still to do. `tools/ci/refresh-samples.py` rewrites a skill's sample
  blocks from the tested files after a change.
- Defaults are the community's. Where teams reasonably differ (mocking library, branch model, key
  type), the rules say "unless your team's rules say otherwise" and show the trade-off.
- Nothing here names an employer, a client, an internal service or an internal host.
