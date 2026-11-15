# Referrals

A treating doctor refers a patient to a specialist clinic; the clinic works its queue.

- `POST /referrals` makes a referral; `POST /referrals/{id}/accept` is the clinic accepting it.
- The clinic's queue is `GET /clinics/{id}/queue`.
- A referral expires after 30 days if the clinic hasn't accepted it.
- Settings live under `Referrals` in `appsettings.json`.
