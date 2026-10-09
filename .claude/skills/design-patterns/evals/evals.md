# design-patterns — evals

Mechanical checks only (see `skill-evals`): does the answer reach the right verdict and name the right
thing. Quality of prose is not graded here. Run each case in a fresh subagent with the skill and again
without it.

All nine cases are **held out**: the skill was written before these cases existed and has not been
changed to pass them.

### Case 1: a pattern the developer asked for, and doesn't need
**Prompt:** We send SMS appointment reminders. Should I use the Strategy pattern for sending them?
**Setup:**
```csharp
public sealed class ReminderJob(SmsSender sms, IAppointmentRepository repo)
{
    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var a in await repo.DueTomorrowAsync(ct))
            await sms.SendAsync(a.Mobile, $"Reminder: {a.StartsAt:HH:mm}", ct);
    }
}
// SmsSender is the only sender; there are no plans for email or push.
```
**Must:** conclude that no pattern is needed now; say when it would be (a second channel, or the same
switch in several places).
**Must not:** sketch an `INotificationSender` interface with one implementation, or a factory.
**Trap:** the developer named the pattern. Agreeing is the easy answer; "no pattern needed" is the pass.

### Case 2: a strategy with one implementation and a factory that only calls `new`
**Prompt:** Explain the patterns in this code and whether they fit.
**Setup:**
```csharp
public interface IPriceCalculator { decimal Calculate(Visit visit); }
public sealed class StandardPriceCalculator : IPriceCalculator
{
    public decimal Calculate(Visit visit) => visit.BasePrice * (1 - visit.DiscountRate);
}
public sealed class PriceCalculatorFactory
{
    public IPriceCalculator Create() => new StandardPriceCalculator();
}
// Used in one place: var price = new PriceCalculatorFactory().Create().Calculate(visit);
```
**Must:** judge it over-engineered; name both the single implementation and the factory that only calls
`new`; offer the simpler shape (a method, or a class injected directly).
**Must not:** call it a good use of Strategy or Factory.

### Case 3: a lifecycle kept in booleans
**Prompt:** Explain the patterns in this code and whether anything is missing.
**Setup:**
```csharp
public sealed class Appointment
{
    public bool IsCancelled { get; set; }
    public bool IsCheckedIn { get; set; }
    public bool IsCompleted { get; set; }
}
// CheckInHandler:  if (a.IsCancelled || a.IsCompleted) return Fail(); a.IsCheckedIn = true;
// CancelHandler:   if (a.IsCheckedIn || a.IsCompleted) return Fail(); a.IsCancelled = true;
// CompleteHandler: if (!a.IsCheckedIn || a.IsCancelled) return Fail(); a.IsCompleted = true;
```
**Must:** identify a missing state machine (State); point out the flags can contradict each other and the
rules are spread over three handlers; recommend one status plus a transition table in the entity.
**Must not:** recommend a class per state as the first step.
**Trap:** textbook State is a class per state. The skill says start with a transition table.

### Case 4: things .NET already provides
**Prompt:** Explain the patterns in this code and whether they fit.
**Setup:**
```csharp
public sealed class ClinicSettingsCache
{
    private static ClinicSettingsCache? _instance;
    public static ClinicSettingsCache Instance => _instance ??= new ClinicSettingsCache();
    // ...
}
public sealed class LabClient
{
    private static readonly HttpClient Http = new();
    public async Task<string> GetAsync(string url)
    {
        for (var i = 0; i < 3; i++)
        {
            try { return await Http.GetStringAsync(url); }
            catch (HttpRequestException) { Thread.Sleep(1000 * (i + 1)); }
        }
        throw new InvalidOperationException("lab unavailable");
    }
}
```
**Must:** say these are hand-rolled versions of what .NET has; name DI singletons (`AddSingleton`) for the
first and `IHttpClientFactory` with a resilience handler (Polly) for the second; flag `Thread.Sleep`.
**Must not:** suggest improving the hand-rolled singleton (double-checked locking, `Lazy<T>`) as the fix.

### Case 5: a generic repository over EF Core
**Prompt:** Explain the patterns in this code and whether they fit.
**Setup:**
```csharp
public interface IRepository<T> where T : class
{
    IEnumerable<T> GetAll();
    T? GetById(int id);
    void Add(T entity);
}
public sealed class Repository<T>(AppDbContext db) : IRepository<T> where T : class
{
    public IEnumerable<T> GetAll() => db.Set<T>().ToList();
    public T? GetById(int id) => db.Set<T>().Find(id);
    public void Add(T entity) => db.Set<T>().Add(entity);
}
// Caller: var today = repo.GetAll().Where(a => a.ClinicId == clinicId && a.Day == today).ToList();
```
**Must:** judge it misused; say `GetAll()` loads the whole table and filters in memory; recommend
aggregate-specific repositories with intent-named methods (or `DbContext` directly for reads).
**Must not:** suggest returning `IQueryable<T>` from the repository as the fix.

### Case 6: a specification combined with `Expression.Invoke`
**Prompt:** This works in unit tests but throws against the database. What's wrong?
**Setup:**
```csharp
public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
{
    var p = Expression.Parameter(typeof(T));
    return Expression.Lambda<Func<T, bool>>(
        Expression.AndAlso(Expression.Invoke(a, p), Expression.Invoke(b, p)), p);
}
// db.Appointments.Where(Specs.ForClinic(7).And(Specs.Upcoming(now))).ToListAsync();
```
**Must:** say EF Core can't translate an invocation expression to SQL; fix by rebinding the second
expression's parameter (an `ExpressionVisitor`) into one lambda.
**Must not:** fix it by compiling the expression or calling `AsEnumerable()` before `Where`.

### Case 7: a decorator that fits
**Prompt:** Explain the patterns in this code and whether they fit.
**Setup:**
```csharp
public sealed class CachedClinicDirectory(IClinicDirectory inner, HybridCache cache) : IClinicDirectory
{
    public async Task<ClinicInfo?> FindAsync(int id, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"clinic:{id}", async t => await inner.FindAsync(id, t), cancellationToken: ct);
}
// services.AddScoped<SqlClinicDirectory>();
// services.AddScoped<IClinicDirectory>(sp => new CachedClinicDirectory(sp.GetRequiredService<SqlClinicDirectory>(), sp.GetRequiredService<HybridCache>()));
// IClinicDirectory is injected in 14 places.
```
**Must:** identify Decorator and judge that it fits (caching added without touching the 14 callers).
**Must not:** recommend removing it or replacing it with something more complex.
**Trap:** a review is expected to find faults; "fits" is a valid verdict.

### Case 8: a runtime choice that does need a strategy
**Prompt:** Pricing differs per insurer (four insurers, each with its own rules and its own HTTP client),
and the `switch (visit.InsurerCode)` is now in three services. What pattern fits?
**Must:** recommend Strategy, chosen at runtime by insurer code; show the implementations injected as a
collection and mapped by code (or keyed services with a lookup); include DI registration in the sketch.
**Must not:** resolve the strategy from `IServiceProvider` inside business code (service locator).

### Case 9: a mediator library by default
**Prompt:** Should we add MediatR to decouple our controllers from the services they call?
**Must:** not recommend MediatR by default; mention its licence (13.0+ is commercial or RPL) or that proven-dotnet
injects handlers directly; offer the plain alternative (handlers injected into endpoints, decorators for
cross-cutting concerns).
**Must not:** present MediatR as the standard answer.
**Trap:** MediatR is the most common answer on the internet. The answer here is different.

## Set B — harder cases (added 2026-10-05, after set A passed 9/9 both ways)

Each case targets a point in the skill's misuse list (§5) where the plausible answer is wrong. Held out:
the skill was not changed for them.

### Case 10: a decorator that changes what the call means
**Prompt:** Explain the patterns in this code and whether they fit.
**Setup:**
```csharp
public sealed class SafeAppointmentRepository(IAppointmentRepository inner, ILogger<SafeAppointmentRepository> log) : IAppointmentRepository
{
    public async Task<IReadOnlyList<Appointment>> ForPatientAsync(int patientId, CancellationToken ct)
    {
        try { return await inner.ForPatientAsync(patientId, ct); }
        catch (Exception ex) { log.LogError(ex, "lookup failed"); return []; }
    }
}
// Registered over the EF repository; the app's "My appointments" screen calls it.
```
**Must:** judge the decorator misused: it turns a failure into "no appointments", which the patient sees as
having none; the caller can't tell an outage from an empty list.
**Must not:** call it a good resilience decorator, or suggest only narrowing the catch.

### Case 11: an adapter that leaks the vendor's types
**Prompt:** Explain the patterns in this code and whether they fit.
**Setup:**
```csharp
public interface IPaymentGateway
{
    Task<PayFortPurchaseResponse> ChargeAsync(PayFortPurchaseRequest request, CancellationToken ct);
}
public sealed class PayFortGateway(HttpClient http) : IPaymentGateway { /* calls PayFort */ }
// PayFortPurchaseRequest/Response are the vendor's DTOs (snake_case fields, vendor status codes).
// BookingService builds PayFortPurchaseRequest and reads response.ResponseCode == "14000".
```
**Must:** judge the adapter misused (or not an adapter): the interface uses the vendor's types, so the
vendor's model and status codes leak into `BookingService`; the interface should use our own types and
the adapter translates.
**Must not:** judge it a fitting adapter because an interface exists.

### Case 12: a Unit of Work that forwards SaveChanges
**Prompt:** Explain the patterns in this code and whether they fit.
**Setup:**
```csharp
public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct); }
public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
// Handlers inject IUnitOfWork plus one repository per entity, and call SaveChangesAsync at the end.
```
**Must (revised after run B1, see Results):** say the answer depends on the layering: in Clean
Architecture (Application can't reference Infrastructure) a thin `IUnitOfWork` port is how handlers commit
and fits; where handlers can use the `DbContext` directly, the wrapper only adds a hop.
**Must not:** judge it without considering the layering.
**Original Must (run B1):** "over-engineered or built into .NET; a wrapper earns its place only when it
adds something". That was the skill's own rule, and it was wrong for Clean Architecture services.

### Case 13: a builder for a small request
**Prompt:** Explain the patterns in this production code and whether they fit.
**Setup:**
```csharp
public sealed class BookingRequestBuilder
{
    private int _clinicId; private DateTimeOffset _startsAt; private string? _notes;
    public BookingRequestBuilder ForClinic(int id) { _clinicId = id; return this; }
    public BookingRequestBuilder At(DateTimeOffset t) { _startsAt = t; return this; }
    public BookingRequestBuilder WithNotes(string n) { _notes = n; return this; }
    public BookingRequest Build() => new(_clinicId, _startsAt, _notes);
}
public sealed record BookingRequest(int ClinicId, DateTimeOffset StartsAt, string? Notes);
```
**Must:** judge it over-engineered for three fields (use the record directly, `required`/`init` if needed),
and note that it can build an invalid request (clinic 0, no time) because `Build()` checks nothing.
**Must not:** endorse it for production code. (A test-data builder would be fine; this isn't one.)

### Case 14: a keyed service with a fixed key
**Prompt:** Is this a service locator? A colleague says keyed services are banned here.
**Setup:**
```csharp
public sealed class OtpService([FromKeyedServices("sms")] IMessageSender sender)
{
    public Task SendAsync(string mobile, string code, CancellationToken ct) =>
        sender.SendAsync(mobile, $"Your code is {code}", ct);
}
// services.AddKeyedSingleton<IMessageSender, SmsSender>("sms");
// services.AddKeyedSingleton<IMessageSender, EmailSender>("email");
```
**Must:** say it is not a service locator: the key is fixed at compile time and the dependency is visible in
the constructor; it's .NET's built-in way to pick a strategy chosen at compile time. A locator is
resolving by a runtime value from `IServiceProvider`.
**Must not:** agree that it should be removed.

### Case 15: a repository that returns IQueryable
**Prompt:** Our repositories return `IQueryable<T>` so callers can add their own filters. Good idea?
**Setup:**
```csharp
public interface IAppointmentRepository { IQueryable<Appointment> Query(); }
// API endpoint: repo.Query().Where(a => a.ClinicId == id).Include(a => a.Patient).ToListAsync();
```
**Must:** say no: the query (and EF concepts like `Include`) leaks out of Infrastructure into callers, the
repository promises nothing about what runs, and every caller builds its own SQL; offer intent-named
methods, or for reads skip the repository and query in the read side.
**Must not:** recommend it.

### Case 16: inheritance for code reuse
**Prompt:** Explain the patterns in this code and whether they fit.
**Setup:**
```csharp
public abstract class ReportBase { public async Task<byte[]> RunAsync() { var d = await LoadAsync(); return Render(Format(d)); }
    protected abstract Task<Data> LoadAsync(); protected virtual Data Format(Data d) => d; protected abstract byte[] Render(Data d); }
public abstract class PdfReportBase : ReportBase { protected override byte[] Render(Data d) => Pdf.From(d); }
public abstract class ClinicPdfReportBase : PdfReportBase { protected override Data Format(Data d) => AddClinicHeader(d); }
public sealed class MonthlyVisitsReport : ClinicPdfReportBase { protected override Task<Data> LoadAsync() => /* query */ default!; }
// 11 reports derive from these three bases; two of them override Render back to Excel.
```
**Must:** identify Template Method and judge it misused: a three-level hierarchy where each level overrides
part of the parent, and subclasses undo a parent's choice (Excel over PDF); prefer composition (a loader,
a formatter and a renderer injected).
**Must not:** judge it fitting because the template method itself is textbook.

## Set C — the `/patterns` command on a whole service (added 2026-10-09)

Sets A and B ask about one snippet each. Set C runs the command the way a developer does: on a folder of
15 files (`tests/evals/patterns/ClinicBooking`), where the patterns have to be found before they can be
judged. Held out: the command, the agent and the skill were not changed for these cases.

**With the command:** the run reads `commands/patterns.md`, `agents/pattern-analyst.md` and this skill,
then gets the developer's line (`/patterns explain <folder>`). **Without:** the same question in plain
words, and no proven-dotnet files read. Both ran in the proven-dotnet repo, so both had the always-loaded rules.

**The key** (nine planted findings):

| # | Planted | Expected |
|---|---|---|
| K1 | `CachedClinicDirectory` wraps `SqlClinicDirectory` | Fits |
| K2 | `IPriceCalculator` (one implementation) + `PriceCalculatorFactory` that only calls `new` | Over-engineered |
| K3 | `Appointment` lifecycle in three bools, rules repeated in three handlers | Missing (status + transition table first) |
| K4 | `ClinicSettingsCache.Instance`, hand-rolled singleton of settings | Built into .NET (options) |
| K5 | `IRepository<T>`; `GetAll()` then filtering in memory in `TodayListHandler` | Misused |
| K6 | `INotificationChannel` chosen per patient in `ReminderSender` | Fits |
| K7 | the same `switch` on `InsuranceType` in three classes | Missing (one place per insurance type) |
| K8 | `BookAppointmentHandler` saves, then produces to Kafka | Misused / missing outbox |
| K9 | `RetryStrategy`, a static class of constants | Must not be reported as Strategy |

### Case 17: explain a whole service
**Prompt:** `/patterns explain tests/evals/patterns/ClinicBooking` (without: "Explain the design patterns
in this folder, and whether each one fits.")
**Must:** reach the key's verdict on K1–K8; cite file and symbol for each; a table, then a section per
finding with "how it works here" for the ones that fit.
**Must not:** report K9 as a Strategy; report a pattern with no evidence.

### Case 18: `--brief`
**Prompt:** the same with `--brief` (without: "... Keep it short: a table, plus detail only for the ones
that don't fit.")
**Must:** the table, then sections for the non-**Fits** findings only; the same verdicts as case 17.

### Case 19: suggest, where the pattern is already there
**Prompt:** `/patterns suggest "We want to add WhatsApp reminders next quarter. How should we structure
it?" .../Notifications` (without: "Which design pattern should we use?")
**Must:** keep the existing `INotificationChannel` strategy: one new channel class, an enum value, one
registration; a sketch in this repo's style.
**Must not:** add a factory, a new abstraction over the channels, or a mediator.

### Case 20: suggest, vague
**Prompt:** `/patterns suggest "make the booking code cleaner"` (without: "Which design pattern would make
the booking code cleaner?")
**Must:** ask one question before recommending (the command's step 1).
**Must not:** answer with a full set of recommendations.

## Results

### 2026-10-05, v10.40.0 — first run (all cases held out)

One fresh general-purpose subagent per case and condition (18 runs), graded by the main session against
the Must / Must not lines above. Both conditions ran in the proven-dotnet repo, so **both had proven-dotnet's always-loaded
rules** (service locator banned, `Thread.Sleep` banned, MediatR licence notes in `package-policy`): the
baseline is "proven-dotnet without this skill", not "a bare model".

| Case | With skill | Without skill |
|---|---|---|
| 1 No pattern needed | pass | pass |
| 2 One implementation + factory that calls `new` | pass | pass |
| 3 Lifecycle in booleans | pass (transition table) | pass (switch in entity) |
| 4 Hand-rolled singleton and retry | pass | pass |
| 5 Generic repository over EF | pass | pass |
| 6 `Expression.Invoke` | pass | pass (hedged: "EF Core only in some cases") |
| 7 Decorator that fits | pass | pass |
| 8 Runtime strategy | pass | pass |
| 9 MediatR by default | pass | pass |

**9/9 vs 9/9 on the mechanical checks.** What the skill changed was the shape, not the correctness: every
with-skill answer used the five verdict words and cited the skill's sections, which `/patterns` and
`code-reviewer` rely on to report findings the same way; the without-skill answers were right in their
own words. Cost: about 11k more tokens per run with the skill (≈108k vs ≈97k per subagent), which is the
skill file itself.

Reading: these cases were too easy to separate the conditions; the model plus the rules already knows
the common pattern mistakes. The skill earns its cost through the shared verdicts, the "what .NET
already gives you" table and the tested samples, not through the general catalogue in §4, which restates
what the model knows. Next: harder cases (traps specific to these rules: keyed services with a runtime key, HiLo
for events, `SKIP LOCKED` relays), and a decision on trimming §4.

### 2026-10-05, v10.41.0 — run B1 (set B, cases 10–16, held out)

Same method: 14 fresh subagents, graded against the case lines as written before the run.

| Case | With skill | Without skill |
|---|---|---|
| 10 Decorator that swallows failures | pass | pass |
| 11 Adapter leaking vendor types | pass | pass |
| 12 Unit of Work forwarding SaveChanges | pass *by the original lines* | fail *by the original lines* |
| 13 Builder for three fields | pass | pass |
| 14 Keyed service with a fixed key | pass | pass |
| 15 Repository returning `IQueryable` | pass | pass |
| 16 Template Method hierarchy | pass | pass |

**Case 12 is the finding.** The with-skill answer followed the skill ("an `IUnitOfWork` that forwards
`SaveChangesAsync`" is a misuse) and called it built into .NET. The without-skill answer kept the
interface because in Clean Architecture handlers in Application can't reference the `DbContext`, which is
right for our layering and matches the `IUnitOfWork` in the CQRS sample. The skill was wrong; it now says
the verdict depends on the layering, and the case was revised. **This is a correction made after seeing a
result:** the next run of case 12 measures the fix, not the skill's original quality.

**Across both sets (16 cases):** with the skill 16/16 by the original lines but one of those passes was
for a wrong rule; without the skill 15/16, and its "failure" was the better answer. Honest reading: on
correctness, the skill adds nothing the model doesn't already know, and it can carry a wrong rule into
every review. Its value is consistency (verdict words, citations) and the tested samples. Cost:
about 11k tokens per load.

### 2026-10-05 — A/B after trimming §4 (v10.41.2)

§4 went from a paragraph per pattern to a table of what's specific here (22.2 KB → 18.6 KB for the whole
file). The nine cases that §4 can affect were rerun with the trimmed skill: 2, 5, 9, 10, 11, 12, 13, 15, 16.
Cases 1, 3, 4, 6, 7, 8 and 14 rely on sections that didn't change (§1–§3, §5) and were not rerun.

| Case | Before trim | After trim |
|---|---|---|
| 2, 5, 9, 10, 11, 13, 15, 16 | pass | pass |
| 12 Unit of Work (revised lines) | — (old rule was wrong) | pass: "fits if Clean Architecture… check the project references" |

No case got worse. Cost: about 1.2k fewer tokens per load (≈106.3k vs ≈107.6k per subagent). Most of the
skill's weight is the tested samples in §3, which stay.


### 2026-10-09, v10.41.21 — run C1 (set C, cases 17–20, held out)

Eight fresh general-purpose subagents (four cases, with and without), graded by the main session against
the key above, written before the runs. Roslyn MCP wasn't available in the session; the command runs fell
back to Read/Grep and said so.

| Case | With the command | Without |
|---|---|---|
| 17 Whole service | 9/9 | 8/9: K7 judged "leave the switches for now" |
| 18 `--brief` | 9/9, format right | 8/9 (K7 again), format right |
| 19 WhatsApp | pass (extend the strategy; considered and rejected keyed services as a service locator) | pass |
| 20 Vague problem | pass (one question, four options drawn from the code) | fail (a full set of recommendations, no question) |

**Reading.** Both found every planted pattern and neither fell for `RetryStrategy`, so finding patterns
across a folder isn't where the command adds value; the model does that well on its own. What the command
added: the verdict on K7 (it follows the skill's "the same switch in several places" rule and proposed the
data form of a strategy, not a class per insurer), asking before answering a vague request, the fixed
shape with file and line on every claim, and the "considered" line in suggest mode.

**K7 is a judgment call, and the key takes one side.** The without-skill answer ("three small switches on
three concerns; revisit at a fourth insurer, and fix the inconsistent default arms now") is defensible.
Both answers flagged the inconsistent defaults, which is the defect either way.

**What the baseline caught that the command didn't:** `RetryStrategy.MaxAttempts` is passed to
`MessageSendMaxRetries`, so it allows four attempts, and it lowers the Kafka client's default; and the
`Clinic` strings have no length (column rules). Neither is a pattern finding, which is why `/patterns` stayed
quiet, but it shows a narrow command can miss what a general read sees. `/full-review` is the place for
those.

**Cost:** about 114k tokens and 60 s per command run, against 99k and 54 s without: the three files read.
