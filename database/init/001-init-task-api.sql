CREATE TABLE IF NOT EXISTS "Tasks"
(
    "Id" UUID PRIMARY KEY,
    "Title" VARCHAR(200) NOT NULL,
    "IsCompleted" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt" TIMESTAMPTZ NOT NULL,
    "CompletedAt" TIMESTAMPTZ NULL,
    "Priority" VARCHAR(16) NOT NULL DEFAULT 'Medium',
    "RowVersion" BIGINT NOT NULL DEFAULT 0,
    CONSTRAINT "CK_Tasks_Priority" CHECK ("Priority" IN ('Low', 'Medium', 'High'))
);

CREATE INDEX IF NOT EXISTS "IX_Tasks_CreatedAt" ON "Tasks" ("CreatedAt");
