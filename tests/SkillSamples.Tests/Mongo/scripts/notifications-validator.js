// db-scripts/mongo/notifications-validator.js: run by the pipeline/DBA with mongosh. Safe to run again.
const validator = { $jsonSchema: {
  bsonType: "object",
  required: ["userId", "title", "createdAt", "schemaVersion"],
  properties: {
    userId:        { bsonType: "int" },
    title:         { bsonType: "string", maxLength: 200 },
    isRead:        { bsonType: "bool" },
    createdAt:     { bsonType: "date" },
    schemaVersion: { bsonType: "int", minimum: 1 },
    attempts:      { bsonType: "array", maxItems: 5 }
  }
}};

// collMod fails on a collection that doesn't exist yet: a new database gets createCollection instead.
if (db.getCollectionNames().includes("notifications")) {
  const result = db.runCommand({
    collMod: "notifications",
    validator: validator,
    validationLevel: "moderate",   // a document that is already invalid can still get unrelated updates
    validationAction: "error"
  });
  if (result.ok !== 1) throw new Error(JSON.stringify(result));
} else {
  db.createCollection("notifications", { validator: validator, validationLevel: "moderate", validationAction: "error" });
}
