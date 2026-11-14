`ClinicQueue/` is a .NET 8 service, and .NET 8 lost support on 10 November 2026. Move it to .NET 10 and keep it working as production runs it. Edit the files in place.

You can build it here (`dotnet build` restores from NuGet), and you can write and run unit tests. There is no PostgreSQL or Docker here, so nothing that needs a database runs. Leave the service building, with its warnings as errors. If you add tests, put them in a sibling folder (`ClinicQueue.Tests/`), not inside `ClinicQueue/`.

Tests you can't see will run the service on .NET 10, in-process with `WebApplicationFactory<Program>`, in the Production environment, against PostgreSQL, the way it runs in the cluster: behind the ingress and nginx, so every request arrives from nginx's pod (10.42.0.7) with `X-Forwarded-For`. They rely on these, so keep them working; change anything else:
- `Program`, and the routes with the request and response shapes they use today: `GET /clinics` (each `{ id, name }`), `GET /clinics/{id}/queue?day=yyyy-MM-dd` (`{ clinicId, day, positions }`), `POST /visits` (body `{ clinicId, day }`, 201 with `{ id, position }`), `GET /visits/{id}/day` (`{ day }`), `GET /whoami` (`{ clientIp }`), `GET /health/live`
- the database as it is: the tables `clinic` and `visit` and their columns (the tests create them with SQL, so the model must keep matching them)
- `IClinicSource` with `IReadOnlyList<ClinicEntry> Load()`, and `ClinicEntry(Id, Name)`: the tests put their own source in its place
- JWT bearer authentication on the default scheme, with the authority from `Auth:Authority` and the audience `clinic-queue` (the tests sign their own tokens; `sub` is the patient's id)
- the settings as production has them: `ConnectionStrings:Queue`, `Proxies:Network` = `10.42.0.0/16`

When you're done, list each change you made (file, what, why), and anything you found but chose not to change.
