using System.Globalization;
using Crm.Api.Models;

namespace Crm.Api.Services;

public sealed class CsvCustomerRepository : ICustomerRepository
{
    private readonly string _csvPath = Path.Combine(AppContext.BaseDirectory, "data", "demo", "customers.csv");

    public async Task<IReadOnlyList<CustomerRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_csvPath))
        {
            throw new FileNotFoundException(
                $"Demo customer CSV was not found at '{_csvPath}'. Run scripts/generate_demo_data.py first.");
        }

        var lines = await File.ReadAllLinesAsync(_csvPath, cancellationToken);
        var customers = new List<CustomerRecord>(Math.Max(0, lines.Length - 1));
        foreach (var line in lines.Skip(1).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            // The generated demo columns contain no commas or quoted fields.
            var columns = line.Split(',');
            if (columns.Length != 4 || !DateOnly.TryParseExact(
                    columns[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var signupDate))
            {
                continue;
            }

            customers.Add(new CustomerRecord(columns[0], signupDate, columns[2], columns[3]));
        }

        return customers;
    }
}

