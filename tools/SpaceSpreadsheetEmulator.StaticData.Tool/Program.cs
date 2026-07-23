using System.Globalization;
using SpaceSpreadsheetEmulator.StaticData;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length != 9
        || arguments[0] != "promote"
        || arguments[1] != "--source"
        || arguments[3] != "--output"
        || arguments[5] != "--build"
        || arguments[7] != "--sha256"
        || !int.TryParse(arguments[6], NumberStyles.None, CultureInfo.InvariantCulture, out int build))
    {
        Console.Error.WriteLine("Usage: sse-data promote --source <archive.zip> --output <directory> --build <build> --sha256 <hash>");
        return 2;
    }

    try
    {
        string output = await StaticDataPromoter.PromoteAsync(arguments[2], arguments[4], build, arguments[8]);
        Console.WriteLine(output);
        return 0;
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}
