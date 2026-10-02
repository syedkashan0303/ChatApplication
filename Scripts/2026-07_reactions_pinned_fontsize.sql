/*
  ChatApplication - schema changes for:
    1. Message reactions (group chat)      -> table  ChatMessageReactions
    2. Pin chat (per user)                 -> table  UserPinnedChats
    3. Chat font size (per user)           -> column AspNetUsers.FontSize

  Safe to run more than once (every step checks first). Run it on each database
  (live, staging, local) BEFORE deploying the matching application build.

  Do NOT use "dotnet ef database update" on the live database: its
  __EFMigrationsHistory has a different id for the initial migration, so EF would
  try to re-create existing tables. Use this script instead.
*/

SET QUOTED_IDENTIFIER ON;   -- required for the unique indexes below
GO

BEGIN TRANSACTION;
GO

/* ------------------------------------------------------------------
   1. ChatMessageReactions  (one reaction per user per message)
   ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.ChatMessageReactions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ChatMessageReactions] (
        [Id]            int            NOT NULL IDENTITY,
        [ChatMessageId] int            NOT NULL,
        [UserId]        nvarchar(450)  NOT NULL,
        [Emoji]         nvarchar(16)   NOT NULL,
        [CreatedOn]     datetime2      NOT NULL,
        CONSTRAINT [PK_ChatMessageReactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ChatMessageReactions_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ChatMessageReactions_ChatMessages_ChatMessageId]
            FOREIGN KEY ([ChatMessageId]) REFERENCES [dbo].[ChatMessages] ([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_ChatMessageReactions_ChatMessageId_UserId]
        ON [dbo].[ChatMessageReactions] ([ChatMessageId], [UserId]);

    CREATE INDEX [IX_ChatMessageReactions_UserId]
        ON [dbo].[ChatMessageReactions] ([UserId]);
END
GO

/* ------------------------------------------------------------------
   2. UserPinnedChats  (TargetId = ChatRoom.Id as text when IsRoom = 1, else the other user's Id)
   ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.UserPinnedChats', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserPinnedChats] (
        [Id]        int            NOT NULL IDENTITY,
        [UserId]    nvarchar(450)  NOT NULL,
        [IsRoom]    bit            NOT NULL,
        [TargetId]  nvarchar(128)  NOT NULL,
        [CreatedOn] datetime2      NOT NULL,
        CONSTRAINT [PK_UserPinnedChats] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserPinnedChats_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_UserPinnedChats_UserId_IsRoom_TargetId]
        ON [dbo].[UserPinnedChats] ([UserId], [IsRoom], [TargetId]);
END
GO

/* ------------------------------------------------------------------
   3. AspNetUsers.FontSize  (default 18 for every existing user)
   ------------------------------------------------------------------ */
IF COL_LENGTH(N'dbo.AspNetUsers', N'FontSize') IS NULL
BEGIN
    ALTER TABLE [dbo].[AspNetUsers]
        ADD [FontSize] int NOT NULL CONSTRAINT [DF_AspNetUsers_FontSize] DEFAULT 18;
END
GO

/* ------------------------------------------------------------------
   Record the migrations in EF's history table (keeps EF tooling in sync)
   ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260702000000_AddChatMessageReactions')
        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260702000000_AddChatMessageReactions', N'8.0.11');

    IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260703000000_AddUserPinnedChats')
        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260703000000_AddUserPinnedChats', N'8.0.11');

    IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260704000000_AddUserFontSize')
        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260704000000_AddUserFontSize', N'8.0.11');
END
GO

COMMIT TRANSACTION;
GO

/* ------------------------------------------------------------------
   Check: all three should return a row
   ------------------------------------------------------------------ */
SELECT 'ChatMessageReactions' AS Item, CASE WHEN OBJECT_ID(N'dbo.ChatMessageReactions', N'U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS Status
UNION ALL
SELECT 'UserPinnedChats',            CASE WHEN OBJECT_ID(N'dbo.UserPinnedChats', N'U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'AspNetUsers.FontSize',       CASE WHEN COL_LENGTH(N'dbo.AspNetUsers', N'FontSize') IS NOT NULL THEN 'OK' ELSE 'MISSING' END;
GO
