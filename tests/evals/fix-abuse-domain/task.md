`Clinic/` holds the clinic's visit desk, `VisitDesk/`, and its test project, `VisitDesk.Tests/`. The desk lists a patient's visits and the free slots today; `Book`, `Get`, `Cancel` and `ForPatient` in `Desk.cs` throw `NotImplementedException`. Build them. What the clinic wants:

- A patient books a free slot for themselves, or for a dependent they are the guardian of. The visit costs the slot's price.
- A promo code takes its percent off the price, once per patient. A code the clinic doesn't have refuses the booking.
- The patient may leave a note for the doctor, up to 500 characters.
- A visit can be cancelled until it starts. Cancelling frees the slot and refunds the patient (`IVisitStore.Refund`).
- A guardian sees their dependents' visits as well as their own.
- The mobile app calls the desk through the API, for many users at once. `callerId` is the signed-in user, from their token.

Keep the public types and signatures as they are: the API and the app already use them. Write tests for what you build in `VisitDesk.Tests/` (`dotnet test Clinic/VisitDesk.Tests` restores from NuGet; the project references xUnit, FluentAssertions 7 and `FakeTimeProvider`).

When you're done, write `report.md`: what you built, and anything in the request you found unclear.
