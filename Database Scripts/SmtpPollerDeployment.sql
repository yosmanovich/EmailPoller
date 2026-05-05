-- ============================================
-- EmailQueue Table and Stored Procedures
-- This script creates a robust email queuing system in SQL Server, allowing for asynchronous email
-- by capturing the data that was used by sp_send_dbmail. It includes a table to store email details
-- and parameters, as well as stored procedures to manage the lifecycle of queued emails, including
-- creation, processing, success/failure handling, and maintenance tasks.
-- ============================================
-- ============================================
-- Enable Change Tracking for the database and the EmailQueue table to support efficient polling and processing of pending emails.
-- Change the YourDatabase placeholder to the name of your database before running this script.
-- ============================================
ALTER DATABASE [YourDatabase]
SET CHANGE_TRACKING = ON
(CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON);

-- Create table only if it does not already exist
IF NOT EXISTS (
    SELECT 1
    FROM sys.objects
    WHERE object_id = OBJECT_ID(N'dbo.EmailQueue')
      AND type = 'U'  -- 'U' = User table
)
BEGIN
      CREATE TABLE dbo.EmailQueue
      (
            -- Primary Key
            EmailQueueId        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
   
            -- Queue Management
            Status              VARCHAR(20) NOT NULL DEFAULT 'Pending',  -- Pending, Processing, Sent, Failed
            CreatedDate         DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
            ProcessedDate       DATETIME2 NULL,
            RetryCount          INT NOT NULL DEFAULT 0,
            MaxRetries          INT NOT NULL DEFAULT 3,
            ErrorMessage        NVARCHAR(MAX) NULL,
            MailItemId          INT NULL,  -- Populated after sp_send_dbmail succeeds
   
            -- sp_send_dbmail Parameters
            ProfileName         SYSNAME NULL,
            Recipients          VARCHAR(MAX) NOT NULL,
            CopyRecipients      VARCHAR(MAX) NULL,
            BlindCopyRecipients VARCHAR(MAX) NULL,
            FromAddress         VARCHAR(MAX) NULL,
            ReplyTo             VARCHAR(MAX) NULL,
            Subject             NVARCHAR(255) NULL,
            Body                NVARCHAR(MAX) NULL,
            BodyFormat          VARCHAR(20) NULL DEFAULT 'HTML',  -- TEXT or HTML
            Importance          VARCHAR(6) NULL DEFAULT 'Normal', -- Low, Normal, High
            Sensitivity         VARCHAR(12) NULL DEFAULT 'Normal', -- Normal, Personal, Private, Confidential
            FileAttachments     NVARCHAR(MAX) NULL,
   
            -- Query-related Parameters
            Query                       NVARCHAR(MAX) NULL,
            ExecuteQueryDatabase        SYSNAME NULL,
            AttachQueryResultAsFile     BIT NULL DEFAULT 0,
            QueryAttachmentFilename     NVARCHAR(255) NULL,
            QueryResultHeader           BIT NULL DEFAULT 1,
            QueryResultWidth            INT NULL DEFAULT 256,
            QueryResultSeparator        CHAR(1) NULL DEFAULT ' ',
            ExcludeQueryOutput          BIT NULL DEFAULT 0,
            AppendQueryError            BIT NULL DEFAULT 0,
            QueryNoTruncate             BIT NULL DEFAULT 0,
            QueryResultNoPadding        BIT NULL DEFAULT 0,
   
            -- Constraints
            CONSTRAINT CK_EmailQueue_Status CHECK (Status IN ('Pending', 'Processing', 'Sent', 'Failed', 'Cancelled')),
            CONSTRAINT CK_EmailQueue_BodyFormat CHECK (BodyFormat IN ('TEXT', 'HTML')),
            CONSTRAINT CK_EmailQueue_Importance CHECK (Importance IN ('Low', 'Normal', 'High')),
            CONSTRAINT CK_EmailQueue_Sensitivity CHECK (Sensitivity IN ('Normal', 'Personal', 'Private', 'Confidential'))
      );

      -- Index for processing pending emails
      CREATE NONCLUSTERED INDEX IX_EmailQueue_Status_CreatedDate
      ON dbo.EmailQueue (Status, CreatedDate)
      WHERE Status = 'Pending';
END
GO
-- On the table
ALTER TABLE dbo.EmailQueue
ENABLE CHANGE_TRACKING
WITH (TRACK_COLUMNS_UPDATED = ON);
GO
-- ============================================
-- Stored Procedures for Email Queue Management
-- These procedures handle the lifecycle of queued emails, including creation, processing, success/failure handling, and maintenance tasks.
-- ============================================

-- ============================================
-- Create: Add a new email to the queue
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_Create;
GO
CREATE PROCEDURE dbo.EmailQueue_Create
    @ProfileName         SYSNAME = NULL,
    @Recipients          VARCHAR(MAX),
    @CopyRecipients      VARCHAR(MAX) = NULL,
    @BlindCopyRecipients VARCHAR(MAX) = NULL,
    @FromAddress         VARCHAR(MAX) = NULL,
    @ReplyTo             VARCHAR(MAX) = NULL,
    @Subject             NVARCHAR(255) = NULL,
    @Body                NVARCHAR(MAX) = NULL,
    @BodyFormat          VARCHAR(20) = 'TEXT',
    @Importance          VARCHAR(6) = 'Normal',
    @Sensitivity         VARCHAR(12) = 'Normal',
    @FileAttachments     NVARCHAR(MAX) = NULL,
    @Query               NVARCHAR(MAX) = NULL,
    @ExecuteQueryDatabase       SYSNAME = NULL,
    @AttachQueryResultAsFile    BIT = 0,
    @QueryAttachmentFilename    NVARCHAR(255) = NULL,
    @QueryResultHeader          BIT = 1,
    @QueryResultWidth           INT = 256,
    @QueryResultSeparator       CHAR(1) = ' ',
    @ExcludeQueryOutput         BIT = 0,
    @AppendQueryError           BIT = 0,
    @QueryNoTruncate            BIT = 0,
    @QueryResultNoPadding       BIT = 0,
    @MaxRetries                 INT = 3,
    @EmailQueueId               INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
   
    INSERT INTO dbo.EmailQueue (
        ProfileName, Recipients, CopyRecipients, BlindCopyRecipients,
        FromAddress, ReplyTo, Subject, Body, BodyFormat,
        Importance, Sensitivity, FileAttachments,
        Query, ExecuteQueryDatabase, AttachQueryResultAsFile,
        QueryAttachmentFilename, QueryResultHeader, QueryResultWidth,
        QueryResultSeparator, ExcludeQueryOutput, AppendQueryError,
        QueryNoTruncate, QueryResultNoPadding, MaxRetries
    )
    VALUES (
        @ProfileName, @Recipients, @CopyRecipients, @BlindCopyRecipients,
        @FromAddress, @ReplyTo, @Subject, @Body, @BodyFormat,
        @Importance, @Sensitivity, @FileAttachments,
        @Query, @ExecuteQueryDatabase, @AttachQueryResultAsFile,
        @QueryAttachmentFilename, @QueryResultHeader, @QueryResultWidth,
        @QueryResultSeparator, @ExcludeQueryOutput, @AppendQueryError,
        @QueryNoTruncate, @QueryResultNoPadding, @MaxRetries
    );
   
    SET @EmailQueueId = SCOPE_IDENTITY();
   
    RETURN 0;
END
GO

-- ============================================
-- Processing: Get and lock the next pending email
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_Processing;
GO

CREATE PROCEDURE dbo.EmailQueue_Processing
    @EmailQueueId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.EmailQueue
    SET
        ProcessedDate = SYSDATETIME(),
            Status = 'Processing'
    WHERE EmailQueueId = @EmailQueueId;
   
    RETURN @@ROWCOUNT;
END
GO

-- ============================================
-- ClaimBatch: Atomically retrieve pending emails and set their status to Processing
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_ClaimBatch;
GO

CREATE PROCEDURE dbo.EmailQueue_ClaimBatch
    @MaxRecords INT = 1
AS
BEGIN
    SET NOCOUNT ON;
    -- Use a CTE with OUTPUT clause to atomically select and update
    ;WITH PendingEmails AS (
        SELECT TOP (@MaxRecords)
            EmailQueueId, Status, CreatedDate, ProcessedDate, RetryCount, MaxRetries,
            ErrorMessage, MailItemId, ProfileName, Recipients, CopyRecipients,
            BlindCopyRecipients, FromAddress, ReplyTo, Subject, Body, BodyFormat,
            Importance, Sensitivity, FileAttachments
        FROM dbo.EmailQueue WITH (UPDLOCK, READPAST)
        WHERE Status = 'Pending'
        ORDER BY CreatedDate ASC
    )
    UPDATE PendingEmails
    SET 
        Status = 'Processing',
        ProcessedDate = SYSDATETIME()
    OUTPUT 
        inserted.EmailQueueId,
        inserted.Status,
        inserted.CreatedDate,
        inserted.ProcessedDate,
        inserted.RetryCount,
        inserted.MaxRetries,
        inserted.ErrorMessage,
        inserted.MailItemId,
        inserted.ProfileName,
        inserted.Recipients,
        inserted.CopyRecipients,
        inserted.BlindCopyRecipients,
        inserted.FromAddress,
        inserted.ReplyTo,
        inserted.Subject,
        inserted.Body,
        inserted.BodyFormat,
        inserted.Importance,
        inserted.Sensitivity,
        inserted.FileAttachments;
    RETURN @@ROWCOUNT;
END
GO


-- ============================================
-- SUCCESS: Update record status to 'Sent' on successful send
-- This procedure updates the status of a queued email to 'Sent' 
-- once it has been successfully sent, rather than deleting the record from the queue
-- so that a historical record of sent emails is maintained for auditing or troubleshooting purposes.
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_Success;
GO

CREATE PROCEDURE dbo.EmailQueue_Success
    @EmailQueueId INT
AS
BEGIN
    SET NOCOUNT ON;
   
    UPDATE dbo.EmailQueue
    SET
        ProcessedDate = SYSDATETIME(),
            Status = 'Sent'
    WHERE EmailQueueId = @EmailQueueId;
   
    RETURN @@ROWCOUNT;
END
GO

-- ============================================
-- SUCCESS: Delete record on successful send
-- This procedure is commented out because the current design updates the status to 'Sent' rather than deleting the record from the queue
-- ============================================
/*
DROP PROCEDURE IF EXISTS dbo.EmailQueue_Success;
GO

CREATE PROCEDURE dbo.EmailQueue_Success
    @EmailQueueId INT
AS
BEGIN
    SET NOCOUNT ON;
   
    DELETE FROM dbo.EmailQueue
    WHERE EmailQueueId = @EmailQueueId;
   
    RETURN @@ROWCOUNT;
END
GO
*/
-- ============================================
-- FAILURE: Handle send failure with retry logic
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_Failure;
GO

CREATE PROCEDURE dbo.EmailQueue_Failure
    @EmailQueueId INT,
    @ErrorMessage NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
   
    UPDATE dbo.EmailQueue
    SET
        RetryCount = RetryCount + 1,
        ErrorMessage = @ErrorMessage,
        ProcessedDate = SYSDATETIME(),
        Status = CASE
                    WHEN RetryCount + 1 >= MaxRetries THEN 'Failed'
                    ELSE 'Pending'
                 END
    WHERE EmailQueueId = @EmailQueueId;
   
    RETURN @@ROWCOUNT;
END
GO

-- ============================================
-- Maintenance Procedures
-- These are avaialble for manual execution or can be scheduled as SQL Agent jobs
-- ============================================

-- ============================================
-- CANCEL: Cancel a pending email
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_Cancel;
GO

CREATE PROCEDURE dbo.EmailQueue_Cancel
    @EmailQueueId INT
AS
BEGIN
    SET NOCOUNT ON;
   
    UPDATE dbo.EmailQueue
    SET Status = 'Cancelled',
        ProcessedDate = SYSDATETIME()
    WHERE EmailQueueId = @EmailQueueId
      AND Status IN ('Pending', 'Processing');
   
    RETURN @@ROWCOUNT;
END
GO

-- ============================================
-- RETRY: Reset a failed email for retry
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_Retry;
GO

CREATE PROCEDURE dbo.EmailQueue_Retry
    @EmailQueueId INT,
    @ResetRetryCount BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
   
    UPDATE dbo.EmailQueue
    SET Status = 'Pending',
        RetryCount = CASE WHEN @ResetRetryCount = 1 THEN 0 ELSE RetryCount END,
        ErrorMessage = NULL,
        ProcessedDate = NULL
    WHERE EmailQueueId = @EmailQueueId
      AND Status IN ('Failed', 'Cancelled');
   
    RETURN @@ROWCOUNT;
END
GO

-- ============================================
-- PURGE: Clean up old records
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_Purge;
GO

CREATE PROCEDURE dbo.EmailQueue_Purge
    @DaysToKeep INT = 30,
    @StatusesToPurge VARCHAR(100) = 'Failed,Cancelled'
AS
BEGIN
    SET NOCOUNT ON;
   
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@DaysToKeep, SYSDATETIME());
   
    DELETE FROM dbo.EmailQueue
    WHERE ProcessedDate < @CutoffDate
      AND Status IN (SELECT TRIM(value) FROM STRING_SPLIT(@StatusesToPurge, ','));
   
    RETURN @@ROWCOUNT;
END
GO

-- ============================================
-- RESET STUCK: Reset emails stuck in Processing
-- ============================================
DROP PROCEDURE IF EXISTS dbo.EmailQueue_ResetStuck;
GO

CREATE PROCEDURE dbo.EmailQueue_ResetStuck
    @StuckMinutes INT = 30
AS
BEGIN
    SET NOCOUNT ON;
   
    UPDATE dbo.EmailQueue
    SET Status = 'Pending',
        ErrorMessage = CONCAT(ISNULL(ErrorMessage + ' | ', ''), 'Reset from stuck Processing state')
    WHERE Status = 'Processing'
      AND ProcessedDate < DATEADD(MINUTE, -@StuckMinutes, SYSDATETIME());
   
    RETURN @@ROWCOUNT;
END
GO


-- ============================================
-- Updating dbo.SendEmail to use EmailQueue; duplicates script in Stored Procedures/dbo.SendEmail.sql
-- for deployment purposes. This procedure will be used by applications to queue emails instead of
-- sending them synchronously, improving performance and reliability.
-- ============================================
DROP PROCEDURE IF EXISTS dbo.SendEmail;
GO

CREATE PROCEDURE dbo.SendEmail

       @profile_name sysname = null
    ,@recipients varchar(max) = null
    ,@copy_recipients varchar(max) = null
    ,@blind_copy_recipients varchar(max) = null
    ,@from_address varchar(max) = null
    ,@reply_to varchar(max) = null
    ,@subject nvarchar(255) = 'ESRQS Message'
    ,@body nvarchar(max) = null
    ,@body_format varchar(20) = null
    ,@importance varchar(6) = 'Normal'
    ,@sensitivity varchar(12) = 'Normal'
    ,@file_attachments nvarchar(max) = null
    ,@query nvarchar(max) = null
    ,@execute_query_database sysname = null
    ,@attach_query_result_as_file bit = 0
    ,@query_attachment_filename nvarchar(255) = null
    ,@query_result_header bit = 1
    ,@query_result_width int = 255
    ,@query_result_separator char(1) = ' '
    ,@exclude_query_output bit = 0
    ,@append_query_error bit = 0
    ,@query_no_truncate bit = 0
    ,@query_result_no_padding bit =  0
    ,@execution_mode varchar(10) = 'BASIC'
    ,@mailitem_id int = NULL OUTPUT

AS
/* =============================================
  Author          : Pankaj Mehta
  Create date     : 08/16/2012
  Description     : Wrapper stored procedure for sp_send_dbmail

  =============================================*/

BEGIN
      
      DECLARE @disclaimer varchar(4096)
      DECLARE @line_break varchar(64)
      DECLARE @horizontal_line varchar(50)

      
      SET @line_break = CHAR(13) + CHAR(10)
      SET @horizontal_line = REPLICATE('_',40)
      
      IF (ISNULL(@body_format,'') = 'HTML')
      BEGIN
            SET @line_break = '<br />'
            SET @horizontal_line = '<hr />'
      END
      
      --Begin WI-23812 - 12/07/2017
       IF ([dbo].[fn_GetSystemConfigValue] ('Db_Debug') = 'ON')
       BEGIN
            IF  @blind_copy_recipients IS NULL
            BEGIN
                  SELECT @blind_copy_recipients = [dbo].[fn_GetSystemConfigValue]('ESRQS.Email.TechSupportTeam')
            END
            ELSE
            BEGIN
                  SELECT @blind_copy_recipients = @blind_copy_recipients + ';' +  [dbo].[fn_GetSystemConfigValue]('ESRQS.Email.TechSupportTeam')
            END
       END
      --End WI-23812 - 12/07/2017
      
      SET @disclaimer = REPLICATE(@line_break,3) +  @horizontal_line + @line_break + 'This email was generated by an automatic process from the ESRQS system. Please do not reply to this email.'
      
      
      IF DB_NAME() <>  ISNULL([dbo].[fn_GetSystemConfigValue]('DB.DatabaseName.Production'),'') OR SERVERPROPERTY('servername') <> ISNULL([dbo].[fn_GetSystemConfigValue]('DB.ServerName.Production'), '')
      BEGIN
            SET @body = 'Server and Database: '+ CONVERT(VARCHAR(2048),SERVERPROPERTY('servername')) + '/' + CONVERT(VARCHAR(2048),DB_NAME())  + @line_break + @line_break + 'Recipients: ' + isnull(@recipients,'None') +@line_break + @line_break + 'Copy Recipients: ' + isnull(@copy_recipients,'None') +@line_break + @line_break + 'BCC Recipients: ' + isnull(@blind_copy_recipients,'None') +@line_break +  @horizontal_line + @line_break + isnull(@body,'')
          SET @recipients = ''
          SELECT @recipients = [dbo].[fn_GetSystemConfigValue]('ESRQS.Email.TechSupportTeam')
            SET @copy_recipients = ''
            SET @blind_copy_recipients = 'Aparna.Sistla@usda.gov;Sailaja.Bellamkonda@usda.gov;Pankaj.Mehta@usda.gov;'
      END
      
      SET @body =  ISNULL(@body,'') + @disclaimer

      SET @mailitem_id = 0
      
      
      IF @execution_mode = 'BASIC'
      BEGIN

            EXECUTE dbo.EmailQueue_Create @profile_name = @profile_name,
                                                        @recipients = @recipients,
                                                        @copy_recipients = @copy_recipients,
                                                        @blind_copy_recipients = @blind_copy_recipients,
                                                        @subject = @subject,
                                                        @body = @body,
                                                        @body_format = @body_format
      END
      ELSE IF @execution_mode = 'FULL'
      BEGIN
      
            EXECUTE dbo.EmailQueue_Create @profile_name = @profile_name,
                                                        @recipients  = @recipients,
                                                        @copy_recipients = @copy_recipients,
                                                        @blind_copy_recipients = @blind_copy_recipients,
                                                        @from_address = @from_address,
                                                        @reply_to = @reply_to,
                                                        @subject = @subject,
                                                        @body = @body,
                                                        @body_format = @body_format,
                                                        @importance = @importance,
                                                        @sensitivity = @sensitivity,
                                                        @file_attachments = @file_attachments,
                                                        @query = @query,
                                                        @execute_query_database = @execute_query_database,
                                                        @attach_query_result_as_file = @attach_query_result_as_file,
                                                        @query_attachment_filename = @query_attachment_filename,
                                                        @query_result_header = @query_result_header,
                                                        @query_result_width = @query_result_width,
                                                        @query_result_separator = @query_result_separator,
                                                        @exclude_query_output = @exclude_query_output,
                                                        @append_query_error = @append_query_error,
                                                        @query_no_truncate = @query_no_truncate,
                                                        @query_result_no_padding = @query_result_no_padding,
                                                        @mailitem_id = @mailitem_id  OUTPUT
      END
END