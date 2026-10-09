# story-engine — evals

Mechanical checks only (see `skill-evals`). Each case gives the answers to the up-front questions, so the
run produces a map without stopping to ask. A subagent runs each case with the skill and without it
(the always-loaded rules still apply).

**Banned words** (checked in every case, in the generated text, not in code identifiers): leverage,
utilize, facilitate, robust, seamless, comprehensive, holistic, streamline, surface(s), emit(s),
hardening, drill into, drill-down, one-shot, fan(s) out, bubble up, paints, synthetic, preamble.

## Inputs

### Code A — a reports controller (cases 3 and 5)
```csharp
[ApiController]
[Route("api/tournaments/{tournamentId:int}/reports")]
[Authorize(Policy = "TournamentAdmin")]
public sealed class TournamentReportsController(ITournamentReportService reports) : ControllerBase
{
    /// <summary>Joined / withdrawn / invited / viewer counts and the acceptance rate.</summary>
    [HttpGet("funnel")]
    public async Task<ActionResult<FunnelDto>> GetFunnel(int tournamentId, CancellationToken ct)
    {
        var result = await reports.GetFunnelAsync(tournamentId, ct);   // calls sp_GetTournamentFunnel
        return result switch
        {
            { IsNotFound: true } => NotFound(),
            { IsNotStarted: true } => Conflict(),   // the tournament hasn't started: no data yet
            _ => Ok(result.Value)
        };
    }

    [HttpGet("participants")]
    public async Task<ActionResult<PagedResult<ParticipantDto>>> GetParticipants(
        int tournamentId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100) return BadRequest();
        var result = await reports.GetParticipantsAsync(tournamentId, page, pageSize, ct);   // sp_GetTournamentParticipants
        if (result is null) return NotFound();
        return Ok(result);   // empty Items when the page is past the end
    }
}
```

### Code B — a consumer and its ADR (case 4)
```csharp
public sealed class TournamentCompletedConsumer(
    IConsumer<string, string> consumer, INotificationSender sender) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        consumer.Subscribe("tournament-completed");
        while (!ct.IsCancellationRequested)
        {
            var message = consumer.Consume(ct);
            var completed = JsonSerializer.Deserialize<TournamentCompleted>(message.Message.Value)!;
            foreach (var chunk in completed.ParticipantIds.Chunk(500))   // Firebase takes 500 tokens per request
            {
                await sender.SendPushAsync(chunk, "tournament_completed", ct);
            }

            consumer.Commit(message);   // not committed if a send throws: the event is read again
        }
    }
}
```
ADR-007 (notifications through Kafka): the tournament service publishes `tournament-completed` to Kafka and
the notification service consumes it. Rejected: RabbitMQ (no cluster here), a direct HTTP call from the
tournament service (it would tie the tournament API's latency to Firebase). Consequence: consumer lag must
be watched; a failed send is retried by not committing the offset.

### Code C — a withdraw endpoint (case 7)
```csharp
[HttpDelete("api/tournaments/{tournamentId:int}/participants/me")]
[Authorize]
public async Task<IActionResult> Withdraw(int tournamentId, CancellationToken ct)
{
    var tournament = await db.Tournaments.FindAsync([tournamentId], ct);
    if (tournament is null) return NotFound();
    if (tournament.EndsAt < DateTime.UtcNow) return BadRequest("Tournament ended");

    var row = await db.Participants.FirstOrDefaultAsync(p => p.TournamentId == tournamentId && p.UserId == User.GetUserId(), ct);
    db.Participants.Remove(row!);
    await db.SaveChangesAsync(ct);
    return NoContent();
}
```

## Cases

Every prompt starts with: *Up-front answers: 1 epic, one story per component, title tag `[BE]`, no
estimates. Don't ask questions; produce the result.*

### Case 1: requirements, simple style
**Prompt:** Style: simple. Markdown. Requirements: admins can download a tournament's participant list as a CSV file; a participant can withdraw from a tournament before it ends; every night a job closes tournaments whose end date has passed and stops counting their points.
**Must:** one epic with a 2–3 line description; three stories (the job is its own story); no
"As a / I want / so that" sentence; every story has Given/When/Then scenarios; every sub-task title starts
with `[BE]`; no banned word.
**Must not:** a story whose actor is the system, the platform or the service itself; a Task under a story.

### Case 2: requirements, full style, with conventions
**Prompt:** Style: full. Markdown. Requirements: patients can list their own appointments (paginated, newest first), book an appointment in a free slot, and cancel one up to 24 hours before it. Every endpoint needs a valid token; lists are paginated with page and pageSize; errors come back as ProblemDetails.
**Must:** "As a / I want / so that" with a real actor on each story; sub-tasks as a title plus a body; the
token, paging and error conventions stated once as shared facts at the top.
**Must not:** a story for the token, paging or error convention; an actor that is the system, platform,
service or a UI; a banned word.

### Case 3: code to stories
**Prompt:** Style: simple. Markdown. Write the story map for this code: (Code A).
**Must:** a story per endpoint; scenarios for 200, 404 and 409 on the funnel endpoint, and for 200, 400,
404 and the empty page past the end on the participants endpoint; the `funnel` route kept as a
reference while the story text says what it returns in plain words; `GetFunnelAsync` and the stored
procedure names only in sub-tasks.
**Must not:** a method, controller or stored procedure name in a story's text; the word "funnel" used as
the only description.

### Case 4: code and an ADR to stories
**Prompt:** Style: full. Markdown. Write the story map for this consumer and its ADR: (Code B).
**Must:** a real actor (the participant, or a named service acting on something else); the ADR's
reasoning attached to that story as design rationale (Kafka over RabbitMQ and over a direct HTTP call);
the 500-per-request chunking in a sub-task.
**Must not:** "As a system", "As the system" or "As the notification service" describing itself.

### Case 5: Jira CSV
**Prompt:** Style: simple. Produce only the Jira CSV for this code: (Code A).
**Must:** columns `Issue id`, `Issue Type`, `Summary`, `Parent`, `Epic Name`, `Description`; each story's
`Parent` is the epic's id and each sub-task's `Parent` its story's id; `Epic Name` set on the epic only;
epic and story summaries start with `[BE]`; sub-task summaries start with `[BE]`; multi-line descriptions
quoted.
**Must not:** a sub-task whose parent is the epic; a `Task` issue type.

### Case 6: push to Jira
**Prompt:** Style: simple. Write the story map for this code and push it to our Jira project HLTH: (Code A). (No Jira connector is available in this session.)
**Must:** says the push isn't available here and gives the CSV (or Markdown) instead, with the
External System Import note.
**Must not:** claim anything was created in Jira; invent issue keys.

### Case 7: a bug in the code
**Prompt:** Style: simple. Markdown. Write the story map for this code: (Code C).
**Must:** flags that a caller who never joined makes `Remove(null)` throw (a 500 today), as a likely bug,
instead of writing it as intended behaviour; scenarios for 204, 404 and 400.
**Must not:** a scenario that states the 500 (or a 204 for a non-member) as the expected result.

## Results

### 2026-10-05 — first run: with the skill (v1.0.0) vs without

One fresh subagent per case and condition (14 runs). Without the skill, the always-loaded rules still
applied.

| Case | With the skill | Without |
|---|---|---|
| 1 requirements, simple | pass | **fail**: `As the platform` on the nightly job; an As-a sentence in simple style; sub-tasks without `[BE]` |
| 2 requirements, full | pass | **fail**: sub-tasks as checkbox bullets, not title plus body (the conventions were stated once, correctly) |
| 3 code to stories | pass | **fail**: the stored procedure name in each story (`Data: sp_GetTournamentFunnel`); As-a sentences in simple style |
| 4 consumer and ADR | pass | **fail**: four stories invented for one component (producer, monitoring); `As the notification service`; RabbitMQ missing from the rationale; the 500 limit as a story |
| 5 Jira CSV | pass | **fail**: no `Epic Name`, other column names, no sub-tasks |
| 6 push to Jira | pass | **fail**: refused the push correctly, but pointed to the in-project importer, which drops the hierarchy |
| 7 a bug in the code | pass | pass (flagged the `Remove(null)` 500 in a sub-task) |

With 7/7, without 1/7. Unlike `design-patterns`, `api-design` and `efcore-patterns`, this skill carries
things the model can't know: the format (simple versus full, `[BE]`, shared facts), the Jira import
path and its columns, and the rule that the code drives the hierarchy. No banned word appeared in either
condition.

### 2026-10-05 — v1.1.0: contradictions removed, rerun with the skill

Reading the skill for these cases found text that contradicted itself: §2.1 required an As-a sentence and
header-plus-body sub-tasks in every story, while §1.5 (and `simplicity`) drop both in simple mode; the
§4.4 example story was `As a system`, which §1.5 bans; §7.3 described a Jira push that §8 and `/stories`
say doesn't exist; the CSV example left `[BE]` off the epic and story; "preamble" was used and banned. The
v1.0.0 runs weren't hurt by any of these (§1.5 won each time). The same contradictions were in
`story-review` (which rewrote one-line simple sub-tasks into header-plus-body blocks), `business-analyst`
and `code-analyst`; all made mode-aware.

Rerun of all seven cases with v1.1.0: 7/7, no banned word, no system actor. No case was changed.
