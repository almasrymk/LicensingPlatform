IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'licensing') IS NULL EXEC(N'CREATE SCHEMA [licensing];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'integrations') IS NULL EXEC(N'CREATE SCHEMA [integrations];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA [audit];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'customers') IS NULL EXEC(N'CREATE SCHEMA [customers];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'messaging') IS NULL EXEC(N'CREATE SCHEMA [messaging];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'catalog') IS NULL EXEC(N'CREATE SCHEMA [catalog];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'identity') IS NULL EXEC(N'CREATE SCHEMA [identity];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'subscriptions') IS NULL EXEC(N'CREATE SCHEMA [subscriptions];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'tenants') IS NULL EXEC(N'CREATE SCHEMA [tenants];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA [reporting];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [licensing].[ActivationAttempts] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [LicenseId] uniqueidentifier NULL,
        [CustomerId] uniqueidentifier NULL,
        [ProductKeyPrefix] nvarchar(16) NULL,
        [DeviceId] nvarchar(128) NOT NULL,
        [Success] bit NOT NULL,
        [ErrorCode] nvarchar(64) NULL,
        [IpAddress] nvarchar(64) NULL,
        [At] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ActivationAttempts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [integrations].[ApiClients] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(256) NOT NULL,
        [ClientId] nvarchar(64) NOT NULL,
        [SecretHash] nvarchar(128) NOT NULL,
        [Scopes] nvarchar(500) NOT NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [SecretRotatedAt] datetimeoffset NOT NULL,
        [LastUsedAt] datetimeoffset NULL,
        CONSTRAINT [PK_ApiClients] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [audit].[AuditRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [TenantId] uniqueidentifier NULL,
        [ActorType] int NOT NULL,
        [ActorId] nvarchar(64) NULL,
        [ActorName] nvarchar(256) NULL,
        [Action] nvarchar(100) NOT NULL,
        [EntityType] nvarchar(64) NOT NULL,
        [EntityId] nvarchar(64) NULL,
        [Success] bit NOT NULL,
        [Details] nvarchar(2000) NULL,
        [IpAddress] nvarchar(64) NULL,
        [CorrelationId] nvarchar(64) NULL,
        [At] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AuditRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [customers].[Customers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Email] nvarchar(256) NULL,
        [Phone] nvarchar(50) NULL,
        [Country] nvarchar(100) NULL,
        [TaxNumber] nvarchar(50) NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [messaging].[IdempotencyRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [Scope] nvarchar(200) NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [StatusCode] int NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [messaging].[InboxMessages] (
        [MessageId] uniqueidentifier NOT NULL,
        [Handler] nvarchar(200) NOT NULL,
        [ProcessedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_InboxMessages] PRIMARY KEY ([MessageId], [Handler])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [licensing].[LicenseActivations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [LicenseId] uniqueidentifier NOT NULL,
        [DeviceId] nvarchar(128) NOT NULL,
        [DeviceName] nvarchar(200) NULL,
        [AppVersion] nvarchar(50) NULL,
        [Status] int NOT NULL,
        [ActivatedAt] datetimeoffset NOT NULL,
        [DeactivatedAt] datetimeoffset NULL,
        [LastHeartbeatAt] datetimeoffset NULL,
        [LastIpAddress] nvarchar(64) NULL,
        CONSTRAINT [PK_LicenseActivations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [licensing].[Licenses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [SubscriptionId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [LicenseNumber] nvarchar(32) NOT NULL,
        [ProductKeyHash] nvarchar(128) NOT NULL,
        [ProductKeyPrefix] nvarchar(16) NOT NULL,
        [Status] int NOT NULL,
        [IssuedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NULL,
        [ProductCode] nvarchar(32) NOT NULL,
        [PlanCode] nvarchar(32) NOT NULL,
        [PlanVersion] int NOT NULL,
        [Features] nvarchar(2000) NOT NULL,
        [MaxActivations] int NULL,
        [HeartbeatIntervalHours] int NOT NULL,
        [OfflineGraceDays] int NOT NULL,
        [ActiveActivations] int NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [StatusReason] nvarchar(500) NULL,
        CONSTRAINT [PK_Licenses] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [messaging].[OutboxMessages] (
        [Id] uniqueidentifier NOT NULL,
        [Type] nvarchar(128) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [ProcessedAt] datetimeoffset NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAt] datetimeoffset NULL,
        [LastError] nvarchar(2000) NULL,
        [DeadLettered] bit NOT NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [catalog].[Plans] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Code] nvarchar(32) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Version] int NOT NULL,
        [Status] int NOT NULL,
        [PriceAmount] decimal(18,2) NOT NULL,
        [PriceCurrency] nvarchar(3) NOT NULL,
        [DurationDays] int NULL,
        [TrialDays] int NULL,
        [MaxActivations] int NULL,
        [HeartbeatIntervalHours] int NOT NULL,
        [OfflineGraceDays] int NOT NULL,
        [Features] nvarchar(2000) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [PublishedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Plans] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [catalog].[Products] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(32) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [identity].[RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(128) NOT NULL,
        [FamilyId] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [ReplacedById] uniqueidentifier NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [licensing].[SigningKeys] (
        [Id] uniqueidentifier NOT NULL,
        [Kid] nvarchar(64) NOT NULL,
        [Algorithm] nvarchar(16) NOT NULL,
        [PublicKeyPem] nvarchar(2000) NOT NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [RetiringAt] datetimeoffset NULL,
        [RetiredAt] datetimeoffset NULL,
        CONSTRAINT [PK_SigningKeys] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [subscriptions].[Subscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [StartDate] datetimeoffset NOT NULL,
        [EndDate] datetimeoffset NULL,
        [TrialEndsAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CancelledAt] datetimeoffset NULL,
        [Notes] nvarchar(1000) NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [tenants].[Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(32) NOT NULL,
        [ContactEmail] nvarchar(256) NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [SuspendedAt] datetimeoffset NULL,
        [SuspensionReason] nvarchar(500) NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [reporting].[UsageDaily] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [Day] date NOT NULL,
        [ActiveLicenses] int NOT NULL,
        [ActiveDevices] int NOT NULL,
        [SuccessfulActivations] int NOT NULL,
        [FailedActivations] int NOT NULL,
        CONSTRAINT [PK_UsageDaily] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [identity].[Users] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [CustomerId] uniqueidentifier NULL,
        [Email] nvarchar(256) NOT NULL,
        [FullName] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(512) NOT NULL,
        [Role] nvarchar(32) NOT NULL,
        [IsActive] bit NOT NULL,
        [FailedLoginCount] int NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [LastLoginAt] datetimeoffset NULL,
        [PreferredLanguage] nvarchar(5) NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [integrations].[Webhooks] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Url] nvarchar(1000) NOT NULL,
        [Events] nvarchar(1000) NOT NULL,
        [SecretHash] nvarchar(128) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Webhooks] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [customers].[CustomerContacts] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Email] nvarchar(256) NULL,
        [Phone] nvarchar(50) NULL,
        [JobTitle] nvarchar(100) NULL,
        [IsPrimary] bit NOT NULL,
        CONSTRAINT [PK_CustomerContacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomerContacts_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [customers].[Customers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE TABLE [subscriptions].[SubscriptionHistory] (
        [Id] uniqueidentifier NOT NULL,
        [SubscriptionId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Action] int NOT NULL,
        [FromStatus] int NULL,
        [ToStatus] int NOT NULL,
        [At] datetimeoffset NOT NULL,
        [Details] nvarchar(500) NULL,
        CONSTRAINT [PK_SubscriptionHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionHistory_Subscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [subscriptions].[Subscriptions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ActivationAttempts_TenantId_At] ON [licensing].[ActivationAttempts] ([TenantId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApiClients_ClientId] ON [integrations].[ApiClients] ([ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ApiClients_TenantId] ON [integrations].[ApiClients] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditRecords_At] ON [audit].[AuditRecords] ([At]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditRecords_TenantId_At] ON [audit].[AuditRecords] ([TenantId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CustomerContacts_CustomerId] ON [customers].[CustomerContacts] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Customers_TenantId_Name] ON [customers].[Customers] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_CreatedAt] ON [messaging].[IdempotencyRecords] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdempotencyRecords_Scope_Key] ON [messaging].[IdempotencyRecords] ([Scope], [Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LicenseActivations_LicenseId_DeviceId] ON [licensing].[LicenseActivations] ([LicenseId], [DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LicenseActivations_TenantId_CustomerId_Status] ON [licensing].[LicenseActivations] ([TenantId], [CustomerId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Licenses_LicenseNumber] ON [licensing].[Licenses] ([LicenseNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Licenses_ProductKeyHash] ON [licensing].[Licenses] ([ProductKeyHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Licenses_Status_ExpiresAt] ON [licensing].[Licenses] ([Status], [ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Licenses_SubscriptionId] ON [licensing].[Licenses] ([SubscriptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Licenses_TenantId_CustomerId] ON [licensing].[Licenses] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_ProcessedAt_DeadLettered_NextAttemptAt] ON [messaging].[OutboxMessages] ([ProcessedAt], [DeadLettered], [NextAttemptAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Plans_ProductId_Code_Version] ON [catalog].[Plans] ([ProductId], [Code], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Plans_TenantId] ON [catalog].[Plans] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Products_TenantId_Code] ON [catalog].[Products] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_ExpiresAt] ON [identity].[RefreshTokens] ([ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [identity].[RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId_FamilyId] ON [identity].[RefreshTokens] ([UserId], [FamilyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SigningKeys_Kid] ON [licensing].[SigningKeys] ([Kid]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubscriptionHistory_SubscriptionId] ON [subscriptions].[SubscriptionHistory] ([SubscriptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Subscriptions_Status_EndDate] ON [subscriptions].[Subscriptions] ([Status], [EndDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Subscriptions_TenantId_CustomerId] ON [subscriptions].[Subscriptions] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Code] ON [tenants].[Tenants] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UsageDaily_TenantId_CustomerId_Day] ON [reporting].[UsageDaily] ([TenantId], [CustomerId], [Day]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [identity].[Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_TenantId] ON [identity].[Users] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003122130_InitialCreate'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261003122130_InitialCreate', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004065236_ProductPlatformsAndDeviceOs'
)
BEGIN
    ALTER TABLE [catalog].[Products] ADD [Platforms] nvarchar(200) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004065236_ProductPlatformsAndDeviceOs'
)
BEGIN
    ALTER TABLE [licensing].[LicenseActivations] ADD [OperatingSystem] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004065236_ProductPlatformsAndDeviceOs'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004065236_ProductPlatformsAndDeviceOs', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    IF SCHEMA_ID(N'media') IS NULL EXEC(N'CREATE SCHEMA [media];');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    ALTER TABLE [identity].[Users] ADD [ImageId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    ALTER TABLE [tenants].[Tenants] ADD [ImageId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    ALTER TABLE [catalog].[Products] ADD [Icon] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    ALTER TABLE [catalog].[Products] ADD [ImageId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    ALTER TABLE [customers].[Customers] ADD [ImageId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    CREATE TABLE [media].[MediaFiles] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [OwnerType] nvarchar(16) NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [ContentType] nvarchar(32) NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [Size] int NOT NULL,
        [Data] varbinary(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_MediaFiles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    CREATE INDEX [IX_MediaFiles_OwnerType_OwnerId] ON [media].[MediaFiles] ([OwnerType], [OwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090519_MediaImagesAndProductIcon'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004090519_MediaImagesAndProductIcon', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014143626_PlanKindAndCurrentOffer'
)
BEGIN
    ALTER TABLE [catalog].[Plans] ADD [IsCurrent] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014143626_PlanKindAndCurrentOffer'
)
BEGIN
    ALTER TABLE [catalog].[Plans] ADD [Kind] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014143626_PlanKindAndCurrentOffer'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014143626_PlanKindAndCurrentOffer', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017095925_ProductTrialDays'
)
BEGIN
    ALTER TABLE [catalog].[Products] ADD [TrialDays] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017095925_ProductTrialDays'
)
BEGIN
    EXEC(N'
    UPDATE p SET TrialDays = t.TrialDays
    FROM catalog.Products p
    CROSS APPLY (
        SELECT TOP 1 pl.TrialDays FROM catalog.Plans pl
        WHERE pl.ProductId = p.Id AND pl.Status = 2 AND pl.TrialDays > 0 AND pl.Kind <> 3
        ORDER BY CASE WHEN pl.Kind = 2 AND pl.IsCurrent = 1 THEN 0 ELSE 1 END, pl.Code, pl.Version DESC) t;

    UPDATE catalog.Plans SET Kind = 1, Status = 3, IsCurrent = 0 WHERE Kind = 2;');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017095925_ProductTrialDays'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[catalog].[Plans]') AND [c].[name] = N'TrialDays');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [catalog].[Plans] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [catalog].[Plans] DROP COLUMN [TrialDays];
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017095925_ProductTrialDays'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017095925_ProductTrialDays', N'10.0.12');
END;

COMMIT;
GO

