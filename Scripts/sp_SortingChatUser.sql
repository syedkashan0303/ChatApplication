-- Stored Procedure: sp_SortingChatUser
-- Description: Retrieves users with whom @UserId has exchanged personal messages,
--              ordered by the timestamp of their latest message descending.

IF OBJECT_ID('dbo.sp_SortingChatUser', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_SortingChatUser;
GO

CREATE PROCEDURE dbo.sp_SortingChatUser
    @UserId NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        CASE 
            WHEN SenderId = @UserId THEN ReceiverId 
            ELSE SenderId 
        END AS UserId,
        MAX(CreatedOn) AS LastMessageTime
    FROM dbo.UsersMessage WITH (NOLOCK)
    WHERE SenderId = @UserId OR ReceiverId = @UserId
    GROUP BY 
        CASE 
            WHEN SenderId = @UserId THEN ReceiverId 
            ELSE SenderId 
        END
    ORDER BY LastMessageTime DESC;
END;
GO
