# Per-project skills

These skills are **not installed globally**. The installer copies only `.claude/`, so they stay out of
`~/.claude/skills/` and don't spend the global skill-description budget (past it, descriptions get cut
and the wrong skill fires).

They cover technologies that only **some services** use, so they belong in those repos only.

| Skill | Use it in a service that… |
|-------|---------------------------|
| `cassandra` | reads/writes Apache Cassandra |
| `fhir` | integrates FHIR (healthcare interoperability) |
| `bigquery` | reads/writes Google BigQuery (reporting/analytics) |
| `firebase` | sends FCM push / uses Firestore |
| `signalr` | hosts SignalR real-time hubs (needs the Redis backplane past one pod) |
| `nats` | uses NATS pub/sub messaging |
| `rabbitmq-patterns` | uses RabbitMQ (MassTransit v9 is commercial, see the skill) |
| `cap-library` | uses DotNetCore.CAP (turnkey outbox + event bus) |
| `cicd-github-actions` | builds on GitHub Actions |

**What's tested.** Most of these now show code that runs in CI against real dependencies, with story and
production tests, like the installed skills:

| Skill | Tested against |
|-------|----------------|
| `signalr` | two in-process pods and Redis |
| `nats` | NATS 2.10 |
| `rabbitmq-patterns` | RabbitMQ 4.1 |
| `cap-library` | PostgreSQL and RabbitMQ 4.1 |
| `fhir` | the Firely SDK in-process (messaging, parsing, FHIRPath) |
| `cicd-github-actions` | actionlint and shellcheck (valid workflows, not a real deploy) |
| `bigquery`, `firebase` | **not tested:** they need the Google services; the code is reviewed against the SDKs |
| `cassandra` | no code: modelling guidance only |

## How to use one

Copy the skill folder into the target repo's project-level skills directory:

```bash
cp -r project-skills/cassandra  /path/to/your-service/.claude/skills/
```

```powershell
Copy-Item -Recurse project-skills\cassandra C:\path\to\your-service\.claude\skills\
```

Claude Code loads a repo's `.claude/skills/` when you work in that repo, so the skill is available there
and only there. To stop using one, delete its folder from that repo.
