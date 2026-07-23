-- ============================================================================
-- Stored Procedure: sp_ArchiveOldChatData
-- Description: Archives chat messages, read statuses, logs, and login histories
--              older than @DaysToKeep days (default 2 days) into Archive tables,
--              then deletes them from active tables in safe batches.
-- ============================================================================

IF OBJECT_ID('dbo.sp_ArchiveOldChatData', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ArchiveOldChatData;
GO

CREATE PROCEDURE dbo.sp_ArchiveOldChatData
    @DaysToKeep INT = 2,
    @BatchSize INT = 5000
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @CutoffDate DATETIME = DATEADD(DAY, -@DaysToKeep, GETDATE());
    DECLARE @RowsAffected INT = 1;

    -- ============================================================================
    -- 1. Ensure Archive Tables Exist
    -- ============================================================================

    -- 1a. ChatMessages_Archive
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatMessages_Archive')
    BEGIN
        CREATE TABLE dbo.ChatMessages_Archive (
            Id INT NOT NULL PRIMARY KEY,
            SenderId NVARCHAR(450) NULL,
            ReceiverId NVARCHAR(450) NULL,
            Message NVARCHAR(MAX) NULL,
            GroupName NVARCHAR(MAX) NULL,
            IsDelete BIT NOT NULL,
            ReplyToMessageId INT NOT NULL,
            ClientMessageId NVARCHAR(MAX) NULL,
            CreatedOn DATETIME NULL,
            ArchivedAt DATETIME NOT NULL DEFAULT GETDATE()
        );
        CREATE INDEX IX_ChatMessages_Archive_CreatedOn ON dbo.ChatMessages_Archive(CreatedOn);
    END;

    -- 1b. ChatMessageReadStatuses_Archive
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatMessageReadStatuses_Archive')
    BEGIN
        CREATE TABLE dbo.ChatMessageReadStatuses_Archive (
            Id INT NOT NULL PRIMARY KEY,
            ChatMessageId INT NOT NULL,
            UserId NVARCHAR(450) NOT NULL,
            IsRead BIT NOT NULL,
            CreatedOn DATETIME NOT NULL,
            ArchivedAt DATETIME NOT NULL DEFAULT GETDATE()
        );
        CREATE INDEX IX_ChatMessageReadStatuses_Archive_CreatedOn ON dbo.ChatMessageReadStatuses_Archive(CreatedOn);
        CREATE INDEX IX_ChatMessageReadStatuses_Archive_ChatMessageId ON dbo.ChatMessageReadStatuses_Archive(ChatMessageId);
    END;

    -- 1c. UsersMessage_Archive
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UsersMessage_Archive')
    BEGIN
        CREATE TABLE dbo.UsersMessage_Archive (
            Id INT NOT NULL PRIMARY KEY,
            SenderId NVARCHAR(450) NULL,
            ReceiverId NVARCHAR(450) NULL,
            Message NVARCHAR(MAX) NULL,
            IsDelete BIT NOT NULL,
            ClientMessageId NVARCHAR(MAX) NULL,
            CreatedOn DATETIME NULL,
            ArchivedAt DATETIME NOT NULL DEFAULT GETDATE()
        );
        CREATE INDEX IX_UsersMessage_Archive_CreatedOn ON dbo.UsersMessage_Archive(CreatedOn);
    END;

    -- 1d. UsersMessageReadStatus_Archive
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UsersMessageReadStatus_Archive')
    BEGIN
        CREATE TABLE dbo.UsersMessageReadStatus_Archive (
            Id INT NOT NULL PRIMARY KEY,
            ChatMessageId INT NOT NULL,
            SenderId NVARCHAR(450) NOT NULL,
            ReceiverId NVARCHAR(450) NOT NULL,
            IsRead BIT NOT NULL,
            CreatedOn DATETIME NOT NULL,
            ArchivedAt DATETIME NOT NULL DEFAULT GETDATE()
        );
        CREATE INDEX IX_UsersMessageReadStatus_Archive_CreatedOn ON dbo.UsersMessageReadStatus_Archive(CreatedOn);
        CREATE INDEX IX_UsersMessageReadStatus_Archive_ChatMessageId ON dbo.UsersMessageReadStatus_Archive(ChatMessageId);
    END;

    -- 1e. EditedtMessagesLogs_Archive
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EditedtMessagesLogs_Archive')
    BEGIN
        CREATE TABLE dbo.EditedtMessagesLogs_Archive (
            Id INT NOT NULL PRIMARY KEY,
            MessageId INT NOT NULL,
            GroupName NVARCHAR(MAX) NULL,
            Message NVARCHAR(MAX) NULL,
            EditedBy NVARCHAR(MAX) NULL,
            EditedOn DATETIME NOT NULL,
            ArchivedAt DATETIME NOT NULL DEFAULT GETDATE()
        );
        CREATE INDEX IX_EditedtMessagesLogs_Archive_EditedOn ON dbo.EditedtMessagesLogs_Archive(EditedOn);
    END;

    -- 1f. ChatLogs_Archive
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatLogs_Archive')
    BEGIN
        CREATE TABLE dbo.ChatLogs_Archive (
            Id INT NOT NULL PRIMARY KEY,
            UserId NVARCHAR(MAX) NULL,
            ActionName NVARCHAR(MAX) NULL,
            Details NVARCHAR(MAX) NULL,
            CreatedAt DATETIME NOT NULL,
            ArchivedAt DATETIME NOT NULL DEFAULT GETDATE()
        );
        CREATE INDEX IX_ChatLogs_Archive_CreatedAt ON dbo.ChatLogs_Archive(CreatedAt);
    END;

    -- 1g. UserLoginLogs_Archive
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserLoginLogs_Archive')
    BEGIN
        CREATE TABLE dbo.UserLoginLogs_Archive (
            Id BIGINT NOT NULL PRIMARY KEY,
            UserId NVARCHAR(450) NOT NULL,
            LoginDateTime DATETIME NOT NULL,
            ArchivedAt DATETIME NOT NULL DEFAULT GETDATE()
        );
        CREATE INDEX IX_UserLoginLogs_Archive_LoginDateTime ON dbo.UserLoginLogs_Archive(LoginDateTime);
    END;


    -- ============================================================================
    -- 2. Archive & Delete: Group Chat Read Statuses (ChatMessageReadStatuses)
    -- ============================================================================
    SET @RowsAffected = 1;
    WHILE @RowsAffected > 0
    BEGIN
        BEGIN TRANSACTION;
        
        CREATE TABLE #TargetReadStatuses (Id INT PRIMARY KEY);
        INSERT INTO #TargetReadStatuses (Id)
        SELECT TOP (@BatchSize) Id 
        FROM dbo.ChatMessageReadStatuses WITH (NOLOCK)
        WHERE CreatedOn < @CutoffDate;

        SET @RowsAffected = @@ROWCOUNT;

        IF @RowsAffected > 0
        BEGIN
            INSERT INTO dbo.ChatMessageReadStatuses_Archive (Id, ChatMessageId, UserId, IsRead, CreatedOn)
            SELECT s.Id, s.ChatMessageId, s.UserId, s.IsRead, s.CreatedOn
            FROM dbo.ChatMessageReadStatuses s
            INNER JOIN #TargetReadStatuses t ON s.Id = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.ChatMessageReadStatuses_Archive a WHERE a.Id = s.Id);

            DELETE s
            FROM dbo.ChatMessageReadStatuses s
            INNER JOIN #TargetReadStatuses t ON s.Id = t.Id;
        END

        DROP TABLE #TargetReadStatuses;
        COMMIT TRANSACTION;
    END;


    -- ============================================================================
    -- 3. Archive & Delete: Personal Chat Read Statuses (UsersMessageReadStatus)
    -- ============================================================================
    SET @RowsAffected = 1;
    WHILE @RowsAffected > 0
    BEGIN
        BEGIN TRANSACTION;
        
        CREATE TABLE #TargetUserReadStatuses (Id INT PRIMARY KEY);
        INSERT INTO #TargetUserReadStatuses (Id)
        SELECT TOP (@BatchSize) Id 
        FROM dbo.UsersMessageReadStatus WITH (NOLOCK)
        WHERE CreatedOn < @CutoffDate;

        SET @RowsAffected = @@ROWCOUNT;

        IF @RowsAffected > 0
        BEGIN
            INSERT INTO dbo.UsersMessageReadStatus_Archive (Id, ChatMessageId, SenderId, ReceiverId, IsRead, CreatedOn)
            SELECT s.Id, s.ChatMessageId, s.SenderId, s.ReceiverId, s.IsRead, s.CreatedOn
            FROM dbo.UsersMessageReadStatus s
            INNER JOIN #TargetUserReadStatuses t ON s.Id = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.UsersMessageReadStatus_Archive a WHERE a.Id = s.Id);

            DELETE s
            FROM dbo.UsersMessageReadStatus s
            INNER JOIN #TargetUserReadStatuses t ON s.Id = t.Id;
        END

        DROP TABLE #TargetUserReadStatuses;
        COMMIT TRANSACTION;
    END;


    -- ============================================================================
    -- 4. Archive & Delete: Group Chat Messages (ChatMessages)
    -- ============================================================================
    SET @RowsAffected = 1;
    WHILE @RowsAffected > 0
    BEGIN
        BEGIN TRANSACTION;
        
        CREATE TABLE #TargetChatMessages (Id INT PRIMARY KEY);
        INSERT INTO #TargetChatMessages (Id)
        SELECT TOP (@BatchSize) Id 
        FROM dbo.ChatMessages WITH (NOLOCK)
        WHERE CreatedOn < @CutoffDate;

        SET @RowsAffected = @@ROWCOUNT;

        IF @RowsAffected > 0
        BEGIN
            -- Clean remaining read statuses for these messages
            INSERT INTO dbo.ChatMessageReadStatuses_Archive (Id, ChatMessageId, UserId, IsRead, CreatedOn)
            SELECT s.Id, s.ChatMessageId, s.UserId, s.IsRead, s.CreatedOn
            FROM dbo.ChatMessageReadStatuses s
            INNER JOIN #TargetChatMessages t ON s.ChatMessageId = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.ChatMessageReadStatuses_Archive a WHERE a.Id = s.Id);

            DELETE s
            FROM dbo.ChatMessageReadStatuses s
            INNER JOIN #TargetChatMessages t ON s.ChatMessageId = t.Id;

            -- Copy to Archive
            INSERT INTO dbo.ChatMessages_Archive (Id, SenderId, ReceiverId, Message, GroupName, IsDelete, ReplyToMessageId, ClientMessageId, CreatedOn)
            SELECT m.Id, m.SenderId, m.ReceiverId, m.Message, m.GroupName, m.IsDelete, m.ReplyToMessageId, m.ClientMessageId, m.CreatedOn
            FROM dbo.ChatMessages m
            INNER JOIN #TargetChatMessages t ON m.Id = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.ChatMessages_Archive a WHERE a.Id = m.Id);

            -- Temporarily nullify FK self-reference in target set before deletion
            UPDATE m SET ReplyToMessageId = 0
            FROM dbo.ChatMessages m
            INNER JOIN #TargetChatMessages t ON m.Id = t.Id;

            -- Delete from Active Table
            DELETE m
            FROM dbo.ChatMessages m
            INNER JOIN #TargetChatMessages t ON m.Id = t.Id;
        END

        DROP TABLE #TargetChatMessages;
        COMMIT TRANSACTION;
    END;


    -- ============================================================================
    -- 5. Archive & Delete: Personal Chat Messages (UsersMessage)
    -- ============================================================================
    SET @RowsAffected = 1;
    WHILE @RowsAffected > 0
    BEGIN
        BEGIN TRANSACTION;
        
        CREATE TABLE #TargetUsersMessages (Id INT PRIMARY KEY);
        INSERT INTO #TargetUsersMessages (Id)
        SELECT TOP (@BatchSize) Id 
        FROM dbo.UsersMessage WITH (NOLOCK)
        WHERE CreatedOn < @CutoffDate;

        SET @RowsAffected = @@ROWCOUNT;

        IF @RowsAffected > 0
        BEGIN
            -- Clean remaining read statuses for these messages
            INSERT INTO dbo.UsersMessageReadStatus_Archive (Id, ChatMessageId, SenderId, ReceiverId, IsRead, CreatedOn)
            SELECT s.Id, s.ChatMessageId, s.SenderId, s.ReceiverId, s.IsRead, s.CreatedOn
            FROM dbo.UsersMessageReadStatus s
            INNER JOIN #TargetUsersMessages t ON s.ChatMessageId = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.UsersMessageReadStatus_Archive a WHERE a.Id = s.Id);

            DELETE s
            FROM dbo.UsersMessageReadStatus s
            INNER JOIN #TargetUsersMessages t ON s.ChatMessageId = t.Id;

            -- Copy to Archive
            INSERT INTO dbo.UsersMessage_Archive (Id, SenderId, ReceiverId, Message, IsDelete, ClientMessageId, CreatedOn)
            SELECT m.Id, m.SenderId, m.ReceiverId, m.Message, m.IsDelete, m.ClientMessageId, m.CreatedOn
            FROM dbo.UsersMessage m
            INNER JOIN #TargetUsersMessages t ON m.Id = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.UsersMessage_Archive a WHERE a.Id = m.Id);

            -- Delete from Active Table
            DELETE m
            FROM dbo.UsersMessage m
            INNER JOIN #TargetUsersMessages t ON m.Id = t.Id;
        END

        DROP TABLE #TargetUsersMessages;
        COMMIT TRANSACTION;
    END;


    -- ============================================================================
    -- 6. Archive & Delete: Edited Messages Logs (EditedtMessagesLogs)
    -- ============================================================================
    SET @RowsAffected = 1;
    WHILE @RowsAffected > 0
    BEGIN
        BEGIN TRANSACTION;

        CREATE TABLE #TargetEditedLogs (Id INT PRIMARY KEY);
        INSERT INTO #TargetEditedLogs (Id)
        SELECT TOP (@BatchSize) Id 
        FROM dbo.EditedtMessagesLogs WITH (NOLOCK)
        WHERE EditedOn < @CutoffDate;

        SET @RowsAffected = @@ROWCOUNT;

        IF @RowsAffected > 0
        BEGIN
            INSERT INTO dbo.EditedtMessagesLogs_Archive (Id, MessageId, GroupName, Message, EditedBy, EditedOn)
            SELECT l.Id, l.MessageId, l.GroupName, l.Message, l.EditedBy, l.EditedOn
            FROM dbo.EditedtMessagesLogs l
            INNER JOIN #TargetEditedLogs t ON l.Id = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.EditedtMessagesLogs_Archive a WHERE a.Id = l.Id);

            DELETE l
            FROM dbo.EditedtMessagesLogs l
            INNER JOIN #TargetEditedLogs t ON l.Id = t.Id;
        END

        DROP TABLE #TargetEditedLogs;
        COMMIT TRANSACTION;
    END;


    -- ============================================================================
    -- 7. Archive & Delete: General Chat Logs (ChatLogs)
    -- ============================================================================
    SET @RowsAffected = 1;
    WHILE @RowsAffected > 0
    BEGIN
        BEGIN TRANSACTION;

        CREATE TABLE #TargetChatLogs (Id INT PRIMARY KEY);
        INSERT INTO #TargetChatLogs (Id)
        SELECT TOP (@BatchSize) Id 
        FROM dbo.ChatLogs WITH (NOLOCK)
        WHERE CreatedAt < @CutoffDate;

        SET @RowsAffected = @@ROWCOUNT;

        IF @RowsAffected > 0
        BEGIN
            INSERT INTO dbo.ChatLogs_Archive (Id, UserId, ActionName, Details, CreatedAt)
            SELECT l.Id, l.UserId, l.ActionName, l.Details, l.CreatedAt
            FROM dbo.ChatLogs l
            INNER JOIN #TargetChatLogs t ON l.Id = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.ChatLogs_Archive a WHERE a.Id = l.Id);

            DELETE l
            FROM dbo.ChatLogs l
            INNER JOIN #TargetChatLogs t ON l.Id = t.Id;
        END

        DROP TABLE #TargetChatLogs;
        COMMIT TRANSACTION;
    END;


    -- ============================================================================
    -- 8. Archive & Delete: User Login Logs (UserLoginLogs)
    -- ============================================================================
    SET @RowsAffected = 1;
    WHILE @RowsAffected > 0
    BEGIN
        BEGIN TRANSACTION;

        CREATE TABLE #TargetUserLoginLogs (Id BIGINT PRIMARY KEY);
        INSERT INTO #TargetUserLoginLogs (Id)
        SELECT TOP (@BatchSize) Id 
        FROM dbo.UserLoginLogs WITH (NOLOCK)
        WHERE LoginDateTime < @CutoffDate;

        SET @RowsAffected = @@ROWCOUNT;

        IF @RowsAffected > 0
        BEGIN
            INSERT INTO dbo.UserLoginLogs_Archive (Id, UserId, LoginDateTime)
            SELECT l.Id, l.UserId, l.LoginDateTime
            FROM dbo.UserLoginLogs l
            INNER JOIN #TargetUserLoginLogs t ON l.Id = t.Id
            WHERE NOT EXISTS (SELECT 1 FROM dbo.UserLoginLogs_Archive a WHERE a.Id = l.Id);

            DELETE l
            FROM dbo.UserLoginLogs l
            INNER JOIN #TargetUserLoginLogs t ON l.Id = t.Id;
        END

        DROP TABLE #TargetUserLoginLogs;
        COMMIT TRANSACTION;
    END;

    PRINT 'Archiving of records older than ' + CAST(@DaysToKeep AS VARCHAR) + ' days completed successfully.';
END;
GO

-- Also create or update alias Procedure "DeleteOldReadMappingRecord" for backwards compatibility
IF OBJECT_ID('dbo.DeleteOldReadMappingRecord', 'P') IS NOT NULL
    DROP PROCEDURE dbo.DeleteOldReadMappingRecord;
GO

CREATE PROCEDURE dbo.DeleteOldReadMappingRecord
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.sp_ArchiveOldChatData @DaysToKeep = 2, @BatchSize = 5000;
END;
GO
