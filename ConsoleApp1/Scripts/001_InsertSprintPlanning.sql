CREATE TABLE SprintSessions (
    SprintId INT IDENTITY(1,1) PRIMARY KEY,
    UserId INT NOT NULL,
    SessionName NVARCHAR(100) NOT NULL,
    SessionType NVARCHAR(50) NOT NULL, -- 'Sprint', 'Interval', 'Endurance', 'Recovery'
    PlannedDate DATE NOT NULL,
    ActualDate DATE NULL,
    Duration INT NULL, -- Total session duration in minutes
    Status NVARCHAR(20) DEFAULT 'Planned', -- 'Planned', 'InProgress', 'Completed', 'Cancelled'
    Notes NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    UpdatedAt DATETIME2 DEFAULT GETDATE()
)
GO
