# /full-review on a fixture the rules don't cover — evals

The harder fixture showed the rules do most of the work: a review with the rules and no skills found
10.5 of 14. This one plants only findings the always-loaded rules don't mention: each is something a
skill teaches and its tests prove. So the gap between "with proven" and "rules only" here is the skills'
own. Run it like the others: with proven's `/full-review`, with the rules only, and with no rules at all;
grade blind.

All cases are **held out**: nothing in the commands, agents or skills was changed to pass them.

## The planted findings

| # | Where | What | Expected severity | Taught by |
|---|---|---|---|---|
| S1 | `ReminderRequestsConsumer` | `EnableAutoCommit = true` with the offset store left automatic: offsets are committed before the message is handled, so a crash or restart mid-message loses it | Critical | `kafka-patterns` |
| S2 | `Program.cs` producer | `Acks.Leader` and no `EnableIdempotence`: a broker failover loses or duplicates events | Warning | `kafka-patterns` |
| S3 | `BookingService.BookAsync` | `SaveChangesAsync`, then `ProduceAsync`: a crash or a Kafka error between them leaves a booking no one hears about (dual write); needs an outbox | Critical | `outbox` |
| S4 | `SlotLock.ReleaseAsync` | Releases with a plain `KeyDeleteAsync`: when the 10 s hold expires mid-booking, the slow holder deletes the next holder's lock, and two bookings take the slot | Critical | `redis-patterns` |
| S5 | `BookingService` | `GetDoctorAsync` has no deadline (and no default deadline on the client): a hung doctors service holds the booking request open | Warning | `grpc-patterns` |
| S6 | `ErrorInterceptor` | Catches every exception, `RpcException` included, and rethrows Internal: the service's own `NotFound` reaches clients as Internal | Warning | `grpc-patterns` |
| S7 | `ClinicPrices.UpdateAsync` | Four replicas, HybridCache: `RemoveAsync` clears this pod's L1 and Redis, the other pods keep serving the old price from their L1 | Warning | `caching` |
| S8 | `ReminderSender` | `[AutomaticRetry(Attempts = 0)]` on the class, but the job is enqueued through `IReminderSender`: Hangfire reads the interface, retries anyway, and the SMS can go twice | Warning | `hangfire-patterns` |
| S9 | `VisitsHub.FollowPatient` | The client names the patient: any signed-in user can follow any patient's visit updates | Critical | `signalr` |
| S10 | `CancellationQueue` | A quorum queue with no dead-letter exchange: RabbitMQ 4 drops a message after its delivery limit, so a poison cancellation vanishes | Warning | `rabbitmq-patterns` |

Scoring: found = 1, partial = 0.5 (the spot without the failure the table describes, or the right failure
with the wrong fix), missed = 0. Out of 10.

## Distractors (correct; flagging one is a false positive)

| # | Where | Why it's right |
|---|---|---|
| D1 | `OtpRateLimiter` | The trim, count and add are one Lua script: atomic on the server |
| D2 | `PaymentEventsConsumer` | Auto-commit with `EnableAutoOffsetStore = false` and `StoreOffset` after handling: commits only handled messages; `MarkPaid` is idempotent |
| D3 | `BookingService` availability call | Has a 1 s deadline |
| D4 | `ClinicPrices.PriceAsync` | `GetOrCreateAsync` with tags: stampede-protected; the problem is the update, not the read |

## Grading

A separate agent that didn't see the runs grades all of them under shuffled letters: each planted finding
with a quote, each distractor flagged or not, and any claim about the code that's false.
