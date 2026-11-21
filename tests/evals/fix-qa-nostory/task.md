`Pharmacy/` holds a service, `RefillDesk/`, and its test project, `RefillDesk.Tests/`, which has one test so far. The service lets patients, and caregivers they have delegated, request refills of a prescription, which a pharmacist approves or rejects and then dispenses. There is no written story for it. The team believes the service is correct today.

Write the tests for it in `RefillDesk.Tests/`. They are how the team will find out when a later change to the service breaks what it does now, so they should fail for any such change and pass on the service as it is now. Don't change anything in `RefillDesk/`; the tests are graded against the service exactly as it is.

You can build and run the tests here (`dotnet test Pharmacy/RefillDesk.Tests` restores from NuGet). The project already references xUnit, FluentAssertions 7 and `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`); add other test packages if you need them.

When you're done, write `report.md`: what you tested, and anything about the service you found unclear.
