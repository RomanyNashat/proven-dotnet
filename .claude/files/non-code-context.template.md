# Non-Code Handover Context

> **Purpose:** This file captures everything that matters for a handover but doesn't live in git.
> Update it periodically — weekly or when things change. When you run `/handover`,
> Claude reads this file first and asks about anything missing or stale.
>
> **Location:** copy this file to `~/.claude/handover/non-code-context.md` and fill it in there. The
> installer never touches that copy.
>
> **How to use:** Replace the bracketed placeholders with your actual information.
> Delete sections that genuinely don't apply to your role. Leave sections empty
> if they apply but you haven't filled them yet — `/handover` will ask about them.

---

## On-Call

- **Current rotation:** [Who is on the rotation, schedule link, your slot]
- **Escalation chain:** [First contact → second contact → management]
- **Recent incidents:** [Any recent incidents the covering person should know about]
- **Runbook location:** [Link to incident response runbook]

## Recurring Meetings

- **Daily standup:** [Time, channel, what you report on, who covers for you]
- **Sprint planning:** [Schedule, your role, who covers]
- **Sprint review/demo:** [Schedule, your role, who covers]
- **Retrospective:** [Schedule, who covers]
- **1:1 with manager:** [Schedule — cancel or reschedule?]
- **Cross-team syncs:** [Schedule, your role, who covers]
- **Other:** [Any other recurring meetings]

> **Meetings to cancel while away:** [List meetings that should simply be cancelled]

## Monitoring

- **[Service name] dashboard:** [Link]
  - Normal state: [What "healthy" looks like — response times, error rates, queue depths]
  - Alert thresholds: [What triggers concern, what action to take]

- **[Service name] dashboard:** [Link]
  - Normal state: [Description]
  - Alert thresholds: [Description]

- **Active alerts to watch:** [Any currently firing or flapping alerts]
- **Silenced alerts:** [Any alerts silenced and why, when silence expires]

## Pending Communications

### Emails Sent — Awaiting Response
- **To [Name/Team]:** [Subject] — Sent [date] — Expected response by [date]
  - Context: [What this is about, what to do if they respond while you're out]

### Emails Needing a Reply
- **From [Name/Team]:** [Subject] — Received [date] — Deadline: [date]
  - Key points: [What the reply should cover]

### Ongoing Threads to Monitor
- **[Subject]** with [participants] — Watch for: [What might happen, what action to take]

## Upcoming Deadlines

- **[Date]:** [What's due — sprint end, release, external commitment]
- **[Date]:** [Description]
- **[Date]:** [Description]

## Scheduled Jobs & Background Processes

- **[Job name]:** Runs [schedule] — Does [what] — Watch for [expected behavior]
  - Restart procedure: [Command or steps]
- **[Job name]:** Runs [schedule] — Does [what]

> **Manual recurring tasks during vacation:** [Any manual tasks someone needs to do]

## Environment Notes

- **Recent deployments:** [Service, date, what changed, rollback plan]
- **Feature flags active:** [Flag name — what it controls, current state, safe to toggle?]
- **Staging notes:** [Any differences from prod, ongoing tests]
- **Production notes:** [Temporary configs, workarounds, half-deployed state]
- **Scheduled deployments during vacation:** [Date, what, who owns it]

## Services Owned

- **[Service name]:** Repo [link] | Docs [link] | Dashboard [link] | API spec [link]
  - Known issues: [Brief description or "None"]
- **[Service name]:** Repo [link] | Docs [link] | Dashboard [link]
  - Known issues: [Description]

> **Key documentation:** Architecture overview [link] | Runbooks [link] | ADRs [link]

## Escalation Contacts

| Issue Type | First Contact | Second Contact | Channel |
|-----------|--------------|----------------|---------|
| Production incident | [Name + contact] | [Name + contact] | [Slack/Teams channel] |
| [Service] issue | [Name] | [Name] | |
| Database issue | [DBA/Name] | [Name] | |
| Business question | [PM/PO Name] | [Name] | |
| Infrastructure | [SRE team] | [Name] | |

## Access & Credentials Notes

> ⚠️ Do NOT put actual passwords or secrets here. Only reference where to find them.

- **[Vault / Secret Manager]:** [Link, how to access — e.g., "Use team token"]
- **[CI/CD Pipeline]:** [Link]
- **[Cloud Console]:** [Link — e.g., "Request from SRE if needed"]
- **Access to share temporarily:** [System — who needs it, how to grant]

## Handover Distribution

| Service / Area | Primary Backup | Secondary Backup | Notes |
|----------------|---------------|-----------------|-------|
| [Service A] | [Dev Name] | [Dev Name] | |
| [Service B] | [Dev Name] | [Dev Name] | |

> Ensure each backup has acknowledged their assignment before you leave.

## Return Plan

- **Block first 2 hours** for catching up (no meetings)
- **First actions:** [What to check first — dashboards, incidents, ticket status]
- **Sync with:** [Who to talk to for a verbal summary]
- **Reclaim:** [Tickets, on-call rotation, meeting roles]

## Out-of-Office Plan

### Internal
- [ ] Team notified of vacation dates
- [ ] Slack/Teams status set to OOO with return date
- [ ] Calendar blocked as OOO
- [ ] Manager informed and handover reviewed

### External
- [ ] Email auto-reply set up (include backup contact)
- [ ] External stakeholders / vendors notified

### Emergency Contact Policy
- **P0 (system down):** Contact me via [phone/WhatsApp]
- **P1 (major degradation):** Contact [backup name] first, then me if unresolved
- **P2-P3 (non-urgent):** Wait for my return
