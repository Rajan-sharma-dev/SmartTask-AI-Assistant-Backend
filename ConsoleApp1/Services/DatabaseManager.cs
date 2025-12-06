using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ConsoleApp1.Models;

namespace ConsoleApp1.Services
{
    public interface IDatabaseManager
    {
        Task<DatabaseExecutionSummary> ExecuteScriptsAsync(string scriptsPath);
        Task<DatabaseExecutionSummary> ExecuteScriptsWithDropAsync(string scriptsPath);
        Task<bool> ExecuteSingleScriptAsync(string scriptPath);
        Task<bool> TestConnectionAsync();
        Task<bool> DropAllTablesAsync();
        Task<List<string>> GetExistingTablesAsync();
    }

    public class DatabaseManager : IDatabaseManager
    {
        private readonly DatabaseConfig _config;
        private readonly ILogger<DatabaseManager> _logger;

        public DatabaseManager(DatabaseConfig config, ILogger<DatabaseManager> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();
                _logger.LogInformation($"Database connection test successful for {_config.DatabaseProvider}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Database connection test failed for {_config.DatabaseProvider}");
                return false;
            }
        }

        public async Task<DatabaseExecutionSummary> ExecuteScriptsAsync(string scriptsPath)
        {
            return await ExecuteScriptsInternalAsync(scriptsPath, false);
        }

        public async Task<DatabaseExecutionSummary> ExecuteScriptsWithDropAsync(string scriptsPath)
        {
            return await ExecuteScriptsInternalAsync(scriptsPath, true);
        }

        private async Task<DatabaseExecutionSummary> ExecuteScriptsInternalAsync(string scriptsPath, bool dropTablesFirst)
        {
            var summary = new DatabaseExecutionSummary();
            var startTime = DateTime.Now;

            try
            {
                // Drop existing tables if requested
                if (dropTablesFirst)
                {
                    _logger.LogInformation("Dropping existing tables before script execution...");
                    var dropResult = await DropAllTablesAsync();
                    if (!dropResult)
                    {
                        _logger.LogWarning("Failed to drop some tables, but continuing with script execution...");
                    }
                }

                var scriptFiles = GetScriptFilesInOrder(scriptsPath);
                summary.TotalScripts = scriptFiles.Count;

                _logger.LogInformation($"Found {scriptFiles.Count} script files to execute");

                foreach (var scriptFile in scriptFiles)
                {
                    var result = await ExecuteScriptFileAsync(scriptFile);
                    summary.Results.Add(result);

                    if (result.IsSuccess)
                    {
                        summary.SuccessfulScripts++;
                        _logger.LogInformation($"Successfully executed: {result.ScriptName}");
                    }
                    else
                    {
                        summary.FailedScripts++;
                        _logger.LogError($"Failed to execute: {result.ScriptName} - {result.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during script execution batch");
            }

            summary.TotalExecutionTime = DateTime.Now - startTime;
            return summary;
        }

        public async Task<bool> ExecuteSingleScriptAsync(string scriptPath)
        {
            try
            {
                var result = await ExecuteScriptFileAsync(scriptPath);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing single script: {scriptPath}");
                return false;
            }
        }

        public async Task<List<string>> GetExistingTablesAsync()
        {
            var tables = new List<string>();
            
            try
            {
                using var connection = CreateConnection();
                await connection.OpenAsync();

                string query = _config.DatabaseProvider.ToLower() switch
                {
                    "sqlserver" => @"
                        SELECT TABLE_NAME 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_TYPE = 'BASE TABLE' 
                        AND TABLE_SCHEMA = 'dbo'
                        ORDER BY TABLE_NAME",
                    "sqlite" => @"
                        SELECT name 
                        FROM sqlite_master 
                        WHERE type = 'table' 
                        AND name NOT LIKE 'sqlite_%'
                        ORDER BY name",
                    _ => throw new NotSupportedException($"Database provider '{_config.DatabaseProvider}' is not supported for table listing")
                };

                using var command = connection.CreateCommand();
                command.CommandText = query;
                
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving existing tables");
            }

            return tables;
        }

        public async Task<bool> DropAllTablesAsync()
        {
            try
            {
                var tables = await GetExistingTablesAsync();
                
                if (tables.Count == 0)
                {
                    _logger.LogInformation("No tables found to drop");
                    return true;
                }

                _logger.LogInformation($"Found {tables.Count} tables to drop: {string.Join(", ", tables)}");

                using var connection = CreateConnection();
                await connection.OpenAsync();

                if (_config.DatabaseProvider.ToLower() == "sqlserver")
                {
                    return await DropSqlServerTablesAsync(connection, tables);
                }
                else if (_config.DatabaseProvider.ToLower() == "sqlite")
                {
                    return await DropSqliteTablesAsync(connection, tables);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dropping tables");
                return false;
            }
        }

        private async Task<bool> DropSqlServerTablesAsync(DbConnection connection, List<string> tables)
        {
            using var transaction = await connection.BeginTransactionAsync();
            
            try
            {
                // First, drop foreign key constraints
                var dropConstraintsQuery = @"
                    DECLARE @sql NVARCHAR(MAX) = ''
                    SELECT @sql = @sql + 'ALTER TABLE [' + OBJECT_SCHEMA_NAME(parent_object_id) + '].[' + OBJECT_NAME(parent_object_id) + '] DROP CONSTRAINT [' + name + '];' + CHAR(13)
                    FROM sys.foreign_keys
                    WHERE referenced_object_id IN (SELECT object_id FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo'))
                    EXEC sp_executesql @sql";

                using var constraintCommand = connection.CreateCommand();
                constraintCommand.Transaction = transaction;
                constraintCommand.CommandText = dropConstraintsQuery;
                await constraintCommand.ExecuteNonQueryAsync();
                
                _logger.LogInformation("Dropped foreign key constraints");

                // Then drop tables
                foreach (var table in tables)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = $"DROP TABLE IF EXISTS [{table}]";
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Dropped table: {table}");
                }

                await transaction.CommitAsync();
                _logger.LogInformation($"Successfully dropped {tables.Count} tables");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to drop SQL Server tables");
                return false;
            }
        }

        private async Task<bool> DropSqliteTablesAsync(DbConnection connection, List<string> tables)
        {
            using var transaction = await connection.BeginTransactionAsync();
            
            try
            {
                // SQLite doesn't support DROP TABLE IF EXISTS in older versions
                // So we'll check existence first
                foreach (var table in tables)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = $"DROP TABLE IF EXISTS [{table}]";
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Dropped table: {table}");
                }

                await transaction.CommitAsync();
                _logger.LogInformation($"Successfully dropped {tables.Count} tables");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to drop SQLite tables");
                return false;
            }
        }

        private async Task<ScriptExecutionResult> ExecuteScriptFileAsync(string scriptPath)
        {
            var result = new ScriptExecutionResult
            {
                ScriptName = Path.GetFileName(scriptPath),
                ExecutedAt = DateTime.Now
            };

            var startTime = DateTime.Now;

            try
            {
                if (!File.Exists(scriptPath))
                {
                    throw new FileNotFoundException($"Script file not found: {scriptPath}");
                }

                var sqlContent = await File.ReadAllTextAsync(scriptPath);
                
                if (string.IsNullOrWhiteSpace(sqlContent))
                {
                    throw new InvalidOperationException("Script file is empty");
                }

                using var connection = CreateConnection();
                await connection.OpenAsync();

                // Split the script by GO statements or semicolons for better compatibility
                var commands = SplitSqlCommands(sqlContent);

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    foreach (var commandText in commands)
                    {
                        if (string.IsNullOrWhiteSpace(commandText)) continue;

                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = commandText.Trim();
                        
                        await command.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    result.IsSuccess = true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, $"Error executing script: {scriptPath}");
            }

            result.ExecutionTime = DateTime.Now - startTime;
            return result;
        }

        private List<string> GetScriptFilesInOrder(string scriptsPath)
        {
            if (!Directory.Exists(scriptsPath))
            {
                throw new DirectoryNotFoundException($"Scripts directory not found: {scriptsPath}");
            }

            var allScriptFiles = Directory.GetFiles(scriptsPath, "*.sql", SearchOption.TopDirectoryOnly);
            var selectedScripts = new List<string>();

            // Get script numbers and prioritize provider-specific scripts
            var scriptGroups = allScriptFiles
                .Select(f => new
                {
                    FullPath = f,
                    FileName = Path.GetFileName(f),
                    ScriptNumber = ExtractScriptNumber(Path.GetFileName(f)),
                    IsProviderSpecific = IsProviderSpecificScript(Path.GetFileName(f))
                })
                .Where(s => s.ScriptNumber.HasValue)
                .GroupBy(s => s.ScriptNumber.Value)
                .OrderBy(g => g.Key);

            foreach (var scriptGroup in scriptGroups)
            {
                // First, try to find provider-specific script
                var providerSpecific = scriptGroup.FirstOrDefault(s => s.IsProviderSpecific);
                if (providerSpecific != null)
                {
                    selectedScripts.Add(providerSpecific.FullPath);
                    _logger.LogInformation($"Selected provider-specific script: {providerSpecific.FileName}");
                }
                else
                {
                    // Fallback to generic script
                    var generic = scriptGroup.FirstOrDefault(s => !s.IsProviderSpecific);
                    if (generic != null)
                    {
                        selectedScripts.Add(generic.FullPath);
                        _logger.LogInformation($"Selected generic script: {generic.FileName}");
                    }
                }
            }

            return selectedScripts;
        }

        private int? ExtractScriptNumber(string fileName)
        {
            // Extract number from filenames like "001_Script_Name.sql"
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"^(\d{3})_");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
            {
                return number;
            }
            return null;
        }

        private bool IsProviderSpecificScript(string fileName)
        {
            var providerSuffix = $"_{_config.DatabaseProvider}";
            return fileName.Contains(providerSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private List<string> SplitSqlCommands(string sqlContent)
        {
            var commands = new List<string>();
            
            if (_config.DatabaseProvider.ToLower() == "sqlserver")
            {
                // For SQL Server, split by GO statements
                var parts = sqlContent.Split(new[] { "\nGO\n", "\nGO\r\n", "\rGO\r", "\nGO ", " GO\n", " GO " }, 
                    StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && 
                        !trimmed.StartsWith("--") && 
                        !trimmed.StartsWith("/*"))
                    {
                        commands.Add(trimmed);
                    }
                }
            }
            else
            {
                // For SQLite and others, split by semicolons
                var parts = sqlContent.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && 
                        !trimmed.StartsWith("--") && 
                        !trimmed.StartsWith("/*"))
                    {
                        commands.Add(trimmed);
                    }
                }
            }

            return commands;
        }

        private DbConnection CreateConnection()
        {
            return _config.DatabaseProvider.ToLower() switch
            {
                "sqlserver" => new SqlConnection(_config.ConnectionString),
               
            };
        }
    }
}