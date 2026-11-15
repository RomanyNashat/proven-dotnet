# Story: book a visit for yourself or a dependent

As a patient or a guardian, I book a clinic visit, so that the person who needs care gets a slot.

## Acceptance criteria

- AC1: A patient who is 18 or older on the day of the visit books for themself; a younger patient needs a guardian.
- AC2: A linked guardian books for a dependent who is younger than 18 on the day of the visit, counted in Riyadh time.
- AC3: Nobody books for a person they aren't the linked guardian of.
- AC4: The visit starts at least 2 hours and at most 30 days from now.
- AC5: A visit goes Booked to CheckedIn to Completed, or Booked to Cancelled, and no other way.
