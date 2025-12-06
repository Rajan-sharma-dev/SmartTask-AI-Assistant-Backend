namespace ConsoleApp1.Models
{
    public class DatabaseConfig
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string ScriptsPath { get; set; } = "Scripts";
        public string DatabaseProvider { get; set; } = "SQLite"; // SQLite, SqlServer, etc.
    }

    public class ScriptExecutionResult
    {
        public string ScriptName { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExecutedAt { get; set; }
        public TimeSpan ExecutionTime { get; set; }
    }

    public class DatabaseExecutionSummary
    {
        public List<ScriptExecutionResult> Results { get; set; } = new();
        public int TotalScripts { get; set; }
        public int SuccessfulScripts { get; set; }
        public int FailedScripts { get; set; }
        public TimeSpan TotalExecutionTime { get; set; }
    }
}