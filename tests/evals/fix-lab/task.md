Review the .NET service in this folder (`HomeReadings/`) and fix what's wrong, as you would before it goes to production. Edit the files in place.

You can build it here (`dotnet build` restores from NuGet), and you can write and run unit tests. There is no PostgreSQL or Docker here, so nothing that needs a database runs. Leave the service building. If you add tests, put them in a sibling folder (`HomeReadings.Tests/`), not inside `HomeReadings/`.

Tests you can't see will run the service in-process with `WebApplicationFactory<Program>`, in the Production environment, against PostgreSQL. They rely on these, so keep them working; change anything else:
- `Program`, and the routes the app calls, with the request and response shapes it uses today: `POST /readings` (body `{ nationalId, value, unit, collectedAt, notes }`, returns `{ id, message }`), `GET /readings/{id}`, `GET /readings/today`, `GET /readings/latest` (each reading `{ id, value, unit, collectedAt, notes }`), `POST /readings/{id}/photo` (form field `photo`), `GET /health/live` and `GET /health/ready`
- `ReadingsDbContext` (the tests create the schema from its model)
- `ReadingNotifier`, which the tests resolve from DI and run with `RunOnceAsync` (they stop its background loop)
- `CareTeamClient` registered as a typed `HttpClient`; `TimeProvider` from DI
- JWT bearer authentication on the default scheme, with the authority from `Auth:Authority` and the audience `home-readings` (the tests sign their own tokens; `sub` is the patient's id)
- the settings `ConnectionStrings:Readings`, `CareTeam:BaseAddress` and `Storage:PhotoRoot`, and Serilog configured from the `Serilog` configuration section

The care team's system has no idempotency key. Patients are in Riyadh. The app sends `x-language: ar` or `en`, and sends the meter's value as text, as the meter shows it.

When you're done, list each problem you fixed (file, what, why), and anything you found but chose not to fix.
