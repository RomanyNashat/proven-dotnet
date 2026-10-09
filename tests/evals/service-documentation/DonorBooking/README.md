# Donor Booking service

Lets people find donation centers and book a donation appointment.

## Endpoints
| Method | Path | What it does |
|---|---|---|
| GET | `/api/donations/centers` | Lists donation centers, optionally by city. No sign-in needed. |
| POST | `/api/donations/appointments` | Books an appointment. Refused if the last donation was less than 56 days ago. |
| GET | `/api/donations/appointments/{id}` | One of the caller's appointments. |
| GET | `/api/donors/{id}` | A donor's profile and blood type. |

## Storage
SQL Server, database `Donations`.

## Running locally
Set `ConnectionStrings:Donations` and run `dotnet run`.
