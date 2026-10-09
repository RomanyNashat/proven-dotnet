// db-scripts/mongo/notifications-indexes.js: run by the pipeline/DBA with mongosh. Running it again does
// nothing: createIndex with the same name and keys is a no-op. On a large collection the build loads the
// primary, so the DBA picks the window.

// ESR, Equality then Sort then Range. The user's notifications, newest first, after a cursor:
//   find({ userId: X, _id: { $lt: cursor } }).sort({ _id: -1 })
db.notifications.createIndex({ userId: 1, _id: -1 }, { name: "ix_userId_id" });

// Only unread documents are in this index, so the unread count stays cheap however many are read. A query
// uses it only when its filter includes isRead: false.
db.notifications.createIndex({ userId: 1 }, { name: "ix_unread_userId", partialFilterExpression: { isRead: false } });

// TTL: deletes each document 90 days after createdAt. In healthcare that is a retention policy: agree it first.
db.notifications.createIndex({ createdAt: 1 }, { name: "ix_ttl_90d", expireAfterSeconds: 7776000 });
