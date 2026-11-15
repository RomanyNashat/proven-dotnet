# Appointment reminders: push through the broker, or poll?

Criteria and weights written before any option was scored.

| Criterion | Weight | Broker push | App polls |
|---|---|---|---|
| Reminder on time | 5 | 5 | 3 |
| Works on old app versions | 4 | 2 | 5 |
| Load on the API | 3 | 5 | 2 |
| Ops the team already runs | 2 | 3 | 5 |

- Reminder on time: push is seconds; polling every 15 minutes can be 15 late.
- Old app versions: polling works on every installed version; push needs the new SDK in the app.
