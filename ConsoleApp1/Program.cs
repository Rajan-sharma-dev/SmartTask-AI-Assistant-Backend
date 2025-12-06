// SmartFit Database Management Console Application
// This application provides centralized database operations for table INSERT/UPDATE
// with sequential script execution from a specified folder structure.

partial class Program
{
    static async Task Main()
    {
        try
        {
            using var app = new ConsoleApp1.DatabaseApplication();
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Application startup failed: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
