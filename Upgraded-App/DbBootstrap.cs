// ***************************************************************************************************************************
// File: DbBootstrap.cs
// Description: Ensures all FishLens tables, stored procedures, triggers, and seed data exist in the
//              configured schema. Called once on app startup — safe to run repeatedly (idempotent).
//              If the schema/tables are already present nothing is changed; missing objects are created.
// Notes: Requires SQL Server 2016 SP1+ for CREATE OR ALTER PROCEDURE / TRIGGER syntax.
// ***************************************************************************************************************************

using System;
using System.Data.SqlClient;

namespace FishLens_App
{
    internal static class DbBootstrap
    {
        // ── Public entry point ───────────────────────────────────────────────────────
        public static void EnsureSchemaExists()
        {
            using var conn = new SqlConnection(DatabaseConfig.ConnectionString);
            conn.Open();
            string s = DatabaseConfig.Schema;

            CreateSchemaIfMissing(conn, s);
            CreateTablesIfMissing(conn, s);
            SeedBaseData(conn, s);
            CreateOrAlterProcedures(conn, s);
            CreateOrAlterTriggers(conn, s);
        }

        // ── Infrastructure ───────────────────────────────────────────────────────────
        private static void Exec(SqlConnection conn, string sql)
        {
            using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 120;
            cmd.ExecuteNonQuery();
        }

        // ── Schema ───────────────────────────────────────────────────────────────────
        private static void CreateSchemaIfMissing(SqlConnection conn, string s)
        {
            Exec(conn, $"IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{s}') EXEC('CREATE SCHEMA [{s}]')");
        }

        // ── Tables (creation order respects FK dependencies) ─────────────────────────
        private static void CreateTablesIfMissing(SqlConnection conn, string s)
        {
            // 1. Organizations (no FK deps)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'Organizations')
                CREATE TABLE [{s}].[Organizations] (
                    Id        INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    Name      NVARCHAR(100) NOT NULL,
                    CreatedAt DATETIME      NOT NULL DEFAULT getdate()
                )");

            // 2. Roles (no FK deps)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'Roles')
                CREATE TABLE [{s}].[Roles] (
                    Id   INT          NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(50) NOT NULL
                )");

            // 3. Permissions (no FK deps)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'Permissions')
                CREATE TABLE [{s}].[Permissions] (
                    Id   INT          NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(50) NOT NULL
                )");

            // 4. RolePermissions (depends on Roles, Permissions)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'RolePermissions')
                CREATE TABLE [{s}].[RolePermissions] (
                    RoleId       INT NOT NULL,
                    PermissionId INT NOT NULL,
                    PRIMARY KEY (RoleId, PermissionId),
                    FOREIGN KEY (RoleId)       REFERENCES [{s}].[Roles](Id),
                    FOREIGN KEY (PermissionId) REFERENCES [{s}].[Permissions](Id)
                )");

            // 5. FishLensUsers (depends on Organizations, Roles)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'FishLensUsers')
                CREATE TABLE [{s}].[FishLensUsers] (
                    Id             INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    Username       NVARCHAR(100) NOT NULL,
                    PasswordHash   NVARCHAR(255) NOT NULL,
                    PasswordSalt   NVARCHAR(255) NOT NULL,
                    RoleId         INT           NULL,
                    OrganizationId INT           NULL,
                    Email          NVARCHAR(256) NULL,
                    FOREIGN KEY (RoleId)         REFERENCES [{s}].[Roles](Id),
                    FOREIGN KEY (OrganizationId) REFERENCES [{s}].[Organizations](Id)
                )");

            // 6. FishLensUserSettings (depends on FishLensUsers)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'FishLensUserSettings')
                CREATE TABLE [{s}].[FishLensUserSettings] (
                    UserId            INT           NOT NULL PRIMARY KEY,
                    HighContrastMode  BIT           NOT NULL DEFAULT 0,
                    LargeText         BIT           NOT NULL DEFAULT 0,
                    UpdatedAt         DATETIME2     NULL,
                    FastMode          BIT           NOT NULL DEFAULT 0,
                    ActiveRunOverride NVARCHAR(200) NULL,
                    ActiveLocation    NVARCHAR(200) NULL,
                    FOREIGN KEY (UserId) REFERENCES [{s}].[FishLensUsers](Id)
                )");

            // 7. FishLensOrganizationLocations (depends on Organizations)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'FishLensOrganizationLocations')
                CREATE TABLE [{s}].[FishLensOrganizationLocations] (
                    OrganizationId    INT           NOT NULL,
                    Name              NVARCHAR(200) NOT NULL,
                    UpstreamDirection NVARCHAR(10)  NOT NULL DEFAULT 'left',
                    CreatedAt         DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
                    PRIMARY KEY (OrganizationId, Name),
                    FOREIGN KEY (OrganizationId) REFERENCES [{s}].[Organizations](Id)
                )");

            // 8. FishLensOrganizationRuns (depends on Organizations)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'FishLensOrganizationRuns')
                CREATE TABLE [{s}].[FishLensOrganizationRuns] (
                    OrganizationId INT           NOT NULL,
                    Name           NVARCHAR(200) NOT NULL,
                    Locked         BIT           NOT NULL DEFAULT 0,
                    CreatedAt      DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
                    PRIMARY KEY (OrganizationId, Name),
                    FOREIGN KEY (OrganizationId) REFERENCES [{s}].[Organizations](Id)
                )");

            // 9. FishLensOrganizationSettings (depends on Organizations)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'FishLensOrganizationSettings')
                CREATE TABLE [{s}].[FishLensOrganizationSettings] (
                    OrganizationId      INT           NOT NULL PRIMARY KEY,
                    ConfidenceThreshold FLOAT         NOT NULL DEFAULT 0.7,
                    UpdatedAt           DATETIME2     NULL,
                    UpdatedByUserId     INT           NULL,
                    ActiveRun           NVARCHAR(200) NULL,
                    FOREIGN KEY (OrganizationId) REFERENCES [{s}].[Organizations](Id)
                )");

            // 10. FishDetections (depends on Organizations)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'FishDetections')
                CREATE TABLE [{s}].[FishDetections] (
                    Id                 INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    OrganizationId     INT           NOT NULL,
                    RunName            NVARCHAR(200) NOT NULL DEFAULT '',
                    LocationName       NVARCHAR(200) NOT NULL DEFAULT 'Unknown',
                    VideoFile          NVARCHAR(500) NOT NULL,
                    Species            NVARCHAR(100) NULL,
                    SpeciesConfidence  FLOAT         NULL,
                    LikelyClass        NVARCHAR(100) NULL,
                    Confidence         FLOAT         NULL,
                    Direction          NVARCHAR(50)  NULL,
                    StartTimeSec       NVARCHAR(20)  NULL,
                    EndTimeSec         NVARCHAR(20)  NULL,
                    DetectionTimestamp DATETIME2     NULL,
                    CreatedByUserId    INT           NULL,
                    CreatedAt          DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
                    FOREIGN KEY (OrganizationId) REFERENCES [{s}].[Organizations](Id)
                )");

            // 11. PasswordResetTokens (depends on FishLensUsers)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'PasswordResetTokens')
                CREATE TABLE [{s}].[PasswordResetTokens] (
                    Id        INT          NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    UserId    INT          NOT NULL,
                    Token     NVARCHAR(10) NOT NULL,
                    ExpiresAt DATETIME     NOT NULL,
                    Used      BIT          NOT NULL DEFAULT 0,
                    FOREIGN KEY (UserId) REFERENCES [{s}].[FishLensUsers](Id)
                )");

            // 12. SignupVerificationTokens (no FK deps)
            Exec(conn, $@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                               WHERE TABLE_SCHEMA = '{s}' AND TABLE_NAME = 'SignupVerificationTokens')
                CREATE TABLE [{s}].[SignupVerificationTokens] (
                    Id        INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    Email     NVARCHAR(255) NOT NULL,
                    Token     NVARCHAR(10)  NOT NULL,
                    ExpiresAt DATETIME      NOT NULL,
                    Used      BIT           NOT NULL DEFAULT 0
                )");
        }

        // ── Seed data (Roles and Permissions with fixed IDs the app depends on) ────
        private static void SeedBaseData(SqlConnection conn, string s)
        {
            // Roles — IDs are hardcoded in AuthWindow.xaml.cs (Admin=1, User=2, Viewer=3)
            (int id, string name)[] roles = { (1, "Admin"), (2, "User"), (3, "Viewer") };
            foreach (var (id, name) in roles)
            {
                Exec(conn, $@"
                    IF NOT EXISTS (SELECT 1 FROM [{s}].[Roles] WHERE Id = {id})
                    BEGIN
                        SET IDENTITY_INSERT [{s}].[Roles] ON;
                        INSERT INTO [{s}].[Roles] (Id, Name) VALUES ({id}, N'{name}');
                        SET IDENTITY_INSERT [{s}].[Roles] OFF;
                    END");
            }

            // Permissions — IDs must stay stable so RolePermissions FK references work
            (int id, string name)[] perms =
            {
                (1, "Settings"), (2, "History"), (3, "UserSettings"),
                (4, "CreateRole"), (5, "CreateUser"), (6, "Reports")
            };
            foreach (var (id, name) in perms)
            {
                Exec(conn, $@"
                    IF NOT EXISTS (SELECT 1 FROM [{s}].[Permissions] WHERE Id = {id})
                    BEGIN
                        SET IDENTITY_INSERT [{s}].[Permissions] ON;
                        INSERT INTO [{s}].[Permissions] (Id, Name) VALUES ({id}, N'{name}');
                        SET IDENTITY_INSERT [{s}].[Permissions] OFF;
                    END");
            }

            // RolePermissions — Admin(1)→Settings(1), Admin(1)→CreateUser(5), User(2)→History(2), User(2)→Reports(6)
            (int roleId, int permId)[] rolePerms = { (1, 1), (1, 5), (2, 2), (2, 6) };
            foreach (var (roleId, permId) in rolePerms)
            {
                Exec(conn, $@"
                    IF NOT EXISTS (SELECT 1 FROM [{s}].[RolePermissions]
                                   WHERE RoleId = {roleId} AND PermissionId = {permId})
                        INSERT INTO [{s}].[RolePermissions] (RoleId, PermissionId)
                        VALUES ({roleId}, {permId})");
            }
        }

        // ── Stored Procedures (CREATE OR ALTER — safe to run on every startup) ─────
        private static void CreateOrAlterProcedures(SqlConnection conn, string s)
        {
            // AddOrganizationLocation
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[AddOrganizationLocation]
    @pOrgId             INT,
    @pName              NVARCHAR(200),
    @pUpstreamDirection NVARCHAR(10) = 'left'
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM [{s}].[FishLensOrganizationLocations]
                   WHERE OrganizationId = @pOrgId AND Name = @pName)
    BEGIN
        INSERT INTO [{s}].[FishLensOrganizationLocations] (OrganizationId, Name, UpstreamDirection)
        VALUES (@pOrgId, @pName, @pUpstreamDirection);
    END
END");

            // AddOrganizationRun
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[AddOrganizationRun]
    @pOrgId  INT,
    @pName   NVARCHAR(200),
    @pLocked BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM [{s}].[FishLensOrganizationRuns]
                   WHERE OrganizationId = @pOrgId AND Name = @pName)
    BEGIN
        INSERT INTO [{s}].[FishLensOrganizationRuns] (OrganizationId, Name, Locked)
        VALUES (@pOrgId, @pName, @pLocked);
    END
END");

            // AddUser
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[AddUser]
    @pUser     NVARCHAR(100),
    @pPassword NVARCHAR(255),
    @pRoleId   INT,
    @pOrgId    INT = NULL,
    @pEmail    NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @salt         UNIQUEIDENTIFIER = NEWID();
    DECLARE @passwordHash VARBINARY(64);
    SET @passwordHash = HASHBYTES('SHA2_512', @pPassword + CAST(@salt AS NVARCHAR(36)));
    INSERT INTO [{s}].[FishLensUsers] (Username, PasswordHash, PasswordSalt, RoleId, OrganizationId, Email)
    VALUES (@pUser, CONVERT(NVARCHAR(255), @passwordHash, 2), CAST(@salt AS NVARCHAR(36)),
            @pRoleId, @pOrgId, @pEmail);
END");

            // AssignPermissionToRole
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[AssignPermissionToRole]
    @RoleName       NVARCHAR(50),
    @PermissionName NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RoleId INT;
    DECLARE @PermissionId INT;
    SELECT @RoleId       = Id FROM [{s}].[Roles]       WHERE Name = @RoleName;
    SELECT @PermissionId = Id FROM [{s}].[Permissions] WHERE Name = @PermissionName;
    IF @RoleId IS NULL
    BEGIN
        RAISERROR('Role does not exist.', 16, 1);
        RETURN;
    END
    IF @PermissionId IS NULL
    BEGIN
        RAISERROR('Permission does not exist.', 16, 1);
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM [{s}].[RolePermissions]
                   WHERE RoleId = @RoleId AND PermissionId = @PermissionId)
    BEGIN
        INSERT INTO [{s}].[RolePermissions] (RoleId, PermissionId) VALUES (@RoleId, @PermissionId);
    END
END");

            // ChangeUserRole
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[ChangeUserRole]
    @Username NVARCHAR(100),
    @RoleName NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @RoleId INT;
    SELECT @RoleId = Id FROM [{s}].[Roles] WHERE Name = @RoleName;
    IF @RoleId IS NULL
    BEGIN
        RAISERROR('Role does not exist.', 16, 1);
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM [{s}].[FishLensUsers] WHERE Username = @Username)
    BEGIN
        RAISERROR('User does not exist.', 16, 1);
        RETURN;
    END
    UPDATE [{s}].[FishLensUsers] SET RoleId = @RoleId WHERE Username = @Username;
END");

            // CreateOrganization
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[CreateOrganization]
    @pOrgName  NVARCHAR(100),
    @pUser     NVARCHAR(100),
    @pPassword NVARCHAR(255),
    @pRoleId   INT,
    @pEmail    NVARCHAR(256),
    @pOrgId    INT OUTPUT,
    @pUserId   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [{s}].[Organizations] (Name) VALUES (@pOrgName);
    SET @pOrgId = SCOPE_IDENTITY();
    DECLARE @salt         UNIQUEIDENTIFIER = NEWID();
    DECLARE @passwordHash VARBINARY(64);
    SET @passwordHash = HASHBYTES('SHA2_512', @pPassword + CAST(@salt AS NVARCHAR(36)));
    INSERT INTO [{s}].[FishLensUsers]
        (Username, PasswordHash, PasswordSalt, RoleId, OrganizationId, Email)
    VALUES
        (@pUser, CONVERT(NVARCHAR(255), @passwordHash, 2), CAST(@salt AS NVARCHAR(36)),
         @pRoleId, @pOrgId, @pEmail);
    SET @pUserId = SCOPE_IDENTITY();
END");

            // DeleteOrganizationLocation
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[DeleteOrganizationLocation]
    @pOrgId INT,
    @pName  NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [{s}].[FishLensOrganizationLocations]
    WHERE OrganizationId = @pOrgId AND Name = @pName;
END");

            // DeleteOrganizationRun
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[DeleteOrganizationRun]
    @pOrgId INT,
    @pName  NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [{s}].[FishLensOrganizationRuns]
    WHERE OrganizationId = @pOrgId AND Name = @pName;
END");

            // DeleteUser
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[DeleteUser]
    @pUserId           INT,
    @pRequestingUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @pUserId = @pRequestingUserId
    BEGIN
        RAISERROR('Cannot delete your own account.', 16, 1);
        RETURN;
    END
    DECLARE @TargetOrgId INT, @TargetRoleId INT;
    SELECT @TargetOrgId = OrganizationId, @TargetRoleId = RoleId
    FROM [{s}].[FishLensUsers]
    WHERE Id = @pUserId;
    IF @TargetOrgId IS NULL
    BEGIN
        RAISERROR('User not found.', 16, 1);
        RETURN;
    END
    IF @TargetRoleId = 1
    BEGIN
        DECLARE @AdminCount INT;
        SELECT @AdminCount = COUNT(*)
        FROM [{s}].[FishLensUsers]
        WHERE OrganizationId = @TargetOrgId AND RoleId = 1;
        IF @AdminCount <= 1
        BEGIN
            RAISERROR('Cannot delete the last admin of an organization.', 16, 1);
            RETURN;
        END
    END
    BEGIN TRY
        BEGIN TRANSACTION;
        DELETE FROM [{s}].[PasswordResetTokens]  WHERE UserId = @pUserId;
        DELETE FROM [{s}].[FishLensUserSettings] WHERE UserId = @pUserId;
        UPDATE [{s}].[FishLensOrganizationSettings]
        SET UpdatedByUserId = NULL
        WHERE UpdatedByUserId = @pUserId;
        DELETE FROM [{s}].[FishLensUsers] WHERE Id = @pUserId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END");

            // GetFishDetectionsByOrg
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[GetFishDetectionsByOrg]
    @pOrgId        INT,
    @pRunName      NVARCHAR(200) = NULL,
    @pLocationName NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, OrganizationId, RunName, LocationName, VideoFile,
           Species, SpeciesConfidence, LikelyClass, Confidence,
           Direction, StartTimeSec, EndTimeSec, DetectionTimestamp,
           CreatedByUserId, CreatedAt
    FROM [{s}].[FishDetections]
    WHERE OrganizationId = @pOrgId
      AND (@pRunName      IS NULL OR RunName      = @pRunName)
      AND (@pLocationName IS NULL OR LocationName = @pLocationName)
    ORDER BY DetectionTimestamp DESC, CreatedAt DESC;
END");

            // GetFishDetectionsByRun
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[GetFishDetectionsByRun]
    @pOrgId   INT,
    @pRunName NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, OrganizationId, RunName, LocationName, VideoFile,
           Species, SpeciesConfidence, LikelyClass, Confidence,
           Direction, StartTimeSec, EndTimeSec, DetectionTimestamp,
           CreatedByUserId, CreatedAt
    FROM [{s}].[FishDetections]
    WHERE OrganizationId = @pOrgId AND RunName = @pRunName
    ORDER BY DetectionTimestamp DESC, CreatedAt DESC;
END");

            // GetOrganizationLocations
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[GetOrganizationLocations]
    @pOrgId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Name, UpstreamDirection
    FROM [{s}].[FishLensOrganizationLocations]
    WHERE OrganizationId = @pOrgId
    ORDER BY CreatedAt;
END");

            // GetOrganizationRuns
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[GetOrganizationRuns]
    @pOrgId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Name, Locked
    FROM [{s}].[FishLensOrganizationRuns]
    WHERE OrganizationId = @pOrgId
    ORDER BY CreatedAt;
END");

            // GetOrganizationSettings
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[GetOrganizationSettings]
    @pOrgId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ConfidenceThreshold, ActiveRun
    FROM [{s}].[FishLensOrganizationSettings]
    WHERE OrganizationId = @pOrgId;
END");

            // GetUserPermissions
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[GetUserPermissions]
    @Username NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.Username, r.Name AS Role, p.Name AS Permission
    FROM [{s}].[FishLensUsers] u
    JOIN  [{s}].[Roles]           r  ON u.RoleId       = r.Id
    LEFT JOIN [{s}].[RolePermissions] rp ON r.Id       = rp.RoleId
    LEFT JOIN [{s}].[Permissions]  p  ON rp.PermissionId = p.Id
    WHERE u.Username = @Username;
END");

            // GetUserSettings
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[GetUserSettings]
    @pUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT FastMode, HighContrastMode, LargeText, ActiveRunOverride, ActiveLocation
    FROM [{s}].[FishLensUserSettings]
    WHERE UserId = @pUserId;
END");

            // ResetPassword
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[ResetPassword]
    @pUserId      INT,
    @pNewPassword NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @salt         UNIQUEIDENTIFIER = NEWID();
    DECLARE @passwordHash VARBINARY(64);
    SET @passwordHash = HASHBYTES('SHA2_512', @pNewPassword + CAST(@salt AS NVARCHAR(36)));
    UPDATE [{s}].[FishLensUsers]
    SET PasswordHash = CONVERT(NVARCHAR(255), @passwordHash, 2),
        PasswordSalt = CAST(@salt AS NVARCHAR(36))
    WHERE Id = @pUserId;
END");

            // SaveOrganizationSettings
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[SaveOrganizationSettings]
    @pOrgId               INT,
    @pConfidenceThreshold FLOAT,
    @pActiveRun           NVARCHAR(200) = NULL,
    @pUpdatedByUserId     INT
AS
BEGIN
    SET NOCOUNT ON;
    MERGE [{s}].[FishLensOrganizationSettings] AS target
    USING (SELECT @pOrgId AS OrganizationId) AS source
    ON target.OrganizationId = source.OrganizationId
    WHEN MATCHED THEN
        UPDATE SET ConfidenceThreshold = @pConfidenceThreshold,
                   ActiveRun           = @pActiveRun,
                   UpdatedAt           = SYSUTCDATETIME(),
                   UpdatedByUserId     = @pUpdatedByUserId
    WHEN NOT MATCHED THEN
        INSERT (OrganizationId, ConfidenceThreshold, ActiveRun, UpdatedByUserId)
        VALUES (@pOrgId, @pConfidenceThreshold, @pActiveRun, @pUpdatedByUserId);
END");

            // SaveUserActiveLocation
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[SaveUserActiveLocation]
    @pUserId         INT,
    @pActiveLocation NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    MERGE [{s}].[FishLensUserSettings] AS target
    USING (SELECT @pUserId AS UserId) AS source
    ON target.UserId = source.UserId
    WHEN MATCHED THEN
        UPDATE SET ActiveLocation = @pActiveLocation, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (UserId, ActiveLocation) VALUES (@pUserId, @pActiveLocation);
END");

            // SaveUserSettings
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[SaveUserSettings]
    @pUserId            INT,
    @pFastMode          BIT,
    @pHighContrastMode  BIT,
    @pLargeText         BIT,
    @pActiveRunOverride NVARCHAR(200) = NULL,
    @pActiveLocation    NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    MERGE [{s}].[FishLensUserSettings] AS target
    USING (SELECT @pUserId AS UserId) AS source
    ON target.UserId = source.UserId
    WHEN MATCHED THEN
        UPDATE SET FastMode          = @pFastMode,
                   HighContrastMode  = @pHighContrastMode,
                   LargeText         = @pLargeText,
                   ActiveRunOverride = @pActiveRunOverride,
                   ActiveLocation    = @pActiveLocation,
                   UpdatedAt         = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (UserId, FastMode, HighContrastMode, LargeText, ActiveRunOverride, ActiveLocation)
        VALUES (@pUserId, @pFastMode, @pHighContrastMode, @pLargeText, @pActiveRunOverride, @pActiveLocation);
END");

            // Unsalt  (note: procedure name matches live DB casing)
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[Unsalt]
    @pUser     NVARCHAR(100),
    @pPassword NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @salt       NVARCHAR(36);
    DECLARE @storedHash NVARCHAR(255);
    DECLARE @inputHash  VARBINARY(64);
    SELECT @salt = PasswordSalt, @storedHash = PasswordHash
    FROM [{s}].[FishLensUsers]
    WHERE Username = @pUser;
    IF @salt IS NULL
    BEGIN
        SELECT 0 AS IsValid;
        RETURN;
    END
    SET @inputHash = HASHBYTES('SHA2_512', @pPassword + @salt);
    IF CONVERT(NVARCHAR(255), @inputHash, 2) = @storedHash
        SELECT 1 AS IsValid;
    ELSE
        SELECT 0 AS IsValid;
END");

            // UpdateOrganizationLocation
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[UpdateOrganizationLocation]
    @pOrgId             INT,
    @pName              NVARCHAR(200),
    @pUpstreamDirection NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [{s}].[FishLensOrganizationLocations]
    SET UpstreamDirection = @pUpstreamDirection
    WHERE OrganizationId = @pOrgId AND Name = @pName;
END");

            // UpdateOrganizationRun
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[UpdateOrganizationRun]
    @pOrgId  INT,
    @pName   NVARCHAR(200),
    @pLocked BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [{s}].[FishLensOrganizationRuns]
    SET Locked = @pLocked
    WHERE OrganizationId = @pOrgId AND Name = @pName;
END");

            // UpsertFishDetection
            Exec(conn, $@"
CREATE OR ALTER PROCEDURE [{s}].[UpsertFishDetection]
    @pOrgId              INT,
    @pRunName            NVARCHAR(200),
    @pLocationName       NVARCHAR(200),
    @pVideoFile          NVARCHAR(500),
    @pSpecies            NVARCHAR(100),
    @pSpeciesConfidence  FLOAT,
    @pLikelyClass        NVARCHAR(100),
    @pConfidence         FLOAT,
    @pDirection          NVARCHAR(50),
    @pStartTimeSec       NVARCHAR(20),
    @pEndTimeSec         NVARCHAR(20),
    @pDetectionTimestamp DATETIME2,
    @pCreatedByUserId    INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM [{s}].[FishDetections]
        WHERE OrganizationId = @pOrgId
          AND RunName        = @pRunName
          AND VideoFile      = @pVideoFile
          AND StartTimeSec   = @pStartTimeSec
    )
    BEGIN
        UPDATE [{s}].[FishDetections]
        SET LocationName      = @pLocationName,
            Species           = @pSpecies,
            SpeciesConfidence = @pSpeciesConfidence,
            LikelyClass       = @pLikelyClass,
            Confidence        = @pConfidence,
            Direction         = @pDirection,
            EndTimeSec        = @pEndTimeSec,
            DetectionTimestamp = @pDetectionTimestamp
        WHERE OrganizationId = @pOrgId
          AND RunName        = @pRunName
          AND VideoFile      = @pVideoFile
          AND StartTimeSec   = @pStartTimeSec;
    END
    ELSE
    BEGIN
        INSERT INTO [{s}].[FishDetections]
            (OrganizationId, RunName, LocationName, VideoFile,
             Species, SpeciesConfidence, LikelyClass, Confidence,
             Direction, StartTimeSec, EndTimeSec, DetectionTimestamp, CreatedByUserId)
        VALUES
            (@pOrgId, @pRunName, @pLocationName, @pVideoFile,
             @pSpecies, @pSpeciesConfidence, @pLikelyClass, @pConfidence,
             @pDirection, @pStartTimeSec, @pEndTimeSec, @pDetectionTimestamp, @pCreatedByUserId);
    END
END");
        }

        // ── Triggers ─────────────────────────────────────────────────────────────────
        private static void CreateOrAlterTriggers(SqlConnection conn, string s)
        {
            // Auto-default Species to 'No data' when NULL or written as 'No species'
            Exec(conn, $@"
CREATE OR ALTER TRIGGER [{s}].[tr_FishDetections_SpeciesDefault]
ON [{s}].[FishDetections]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE Species IS NULL OR Species = N'No species')
    BEGIN
        UPDATE fd
        SET fd.Species = N'No data'
        FROM [{s}].[FishDetections] fd
        JOIN inserted i
          ON fd.OrganizationId = i.OrganizationId
         AND fd.RunName        = i.RunName
         AND fd.VideoFile      = i.VideoFile
         AND fd.StartTimeSec   = i.StartTimeSec
        WHERE fd.Species IS NULL OR fd.Species = N'No species';
    END
END");
        }
    }
}
