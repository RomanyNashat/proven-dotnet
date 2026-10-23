Review the .NET service in this folder (`MeterSync/`) and fix what's wrong, as you would before it goes to production. Edit the files in place.

You can build it here (`dotnet build` restores from NuGet), and you can write and run unit tests. There is no PostgreSQL or Docker here, so nothing that needs a database runs. Leave the service building. If you add tests, put them in a sibling folder (`MeterSync.Tests/`), not inside `MeterSync/`.

Tests you can't see will run the service in-process with `WebApplicationFactory<Program>`, in the Production environment, against PostgreSQL, sometimes as two instances sharing one database (two pods). They rely on these, so keep them working; change anything else:
- `Program`, and the routes with the request and response shapes they use today: `POST /meters` (body `{ nationalId, serialNumber }`), `GET /meters` (each meter `{ serialNumber, linkedAt }`), `GET /health/live`
- `SyncDbContext` with its `Patients` set, and `new Patient(nationalId, phone)` (the tests add patients that way, and create the schema from the model)
- `MeterSyncService.SyncOnceAsync(ct)` and `AlertSweeper.RunOnceAsync(ct)`, which the tests resolve from a DI scope and call themselves (they stop the background worker)
- `MeterVendorClient` and `SmsClient` registered as typed `HttpClient`s, and `TimeProvider` from DI
- JWT bearer authentication on the default scheme, with the authority from `Auth:Authority` and the audience `meter-sync` (the tests sign their own tokens; `sub` is the patient's id)
- the settings as production has them: `ConnectionStrings:Sync`, `MeterVendor:BaseAddress` = `https://meters.example.net/v2`, `Sms:BaseAddress` = `https://sms.example.net/`, `Cors:Origins` = `https://clinic.example.net`

`VENDOR.md` describes the vendor's API and the SMS gateway. Patients are in Riyadh and get their SMS in Arabic. The service runs on several pods.

When you're done, list each problem you fixed (file, what, why), and anything you found but chose not to fix.
