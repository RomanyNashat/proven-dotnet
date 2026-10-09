# Handover — the 17-section template

Reference for the `handover` skill. Read this when you are generating the document; the skill body
covers when and how to gather the material.

## Team pre-fills

A team layer can add a `handover-<team>` skill (for example `handover-acme`) with per-section pre-fills:
its services, dashboards and thresholds, work week, escalation contacts, standing warnings. If one is
installed, read it before generating and apply its notes section by section. Without one, every section
is filled only from the sources below.

## 4. The 17-Section Template

This is the complete section specification. Each section includes: what goes in it,
where the data comes from, and how to populate it from the available sources.

### Section 1: General Information

**Purpose:** Vacation dates, contact info, contact policy.
**Primary source:** Interactive (Step 0 — vacation dates question)
**Secondary source:** `non-code-context.md` → Out-of-Office Plan section

**Content:**

| Field | Source |
|-------|--------|
| Name | Developer's name (ask if not known) |
| Role | Ask or infer from project context |
| Team | Ask or infer |
| Vacation Period | From Step 0 interactive config |
| Last Working Day | From Step 0 |
| First Day Back | From Step 0 |
| Emergency Contact | Ask or from non-code-context.md |
| Contact Policy | Ask or from non-code-context.md; default: "P0/P1 production incidents only" |

### Section 2: Handover Distribution Matrix

**Purpose:** Who covers what service/area. Primary and secondary backups.
**Primary source:** `non-code-context.md` → Handover Distribution section
**Secondary source:** Interactive

**Content:** Table with Service/Area, Primary Backup, Secondary Backup, Notes.

**Scoped mode:** Only list the selected services. Ignore all other services.

### Section 3: Active Tickets / Tasks

**Purpose:** All in-progress work with status, priority, handover assignments, and context.
**Primary source:** Git (unmerged branches) + Interactive enrichment
**Secondary source:** ADRs (in-progress) for investigation-only work

**Population logic:**
1. Each unmerged branch becomes a row
2. Branch name → infer ticket ID (from `feature/<ticket>-...` pattern)
3. Commit summary → Notes/Context column
4. Developer's interactive answer → additional context, reasoning, blockers
5. Branch status (from interactive) → Status column mapping:
   - active → "In Progress"
   - paused → "In Progress (Paused — [reason])"
   - blocked → "Blocked — [blocker details]"
   - abandoned → "Abandoned — [reason]" (or exclude, per developer preference)
6. In-progress ADRs with no corresponding branch → add as investigation/decision tasks

**Scoped mode:** Only include branches matching selected services (see §1.5.2). ADRs also filtered to selected services.

### Section 4: Services Owned + Documentation References

**Purpose:** Repository links, documentation, dashboards, API specs, known issues per service.
**Primary source:** `non-code-context.md` → Services Owned section
**Secondary source:** Interactive

**Scoped mode:** Only list the selected services and their documentation.

### Section 5: Scheduled Jobs & Background Processes

**Purpose:** Quartz jobs, Kafka consumers, K8s workers, cron jobs — schedules, monitoring, restart procedures.
**Primary source:** `non-code-context.md` → Scheduled Jobs section
**Secondary source:** Interactive

**Scoped mode:** Only include jobs/consumers/workers that belong to selected services.

### Section 6: Monitoring & Dashboards

**Purpose:** Dashboard links, what "normal" looks like, alert thresholds, active/silenced alerts.
**Primary source:** `non-code-context.md` → Monitoring section
**Secondary source:** Interactive

**Scoped mode:** Only include dashboards and alerts for selected services. Shared infrastructure dashboards (PostgreSQL, Redis, Kafka) are included if any selected service depends on them.

### Section 7: On-Call / Incident Response

**Purpose:** Rotation status, coverage, runbook, recent incidents, response cheat sheet.
**Primary source:** `non-code-context.md` → On-Call section
**Secondary source:** Interactive

**Scoped mode:** Focus on-call coverage and incident context on selected services only.

### Section 8: Environment & Deployment Notes

**Purpose:** Recent deployments, feature flags, Docker/K8s state, environment-specific notes, scheduled deploys.
**Primary source:** `non-code-context.md` → Environment Notes section + Git (recently merged branches)
**Secondary source:** Interactive

**Population from git:** Recently merged branches (from Step 1) populate the "Recent deployments"
table with date, service (inferred from branch name/commits), and what changed.

**Scoped mode:** Only include deployments, flags, and environment notes for selected services. Filter recently merged branches by service match.

### Section 9: Pending PRs / Code Reviews

**Purpose:** Open PRs (yours and ones you're reviewing), status, action needed, reviewer assignment.
**Primary source:** Git (unmerged branches that likely have open PRs)
**Secondary source:** Interactive

**Population logic:**
1. Each unmerged branch with commits → potential open PR
2. Ask developer: "Does branch [X] have an open PR? What's its status?"
3. Also ask: "Are you reviewing any PRs for others that need handover?"

**Scoped mode:** Only include PRs for branches matching selected services.

### Section 10: Pending Communications / Emails

**Purpose:** Emails sent awaiting response, emails needing reply, ongoing threads to monitor.
**Primary source:** `non-code-context.md` → Pending Communications section
**Secondary source:** Interactive

**Three sub-tables:** Sent-Awaiting, Need-Reply, Monitor-Threads.

### Section 11: Recurring Meetings & Ceremonies

**Purpose:** Meeting schedules, your role, who covers, meetings to cancel.
**Primary source:** `non-code-context.md` → Recurring Meetings section
**Secondary source:** Interactive

### Section 12: Upcoming Deadlines or Releases

**Purpose:** Sprint ends, releases, external commitments — dates, owners, status.
**Primary source:** `non-code-context.md` → Upcoming Deadlines section
**Secondary source:** Interactive

### Section 13: Escalation Path

**Purpose:** Who to contact for what issue type, in priority order.
**Primary source:** `non-code-context.md` → Escalation Contacts section
**Secondary source:** Interactive

**Scoped mode:** Only include escalation paths for selected services.

### Section 14: Access & Credentials Notes

**Purpose:** Where to find credentials (NOT the credentials themselves). System access references.
**Primary source:** `non-code-context.md` → Access & Credentials section
**Secondary source:** Interactive

### Section 15: Known Risks / Watch Items

**Purpose:** Risks, their likelihood, impact, mitigation, and owner.
**Primary source:** ADRs (in-progress) + Git (stale branches) + `non-code-context.md`
**Secondary source:** Interactive

**Population logic:**
1. In-progress ADRs → each becomes a risk item (decision in flux, covering person should be aware)
2. Stale branches (> 14 days) that are active or paused → "work paused, may need attention"
3. Content from non-code-context.md monitoring section (alert thresholds approaching)
4. Anything the developer flags during interactive enrichment
5. Use risk severity indicators: 🔴 High, 🟠 Medium, 🟡 Low

**Scoped mode:** Only include risks related to selected services, their ADRs, and their stale branches.

### Section 16: Out-of-Office Communication Plan

**Purpose:** Internal/external notification checklists, auto-reply template, emergency contact policy by severity.
**Primary source:** `non-code-context.md` → Out-of-Office Plan section
**Secondary source:** Interactive

**Content:** Internal checklist (team notified, Slack status, calendar, manager, backups acknowledged),
external checklist (auto-reply, stakeholders, vendors), auto-reply template, severity → action table.

### Section 17: Return Plan / First Day Back

**Purpose:** Before-leaving checklist, first-day-back plan.
**Primary source:** `non-code-context.md` → Return Plan section
**Secondary source:** Interactive

**Before-leaving checklist:** All handover sections complete, sync meeting with backups,
tickets updated, no orphaned PRs, OOO activated, calendar blocked.

**First-day-back plan:** Block 2-3 hours, read channels, review email, sync with backups,
review incidents, check dashboards, reclaim tickets, unset OOO.

---
