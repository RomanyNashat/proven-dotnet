SET lock_timeout = '5s';
BEGIN;

-- Alerts the old version never sent (HI/LO dropped, a failure blocking the queue). Only readings taken
-- after the meter was linked, as the new code does. The last day goes out now; older ones would reach the
-- patient too late to help, so they are parked for the care team to review instead.
UPDATE readings r SET "AlertDelivery" = 'Pending'
FROM meters m
WHERE m."Id" = r."MeterId"
  AND r."Alert" <> 'None' AND r."AlertSentAt" IS NULL AND r."AlertDelivery" = 'NotNeeded'
  AND r."TakenAt" >= m."LinkedAt" AND r."TakenAt" >= now() - interval '1 day';

UPDATE readings r SET "AlertDelivery" = 'Failed', "AlertLastError" = 'Not sent before the 2026-10-23 fix; over a day old'
FROM meters m
WHERE m."Id" = r."MeterId"
  AND r."Alert" <> 'None' AND r."AlertSentAt" IS NULL AND r."AlertDelivery" = 'NotNeeded'
  AND r."TakenAt" >= m."LinkedAt" AND r."TakenAt" < now() - interval '1 day';

COMMIT;
