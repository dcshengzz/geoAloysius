/* ===========================================================================
   GpsSync — database maintenance (run once in SSMS against the GpsSync DB)

   Contents:
     1. Indexes that keep the live-map / latest-ping / retention queries fast
     2. A retention stored procedure that trims old GPS pings in safe batches
     3. A SQL Server Agent job to run the retention nightly
        (+ a Task Scheduler fallback for SQL Server Express, which has no Agent)

   At ~200 engineers pinging every 5s you add ~1.15M gps_pings rows/day. With a
   60-day retention window the table settles at ~70M rows (~15 GB) instead of
   growing forever. Adjust @RetentionDays to taste.
   =========================================================================== */

------------------------------------------------------------------------------
-- 1. INDEXES  (idempotent)
------------------------------------------------------------------------------
USE GpsSync;
GO

-- Speeds up GET /gps/latest (global ORDER BY CreatedAt DESC) and the retention DELETE (CreatedAt < cutoff).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
              WHERE name = 'IX_gps_pings_CreatedAt' AND object_id = OBJECT_ID('dbo.gps_pings'))
    CREATE NONCLUSTERED INDEX IX_gps_pings_CreatedAt
        ON dbo.gps_pings (CreatedAt DESC);
GO

-- Speeds up GET /gps?userId=... (WHERE UserId = @u ORDER BY CreatedAt DESC).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
              WHERE name = 'IX_gps_pings_UserId_CreatedAt' AND object_id = OBJECT_ID('dbo.gps_pings'))
    CREATE NONCLUSTERED INDEX IX_gps_pings_UserId_CreatedAt
        ON dbo.gps_pings (UserId, CreatedAt DESC);
GO

/*  Optional: EF Core already created a plain index on UserId (IX_gps_pings_UserId).
    The composite above supersedes it, so you may drop the single-column one:
        DROP INDEX IF EXISTS IX_gps_pings_UserId ON dbo.gps_pings;
    (Left in place by default — harmless, just a little extra write cost.)         */


------------------------------------------------------------------------------
-- 2. RETENTION STORED PROCEDURE  (batched delete, log-friendly)
------------------------------------------------------------------------------
USE GpsSync;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurgeOldGpsPings
    @RetentionDays int = 60,     -- keep this many days of pings
    @BatchSize     int = 5000,   -- rows per delete pass (keeps the tx log small, avoids lock escalation)
    @MaxBatches    int = 100000  -- safety stop
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @cutoff  datetimeoffset(7) = DATEADD(DAY, -@RetentionDays, SYSDATETIMEOFFSET());
    DECLARE @rows    int = 1;
    DECLARE @batches int = 0;
    DECLARE @total   bigint = 0;

    WHILE @rows > 0 AND @batches < @MaxBatches
    BEGIN
        DELETE TOP (@BatchSize) FROM dbo.gps_pings
        WHERE CreatedAt < @cutoff;

        SET @rows    = @@ROWCOUNT;
        SET @total  += @rows;
        SET @batches += 1;

        -- brief pause so we never monopolise the DB during business hours
        IF @rows > 0 WAITFOR DELAY '00:00:00.100';
    END

    PRINT CONCAT('usp_PurgeOldGpsPings: deleted ', @total, ' rows older than ', CONVERT(varchar(33), @cutoff, 127));
END
GO

-- Manual run (test it now — safe, only touches rows past the window):
--   EXEC dbo.usp_PurgeOldGpsPings @RetentionDays = 60;


------------------------------------------------------------------------------
-- 3a. NIGHTLY JOB via SQL SERVER AGENT   (Standard/Developer/Enterprise editions)
--     Skip this block on SQL Server Express (no Agent) and use 3b instead.
------------------------------------------------------------------------------
USE msdb;
GO

-- Drop any previous version so this script is re-runnable
IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'GpsSync - Purge old GPS pings')
    EXEC msdb.dbo.sp_delete_job @job_name = N'GpsSync - Purge old GPS pings', @delete_unused_schedule = 1;
GO

EXEC msdb.dbo.sp_add_job
     @job_name    = N'GpsSync - Purge old GPS pings',
     @enabled     = 1,
     @description = N'Deletes dbo.gps_pings older than the retention window (default 60 days).';

EXEC msdb.dbo.sp_add_jobstep
     @job_name       = N'GpsSync - Purge old GPS pings',
     @step_name      = N'Purge',
     @subsystem      = N'TSQL',
     @database_name  = N'GpsSync',
     @command        = N'EXEC dbo.usp_PurgeOldGpsPings @RetentionDays = 60, @BatchSize = 5000;',
     @retry_attempts = 2,
     @retry_interval = 5;

-- Daily at 02:00
EXEC msdb.dbo.sp_add_jobschedule
     @job_name          = N'GpsSync - Purge old GPS pings',
     @name              = N'GpsSync daily 02:00',
     @freq_type         = 4,        -- daily
     @freq_interval     = 1,
     @active_start_time = 020000;   -- HHMMSS -> 02:00:00

-- Run it on this server
EXEC msdb.dbo.sp_add_jobserver
     @job_name    = N'GpsSync - Purge old GPS pings',
     @server_name = N'(LOCAL)';
GO


------------------------------------------------------------------------------
-- 3b. FALLBACK for SQL Server EXPRESS (no Agent) — use Windows Task Scheduler.
--     Run this ONE line in an ELEVATED Command Prompt (not SSMS). Runs daily 02:00
--     as SYSTEM; ensure NT AUTHORITY\SYSTEM has a SQL login with rights on GpsSync,
--     or swap /RU SYSTEM for /RU <you> /RP <password>.
--
--   schtasks /Create /TN "GpsSync Purge Pings" /SC DAILY /ST 02:00 /RU SYSTEM ^
--     /TR "sqlcmd -S localhost -d GpsSync -E -Q \"EXEC dbo.usp_PurgeOldGpsPings @RetentionDays=60\""
------------------------------------------------------------------------------
