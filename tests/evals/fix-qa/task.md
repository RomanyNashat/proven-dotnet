`Pharmacy/` holds a service, `RefillDesk/`, and its test project, `RefillDesk.Tests/`, which has one test so far. `Pharmacy/story.md` is the story the service was built for, with its acceptance criteria. The team believes the service is correct today.

Write the tests for this story in `RefillDesk.Tests/`. They are how the team will find out when a later change to the service breaks what the story asks for, so they should fail for any such change and pass on the service as it is now. Don't change anything in `RefillDesk/`; the tests are graded against the service exactly as it is.

You can build and run the tests here (`dotnet test Pharmacy/RefillDesk.Tests` restores from NuGet). The project already references xUnit, FluentAssertions 7 and `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`); add other test packages if you need them.

When you're done, write `report.md`: what you tested, criterion by criterion, and anything in the story you found unclear.
