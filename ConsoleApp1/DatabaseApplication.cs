using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConsoleApp1.Configuration;
using ConsoleApp1.Services;

namespace ConsoleApp1
{
    public class DatabaseApplication : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DatabaseApplication> _logger;

        public DatabaseApplication()
        {
            _serviceProvider = ServiceConfiguration.ConfigureServices();
            _logger = _serviceProvider.GetRequiredService<ILogger<DatabaseApplication>>();
        }

        public async Task RunAsync()
        {
            try
            {
                _logger.LogInformation("Starting SmartFit Database Application...");

                // Ensure required directories exist
                var config = _serviceProvider.GetRequiredService<Models.DatabaseConfig>();
                ServiceConfiguration.EnsureDirectoriesExist(config);

                // Display current configuration
                ServiceConfiguration.DisplayConfiguration(config);

                var scriptRunner = _serviceProvider.GetRequiredService<ISqlScriptRunner>();

                // Display menu and handle user choices
                await ShowMenuAsync(scriptRunner);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while running the application");
                Console.WriteLine($"Application error: {ex.Message}");
            }
        }

        private async Task ShowMenuAsync(ISqlScriptRunner scriptRunner)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("SmartFit Database Management System");
                Console.WriteLine("==================================");
                Console.WriteLine("1. Run All Database Scripts");
                Console.WriteLine("2. Run All Scripts (Drop Existing Tables First) ??");
                Console.WriteLine("3. Run Scripts from Custom Folder");
                Console.WriteLine("4. Run Scripts from Custom Folder (Drop Tables First) ??");
                Console.WriteLine("5. Run Single Script");
                Console.WriteLine("6. Validate Script Order");
                Console.WriteLine("7. Show Existing Tables");
                Console.WriteLine("8. Drop All Tables ??");
                Console.WriteLine("9. Test SQL Server Connections");
                Console.WriteLine("10. Exit");
                Console.WriteLine();
                Console.WriteLine("??  = Destructive operations - use with caution!");
                Console.WriteLine();
                Console.Write("Please select an option (1-10): ");

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await RunAllScriptsAsync(scriptRunner);
                        break;
                    case "2":
                        await RunAllScriptsWithDropAsync(scriptRunner);
                        break;
                    case "3":
                        await RunScriptsFromCustomFolderAsync(scriptRunner);
                        break;
                    case "4":
                        await RunScriptsFromCustomFolderWithDropAsync(scriptRunner);
                        break;
                    case "5":
                        await RunSingleScriptAsync(scriptRunner);
                        break;
                    case "6":
                        await ValidateScriptOrderAsync(scriptRunner);
                        break;
                    case "7":
                        await ShowExistingTablesAsync(scriptRunner);
                        break;
                    case "8":
                        await DropAllTablesAsync(scriptRunner);
                        break;
                    case "9":
                        await TestSqlServerConnectionsAsync();
                        break;
                    case "10":
                        Console.WriteLine("Goodbye!");
                        return;
                    default:
                        Console.WriteLine("Invalid option. Please try again.");
                        break;
                }

                if (choice != "10")
                {
                    Console.WriteLine("\nPress any key to continue...");
                    Console.ReadKey();
                }
            }
        }

        private async Task RunAllScriptsAsync(ISqlScriptRunner scriptRunner)
        {
            Console.WriteLine("\nRunning all database scripts...");
            
            var summary = await scriptRunner.RunAllScriptsAsync();
            
            if (scriptRunner is SqlScriptRunner concreteRunner)
            {
                concreteRunner.PrintExecutionSummary(summary);
            }
        }

        private async Task RunAllScriptsWithDropAsync(ISqlScriptRunner scriptRunner)
        {
            Console.WriteLine("\n??  WARNING: This will drop all existing tables before running scripts!");
            Console.Write("Are you sure you want to continue? (yes/no): ");
            
            var confirmation = Console.ReadLine()?.Trim().ToLower();
            if (confirmation != "yes")
            {
                Console.WriteLine("Operation cancelled.");
                return;
            }

            Console.WriteLine("\nRunning all database scripts with table drop...");
            
            var summary = await scriptRunner.RunAllScriptsWithDropAsync();
            
            if (scriptRunner is SqlScriptRunner concreteRunner)
            {
                concreteRunner.PrintExecutionSummary(summary);
            }
        }

        private async Task RunScriptsFromCustomFolderAsync(ISqlScriptRunner scriptRunner)
        {
            Console.Write("\nEnter the folder path containing SQL scripts: ");
            var folderPath = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                Console.WriteLine("Invalid folder path.");
                return;
            }

            Console.WriteLine($"\nRunning scripts from folder: {folderPath}");
            
            var summary = await scriptRunner.RunScriptsFromFolderAsync(folderPath);
            
            if (scriptRunner is SqlScriptRunner concreteRunner)
            {
                concreteRunner.PrintExecutionSummary(summary);
            }
        }

        private async Task RunScriptsFromCustomFolderWithDropAsync(ISqlScriptRunner scriptRunner)
        {
            Console.Write("\nEnter the folder path containing SQL scripts: ");
            var folderPath = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                Console.WriteLine("Invalid folder path.");
                return;
            }

            Console.WriteLine("\n??  WARNING: This will drop all existing tables before running scripts!");
            Console.Write("Are you sure you want to continue? (yes/no): ");
            
            var confirmation = Console.ReadLine()?.Trim().ToLower();
            if (confirmation != "yes")
            {
                Console.WriteLine("Operation cancelled.");
                return;
            }

            Console.WriteLine($"\nRunning scripts from folder with table drop: {folderPath}");
            
            var summary = await scriptRunner.RunScriptsFromFolderWithDropAsync(folderPath);
            
            if (scriptRunner is SqlScriptRunner concreteRunner)
            {
                concreteRunner.PrintExecutionSummary(summary);
            }
        }

        private async Task RunSingleScriptAsync(ISqlScriptRunner scriptRunner)
        {
            Console.Write("\nEnter the full path to the SQL script: ");
            var scriptPath = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                Console.WriteLine("Invalid script path.");
                return;
            }

            Console.WriteLine($"\nRunning script: {scriptPath}");
            
            var success = await scriptRunner.RunSingleScriptAsync(scriptPath);
            
            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("? Script executed successfully!");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("? Script execution failed!");
            }
            Console.ResetColor();
        }

        private async Task ValidateScriptOrderAsync(ISqlScriptRunner scriptRunner)
        {
            Console.Write("\nEnter the folder path to validate (or press Enter for default): ");
            var folderPath = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                var config = _serviceProvider.GetRequiredService<Models.DatabaseConfig>();
                folderPath = config.ScriptsPath;
            }

            Console.WriteLine($"\nValidating script order in: {folderPath}");
            
            var isValid = await scriptRunner.ValidateScriptsOrderAsync(folderPath);
            
            if (isValid)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("? Script validation completed!");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("? Script validation failed!");
            }
            Console.ResetColor();
        }

        private async Task ShowExistingTablesAsync(ISqlScriptRunner scriptRunner)
        {
            Console.WriteLine("\nRetrieving existing tables...");
            await scriptRunner.ShowExistingTablesAsync();
        }

        private async Task DropAllTablesAsync(ISqlScriptRunner scriptRunner)
        {
            Console.WriteLine("\n=== Drop All Tables ===");
            await scriptRunner.DropAllTablesAsync();
        }

        private async Task TestSqlServerConnectionsAsync()
        {
            Console.WriteLine("\n=== SQL Server Connection Testing ===");
            var workingConnection = await ServiceConfiguration.FindWorkingConnectionString();
            
            if (!string.IsNullOrEmpty(workingConnection))
            {
                Console.WriteLine($"\n? Found working connection!");
                Console.WriteLine("You can update your appsettings.json with this connection string:");
                Console.WriteLine($"\"{workingConnection}\"");
                
                Console.WriteLine("\nWould you like to test database creation with this connection? (y/n): ");
                var response = Console.ReadLine();
                
                if (response?.ToLower() == "y")
                {
                    await TestDatabaseCreation(workingConnection);
                }
            }
            else
            {
                Console.WriteLine("\n? No working SQL Server connection found.");
                Console.WriteLine("Consider:");
                Console.WriteLine("1. Using SQLite (set DatabaseProvider to 'SQLite' in appsettings.json)");
                Console.WriteLine("2. Installing SQL Server Express");
                Console.WriteLine("3. Starting SQL Server service");
                Console.WriteLine("4. Enabling TCP/IP in SQL Server Configuration Manager");
            }
        }

        private async Task TestDatabaseCreation(string connectionString)
        {
            try
            {
                // Test if database exists, create if not
                var masterConnection = connectionString.Replace("Initial Catalog=SmartFitDB", "Initial Catalog=master");
                
                using var connection = new Microsoft.Data.SqlClient.SqlConnection(masterConnection);
                await connection.OpenAsync();
                
                var checkDbCommand = connection.CreateCommand();
                checkDbCommand.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = 'SmartFitDB'";
                var dbExists = (int)await checkDbCommand.ExecuteScalarAsync() > 0;
                
                if (!dbExists)
                {
                    Console.WriteLine("Creating SmartFitDB database...");
                    var createDbCommand = connection.CreateCommand();
                    createDbCommand.CommandText = "CREATE DATABASE SmartFitDB";
                    await createDbCommand.ExecuteNonQueryAsync();
                    Console.WriteLine("? Database created successfully!");
                }
                else
                {
                    Console.WriteLine("? SmartFitDB database already exists!");
                }
                
                // Test connection to the actual database and check tables
                using var dbConnection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                await dbConnection.OpenAsync();
                Console.WriteLine("? Connection to SmartFitDB successful!");
                
                // Check what tables exist
                await VerifyDatabaseSchema(dbConnection);
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Database creation/connection failed: {ex.Message}");
            }
        }

        private async Task VerifyDatabaseSchema(Microsoft.Data.SqlClient.SqlConnection connection)
        {
            try
            {
                Console.WriteLine("\n=== Database Schema Verification ===");
                
                var tablesCommand = connection.CreateCommand();
                tablesCommand.CommandText = @"
                    SELECT 
                        TABLE_NAME, 
                        TABLE_TYPE 
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_TYPE = 'BASE TABLE'
                    ORDER BY TABLE_NAME";
                
                using var reader = await tablesCommand.ExecuteReaderAsync();
                var tableCount = 0;
                
                Console.WriteLine("Existing tables in SmartFitDB:");
                while (await reader.ReadAsync())
                {
                    tableCount++;
                    Console.WriteLine($"  {tableCount}. {reader["TABLE_NAME"]}");
                }
                
                if (tableCount == 0)
                {
                    Console.WriteLine("  ? No tables found in database!");
                    Console.WriteLine("\nThis means either:");
                    Console.WriteLine("  1. Scripts failed to execute");
                    Console.WriteLine("  2. Wrong scripts were executed (SQLite syntax vs SQL Server)");
                    Console.WriteLine("  3. Scripts executed but had syntax errors");
                    
                    Console.WriteLine("\n=== Recommended Actions ===");
                    Console.WriteLine("1. Run the application again and select option 1");
                    Console.WriteLine("2. Check the detailed execution log");
                    Console.WriteLine("3. Manually run this query in SSMS to check:");
                    Console.WriteLine("   SELECT name FROM sys.tables;");
                }
                else
                {
                    Console.WriteLine($"\n? Found {tableCount} tables in database!");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Schema verification failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}