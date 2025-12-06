using System.Data;
using Microsoft.Data.SqlClient;

namespace MiddleWareWebApi.data
{
    // C#
    public class DapperContext
    {
        private readonly IConfiguration _config;
        public DapperContext(IConfiguration config) => _config = config;

        public IDbConnection CreateConnection()
        {
            var connStr = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' is not configured. " +
                    "Set it in appsettings.json or as the environment variable 'ConnectionStrings__DefaultConnection'.");
            }

            // Return connection without opening it. Let callers open/close as needed.
            return new SqlConnection(connStr);
        }
    }
}
