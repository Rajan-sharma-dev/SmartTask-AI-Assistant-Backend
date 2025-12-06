using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ConsoleApp1.Models;
using ConsoleApp1.Services;

namespace ConsoleApp1.Configuration
{
    public static class ServiceConfiguration
    {
        public static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configure configuration
            var configuration = BuildConfiguration();
            services.AddSingleton<IConfiguration>(configuration);

            // Configure logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Configure database settings
            var databaseConfig = new DatabaseConfig
            {
                ConnectionString = GetConnectionString(configuration),
                ScriptsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts"),
                DatabaseProvider = GetDatabaseProvider(configuration)
            };

            services.AddSingleton(databaseConfig);

            // Register services
            services.AddScoped<IDatabaseManager, DatabaseManager>();
            services.AddScoped<ISqlScriptRunner, SqlScriptRunner>();

            return services.BuildServiceProvider();
        }

        private static IConfiguration BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            return builder.Build();
        }

        private static string GetDatabaseProvider(IConfiguration configuration)
        {
            // 1. Check environment variable first
            var envProvider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER");
            if (!string.IsNullOrEmpty(envProvider))
            {
                return envProvider;
            }

            // 2. Check configuration section
            var configProvider = configuration["DatabaseProvider"];
            if (!string.IsNullOrEmpty(configProvider))
            {
                return configProvider;
            }

            // 3. Auto-detect from connection string
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrEmpty(connectionString))
            {
                // Check if it's SQL Server connection string
                if (connectionString.Contains("Server=") || 
                    connectionString.Contains("Data Source=") && connectionString.Contains("Initial Catalog="))
                {
                    return "SqlServer";
                }
                
                // Check if it's SQLite connection string
                if (connectionString.Contains("Data Source=") && connectionString.EndsWith(".db"))
                {
                    return "SQLite";
                }
            }

            // 4. Default fallback
            return "SQLite";
        }

        private static string GetConnectionString(IConfiguration configuration)
        {
            var provider = GetDatabaseProvider(configuration);
            
            return provider.ToLower() switch
            {
                "sqlserver" => GetSqlServerConnectionString(configuration),
                "sqlite" => GetSqliteConnectionString(configuration),
                _ => GetSqliteConnectionString(configuration) // Default fallback
            };
        }

        private static string GetSqlServerConnectionString(IConfiguration configuration)
        {
            // 1. Check if there's a connection string in appsettings.json
            var configConnectionString = configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrEmpty(configConnectionString))
            {
                return configConnectionString;
            }

            // 2. Try common SQL Server Express connection strings
            var server = Environment.GetEnvironmentVariable("SQL_SERVER") ?? GetSqlServerInstance();
            var database = Environment.GetEnvironmentVariable("SQL_DATABASE") ?? "SmartFitDB";
            var userId = Environment.GetEnvironmentVariable("SQL_USER_ID");
            var password = Environment.GetEnvironmentVariable("SQL_PASSWORD");
            var integratedSecurity = Environment.GetEnvironmentVariable("SQL_INTEGRATED_SECURITY") ?? "true";

            if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(password))
            {
                // SQL Server Authentication
                return $"Server={server};Database={database};User Id={userId};Password={password};TrustServerCertificate=true;";
            }
            else
            {
                // Windows Authentication (Integrated Security)
                return $"Server={server};Database={database};Integrated Security={integratedSecurity};TrustServerCertificate=true;";
            }
        }

        private static string GetSqlServerInstance()
        {
            // Try common SQL Server instance names
            var commonInstances = new[]
            {
                "localhost\\SQLEXPRESS",
                "(localdb)\\MSSQLLocalDB",
                "localhost",
                ".\\SQLEXPRESS",
                "(local)\\SQLEXPRESS",
                "."
            };

            return commonInstances[0]; // Default to SQLEXPRESS
        }

        private static string GetSqliteConnectionString(IConfiguration configuration)
        {
            // 1. Check if there's a SQLite connection string in appsettings.json
            var configConnectionString = configuration.GetConnectionString("SqliteConnection");
            if (!string.IsNullOrEmpty(configConnectionString))
            {
                return configConnectionString;
            }

            // 2. Default SQLite connection string
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SmartFitDB.db");
            return $"Data Source={dbPath};";
        }

        public static void EnsureDirectoriesExist(DatabaseConfig config)
        {
            // Ensure Scripts directory exists
            if (!Directory.Exists(config.ScriptsPath))
            {
                Directory.CreateDirectory(config.ScriptsPath);
            }

            // For SQLite, ensure database directory exists
            if (config.DatabaseProvider.ToLower() == "sqlite")
            {
                var connectionString = config.ConnectionString;
                if (connectionString.Contains("Data Source="))
                {
                    var dbPath = connectionString.Split("Data Source=")[1].Split(';')[0];
                    var dbDirectory = Path.GetDirectoryName(dbPath);
                    
                    if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
                    {
                        Directory.CreateDirectory(dbDirectory);
                    }
                }
            }
        }

        // Helper method to display current configuration
        public static void DisplayConfiguration(DatabaseConfig config)
        {
            Console.WriteLine("=== Database Configuration ===");
            Console.WriteLine($"Provider: {config.DatabaseProvider}");
            Console.WriteLine($"Scripts Path: {config.ScriptsPath}");
            
            // Only show connection string without sensitive information
            var safeConnectionString = config.ConnectionString;
            if (safeConnectionString.Contains("Password="))
            {
                safeConnectionString = System.Text.RegularExpressions.Regex.Replace(
                    safeConnectionString, 
                    @"Password=[^;]*", 
                    "Password=***");
            }
            Console.WriteLine($"Connection: {safeConnectionString}");
            
            // Show detection logic
            Console.WriteLine("\n=== Configuration Source ===");
            var envProvider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER");
            if (!string.IsNullOrEmpty(envProvider))
            {
                Console.WriteLine($"Database Provider: Environment Variable ({envProvider})");
            }
            else
            {
                Console.WriteLine("Database Provider: Auto-detected from connection string");
            }

            // Show SQL Server diagnostic information
            if (config.DatabaseProvider.ToLower() == "sqlserver")
            {
                Console.WriteLine("\n=== SQL Server Diagnostics ===");
                Console.WriteLine("If connection fails, try these steps:");
                Console.WriteLine("1. Ensure SQL Server service is running");
                Console.WriteLine("2. Enable TCP/IP in SQL Server Configuration Manager");
                Console.WriteLine("3. Check Windows Firewall settings");
                Console.WriteLine("4. Verify instance name (common: SQLEXPRESS, MSSQLLocalDB)");
                
                Console.WriteLine("\n=== Common Connection Strings to Try ===");
                var commonConnections = new[]
                {
                    "Data Source=localhost\\SQLEXPRESS;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;",
                    "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;",
                    "Data Source=.\\SQLEXPRESS;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;",
                    "Data Source=(local);Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;"
                };
                
                for (int i = 0; i < commonConnections.Length; i++)
                {
                    Console.WriteLine($"{i + 1}. {commonConnections[i]}");
                }
            }
            
            Console.WriteLine("==============================\n");
        }

        public static async Task<string> FindWorkingConnectionString()
        {
            var commonConnections = new[]
            {
                "Data Source=localhost\\SQLEXPRESS;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;",
                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;",
                "Data Source=.\\SQLEXPRESS;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;",
                "Data Source=(local)\\SQLEXPRESS;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;",
                "Data Source=localhost;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;",
                "Data Source=.;Initial Catalog=SmartFitDB;Integrated Security=True;TrustServerCertificate=True;"
            };

            Console.WriteLine("Testing SQL Server connection strings...\n");

            foreach (var connectionString in commonConnections)
            {
                try
                {
                    using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                    Console.Write($"Testing: {connectionString.Split(';')[0]}... ");
                    await connection.OpenAsync();
                    Console.WriteLine("? SUCCESS");
                    return connectionString;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"? FAILED: {ex.Message.Split('.')[0]}");
                }
            }

            Console.WriteLine("\nNo working SQL Server connection found. Consider using SQLite instead.");
            return string.Empty;
        }
    }
}