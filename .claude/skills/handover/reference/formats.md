# Handover — format generation rules

Reference for the `handover` skill: Markdown, Teams and Notion output rules. Read this at output
time, once the content exists.

## 6. Format Generation Rules

### 6.1 Markdown (Full Document)

**Filename:** `vacation-handover.md`
**Structure:** Full 17 sections with horizontal rules between sections.

**Formatting rules:**
- Title: `# 🏖️ Vacation Handover Document` (add ` — <Team>` when the team is known)
- Section headers: `## N. Section Title`
- Tables for structured data (use `|` pipe tables)
- Callout boxes with `>` blockquotes for tips and warnings
- Emoji indicators for warnings (⚠️), tips (💡), reminders (📅)
- Status indicators in text: In Progress, Blocked, etc.
- Footer: `*Last updated: [date] by [name]*`

**Section ordering:** Exactly as numbered 1–17. No reordering.

**Empty section handling:** If a section was skipped during interactive enrichment,
include the section header with a note: `> ℹ️ This section was not populated during handover generation. Contact [developer name] for details.`

### 6.2 Teams Medium

**Filename:** `vacation-handover-teams-medium.txt`
**Structure:** Single message, all sections condensed. Designed to be pasted into a Teams channel.

**Formatting rules:**
- Header: `🏖️ VACATION HANDOVER — [Name]` with `━` separator line
- Top block: dates, emergency contact, link to full doc
- Section headers: bold with emoji prefix, e.g., `**👥 HANDOVER DISTRIBUTION**`
- Tables where they fit; bullet points (`•`) for lists
- Sub-items use `↳` indent: `  ↳ Context: [details]`
- Warnings use `⚠️` prefix
- Horizontal rules (`---`) between sections
- No markdown headers (`#`) — Teams renders plain text with bold
- Compact: omit explanatory text, keep only data
- Link to full handover doc at top AND bottom

**Section mapping — all 17 sections condensed:**

| Full Section | Teams Header | Notes |
|-------------|-------------|-------|
| 1. General Info | Top block (dates, contact) | No separate header |
| 2. Distribution | `**👥 HANDOVER DISTRIBUTION**` | Table format |
| 3. Active Tickets | `**🎫 ACTIVE TICKETS**` | Bullet list with `↳` context |
| 4. Services | `**📦 SERVICES & DOCUMENTATION**` | Bullet list with inline links |
| 5. Jobs | `**⏰ SCHEDULED JOBS**` | Bullet list, grouped by type (scheduler, consumers, workers) |
| 6. Monitoring | `**📊 MONITORING**` | Compact table: Dashboard \| Normal \| Alert |
| 7. On-Call | `**🚨 ON-CALL**` | Bullet list + cheat sheet (numbered) |
| 8. Environment | `**🚀 DEPLOYMENTS & ENVIRONMENTS**` | Inline key-value pairs |
| 9. Pending PRs | `**🔀 PENDING PRs**` | Bullet list |
| 10. Communications | `**📧 PENDING EMAILS**` | Three sub-groups: awaiting, need reply, monitor |
| 11. Meetings | `**📅 RECURRING MEETINGS**` | Bullet list with `→` coverage |
| 12. Deadlines | `**🎯 UPCOMING DEADLINES**` | Bullet list with status emoji |
| 13. Escalation | `**🆘 ESCALATION PATH**` | Compact table: Issue \| Contact → Backup |
| 14. Access | `**🔐 ACCESS NOTES**` | Bullet list of system → how to access |
| 15. Known Risks | `**⚡ KNOWN RISKS**` | Bullet list with severity emoji (🔴🟠🟡) |
| 16. OOO Plan | `**📤 OOO PLAN**` | Checklist with ✅ + emergency policy |
| 17. Return Plan | `**🔄 FIRST DAY BACK**` | One-line summary of plan |

### 6.3 Teams Ultra-Short

**Filename:** `vacation-handover-teams-short.txt`
**Structure:** Executive summary only. Designed for a quick-glance post or a Teams status message.

**Formatting rules:**
- Same header as Teams medium
- ONLY these sections (in this order):
  1. Dates + emergency contact (2 lines)
  2. Coverage matrix (bullet list: service → primary, secondary)
  3. Active tickets (bullet list: ticket → status → assigned to)
  4. Known risks (bullet list: risk → mitigation)
  5. Pending emails (bullet list: key items only)
  6. Escalation (compact: issue → contact chain)
  7. Key dashboards link (1 line)
  8. Link to full handover doc (1 line)
- Maximum ~40 lines total
- No tables — pure bullet points for maximum Teams compatibility
- No detailed context — just enough to know who to contact for what

### 6.4 Notion

**Filename:** `vacation-handover-notion.md`
**Structure:** Full 17 sections with Notion-specific formatting hints.

**Formatting rules:**
- Title: `# 🏖️ Vacation Handover Document` with usage instructions in blockquote
- Section headers: `## [emoji] N. Section Title` (each section gets a category emoji)
- Tables use `|---|---|` Notion-compatible format (minimal cell separators)
- `<details><summary>` blocks for supplementary content (alerts, recent incidents,
  cheat sheets, feature flags, Docker/K8s notes, manual tasks, access sharing)
- Status emoji in tables: 🔵 In Progress, 🟡 In Review, 🟢 Done, 🔴 Blocked, ⚪ Not Started
- Priority emoji: 🔴 P0, 🟠 P1, 🟡 P2, 🟢 P3
- Notion database hints: `> Create this as a **Notion Database (Table view)**` for
  Active Tickets, Services Owned
- Notion URL property hints: `> 💡 Tip: Convert this into a Database with URL properties`
- Checklists use `- [ ]` format (Notion renders as toggleable checkboxes)

**Section emoji mapping:**

| Section | Emoji |
|---------|-------|
| 1. General Info | 📌 |
| 2. Distribution | 👥 |
| 3. Active Tickets | 🎫 |
| 4. Services | 📦 |
| 5. Jobs | ⏰ |
| 6. Monitoring | 📊 |
| 7. On-Call | 🚨 |
| 8. Environment | 🚀 |
| 9. Pending PRs | 🔀 |
| 10. Communications | 📧 |
| 11. Meetings | 📅 |
| 12. Deadlines | 🎯 |
| 13. Escalation | 🆘 |
| 14. Access | 🔐 |
| 15. Known Risks | ⚡ |
| 16. OOO Plan | 📤 |
| 17. Return Plan | 🔄 |

**Toggle block usage:** Use `<details><summary>` for content that is supplementary
or reference-only. Primary content stays visible. Pattern:

```markdown
<details>
<summary><strong>🔔 Active Alerts to Be Aware Of</strong></summary>

- **[Alert Name]:** [What it means and what action to take]

</details>
```

---
