Review the .NET service in this folder (`LabResults/`) and fix what's wrong, as you would before it goes to production. Edit the files in place.

You can build it here (`dotnet build` restores from NuGet), and you can write and run unit tests. There is no PostgreSQL or Docker here, so nothing that needs a database runs. Leave the service building. If you add tests, put them in a sibling folder (`LabResults.Tests/`), not inside `LabResults/`.

Tests you can't see will run the service in-process with `WebApplicationFactory<Program>`, in the Production environment, against PostgreSQL. They rely on these, so keep them working; change anything else:
- `Program`, and the routes with the shapes they use today: `POST /results` (body `{ patientId, nationalId, phone, test, value, unit, notes }`, returns the result), `GET /results` (`{ items, next }`, newest first; pass `next` back as `before` for the next page; each item `{ id, patientId, test, value, unit, notes, recordedBy, receivedAt }`, where `recordedBy` is the clinician's `sub`), `POST /lab-orders/lookup` (body `{ nationalId }`, returns the lab's orders), `GET /health/live`
- `LabDbContext` (the tests create the schema from its model)
- `LabClient` and `SmsClient` registered as typed `HttpClient`s, and `TimeProvider` from DI
- JWT bearer authentication on the default scheme, with the authority from `Auth:Authority` and the audience `lab-results`; the tokens come from Keycloak (the tests sign their own)
- the settings `ConnectionStrings:Lab`, `Labs:BaseAddress` and `Sms:BaseAddress`

`LABS.md` describes the lab network's API and the SMS gateway.

When you're done, list each problem you fixed (file, what, why), and anything you found but chose not to fix.
