using Microsoft.Extensions.Logging;
using ConsoleApp1.Models;
using ConsoleApp1.Services;

namespace ConsoleApp1.Services
{
    public interface ISqlScriptRunner
    {
        Task<DatabaseExecutionSummary> RunAllScriptsAsync();
        Task<DatabaseExecutionSummary> RunAllScriptsWithDropAsync();
        Task<DatabaseExecutionSummary> RunScriptsFromFolderAsync(string folderPath);
        Task<DatabaseExecutionSummary> RunScriptsFromFolderWithDropAsync(string folderPath);
        Task<bool> RunSingleScriptAsync(string scriptPath);
        Task<bool> ValidateScriptsOrderAsync(string folderPath);
        Task<bool> ShowExistingTablesAsync();
        Task<bool> DropAllTablesAsync();
    }

    public class SqlScriptRunner : ISqlScriptRunner
    {
        private readonly IDatabaseManager _databaseManager;
        private readonly DatabaseConfig _config;
        private readonly ILogger<SqlScriptRunner> _logger;

        public SqlScriptRunner(
            IDatabaseManager databaseManager, 
            DatabaseConfig config,
            ILogger<SqlScriptRunner> logger)
        {
            _databaseManager = databaseManager ?? throw new ArgumentNullException(nameof(databaseManager));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DatabaseExecutionSummary> RunAllScriptsAsync()
        {
            _logger.LogInformation($"Starting script execution from default folder: {_config.ScriptsPath}");
            
            // Test connection first
            if (!await _databaseManager.TestConnectionAsync())
            {
                _logger.LogError("Database connection failed. Aborting script execution.");
                return new DatabaseExecutionSummary();
            }

            return await _databaseManager.ExecuteScriptsAsync(_config.ScriptsPath);
        }

        public async Task<DatabaseExecutionSummary> RunAllScriptsWithDropAsync()
        {
            _logger.LogInformation($"Starting script execution with table drop from default folder: {_config.ScriptsPath}");
            
            // Test connection first
            if (!await _databaseManager.TestConnectionAsync())
            {
                _logger.LogError("Database connection failed. Aborting script execution.");
                return new DatabaseExecutionSummary();
            }

            return await _databaseManager.ExecuteScriptsWithDropAsync(_config.ScriptsPath);
        }

        public async Task<DatabaseExecutionSummary> RunScriptsFromFolderAsync(string folderPath)
        {
            _logger.LogInformation($"Starting script execution from folder: {folderPath}");
            
            if (!Directory.Exists(folderPath))
            {
                _logger.LogError($"Scripts folder does not exist: {folderPath}");
                return new DatabaseExecutionSummary();
            }

            // Test connection first
            if (!await _databaseManager.TestConnectionAsync())
            {
                _logger.LogError("Database connection failed. Aborting script execution.");
                return new DatabaseExecutionSummary();
            }

            return await _databaseManager.ExecuteScriptsAsync(folderPath);
        }

        public async Task<DatabaseExecutionSummary> RunScriptsFromFolderWithDropAsync(string folderPath)
        {
            _logger.LogInformation($"Starting script execution with table drop from folder: {folderPath}");
            
            if (!Directory.Exists(folderPath))
            {
                _logger.LogError($"Scripts folder does not exist: {folderPath}");
                return new DatabaseExecutionSummary();
            }

            // Test connection first
            if (!await _databaseManager.TestConnectionAsync())
            {
                _logger.LogError("Database connection failed. Aborting script execution.");
                return new DatabaseExecutionSummary();
            }

            return await _databaseManager.ExecuteScriptsWithDropAsync(folderPath);
        }

        public async Task<bool> RunSingleScriptAsync(string scriptPath)
        {
            _logger.LogInformation($"Executing single script: {scriptPath}");
            
            if (!File.Exists(scriptPath))
            {
                _logger.LogError($"Script file does not exist: {scriptPath}");
                return false;
            }

            // Test connection first
            if (!await _databaseManager.TestConnectionAsync())
            {
                _logger.LogError("Database connection failed. Aborting script execution.");
                return false;
            }

            return await _databaseManager.ExecuteSingleScriptAsync(scriptPath);
        }

        public async Task<bool> ValidateScriptsOrderAsync(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    _logger.LogError($"Scripts folder does not exist: {folderPath}");
                    return false;
                }

                var scriptFiles = Directory.GetFiles(folderPath, "*.sql", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .OrderBy(f => f)
                    .ToList();

                _logger.LogInformation($"Found {scriptFiles.Count} SQL scripts:");
                
                for (int i = 0; i < scriptFiles.Count; i++)
                {
                    var expectedPrefix = $"{(i + 1):D3}_";
                    var fileName = scriptFiles[i];
                    
                    if (!fileName!.StartsWith(expectedPrefix))
                    {
                        _logger.LogWarning($"Script naming convention warning: {fileName} should start with {expectedPrefix}");
                    }
                    
                    _logger.LogInformation($"  {i + 1}. {fileName}");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating script order");
                return false;
            }
        }

        public async Task<bool> ShowExistingTablesAsync()
        {
            try
            {
                var tables = await _databaseManager.GetExistingTablesAsync();
                
                if (tables.Count == 0)
                {
                    Console.WriteLine("No tables found in the database.");
                    return true;
                }

                Console.WriteLine($"\nExisting tables in database ({tables.Count} total):");
                Console.WriteLine(new string('-', 50));
                
                for (int i = 0; i < tables.Count; i++)
                {
                    Console.WriteLine($"  {i + 1:D2}. {tables[i]}");
                }
                
                Console.WriteLine(new string('-', 50));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving existing tables");
                Console.WriteLine($"Error retrieving tables: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DropAllTablesAsync()
        {
            try
            {
                var tables = await _databaseManager.GetExistingTablesAsync();
                
                if (tables.Count == 0)
                {
                    Console.WriteLine("No tables found to drop.");
                    return true;
                }

                Console.WriteLine($"\nThe following {tables.Count} tables will be dropped:");
                foreach (var table in tables)
                {
                    Console.WriteLine($"  - {table}");
                }

                Console.WriteLine("\n??  WARNING: This action cannot be undone!");
                Console.Write("Are you sure you want to drop all tables? (yes/no): ");
                
                var confirmation = Console.ReadLine()?.Trim().ToLower();
                
                if (confirmation != "yes")
                {
                    Console.WriteLine("Operation cancelled.");
                    return false;
                }

                Console.WriteLine("\nDropping tables...");
                var result = await _databaseManager.DropAllTablesAsync();
                
                if (result)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("? All tables dropped successfully!");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("? Failed to drop some tables. Check logs for details.");
                    Console.ResetColor();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during table drop operation");
                Console.WriteLine($"Error dropping tables: {ex.Message}");
                return false;
            }
        }

        public void PrintExecutionSummary(DatabaseExecutionSummary summary)
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("DATABASE SCRIPT EXECUTION SUMMARY");
            Console.WriteLine(new string('=', 60));
            
            Console.WriteLine($"Total Scripts: {summary.TotalScripts}");
            Console.WriteLine($"Successful: {summary.SuccessfulScripts}");
            Console.WriteLine($"Failed: {summary.FailedScripts}");
            Console.WriteLine($"Total Execution Time: {summary.TotalExecutionTime.TotalSeconds:F2} seconds");
            
            if (summary.Results.Any())
            {
                Console.WriteLine("\nDetailed Results:");
                Console.WriteLine(new string('-', 60));
                
                foreach (var result in summary.Results)
                {
                    var status = result.IsSuccess ? "? SUCCESS" : "? FAILED";
                    var color = result.IsSuccess ? ConsoleColor.Green : ConsoleColor.Red;
                    
                    Console.ForegroundColor = color;
                    Console.Write($"[{status}]");
                    Console.ResetColor();
                    
                    Console.WriteLine($" {result.ScriptName} ({result.ExecutionTime.TotalMilliseconds:F0}ms)");
                    
                    if (!result.IsSuccess && !string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"    Error: {result.ErrorMessage}");
                        Console.ResetColor();
                    }
                }
            }
            
            Console.WriteLine(new string('=', 60));
        }
    }
}