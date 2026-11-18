using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Crm.Api.Data;
using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Roles = "Admin,Manager,Sales Executive")]
public sealed class CustomerImportController(
    CrmDbContext database,
    ITenantContext tenant,
    AuditService audit) : ControllerBase
{
    private const long MaximumUploadBytes = 10 * 1024 * 1024;
    private const int MaximumRows = 10_000;
    private const int MaximumReportedErrors = 100;

    [HttpPost("import")]
    [RequestSizeLimit(MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumUploadBytes)]
    public async Task<IActionResult> ImportCsv([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Upload a non-empty CSV file using the 'file' form field." });
        if (file.Length > MaximumUploadBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "CSV uploads are limited to 10 MB." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .csv files are supported by this import endpoint." });

        var errors = new List<object>();
        var parsed = new List<ImportRow>();
        var rowsRead = 0;
        try
        {
            await using var stream = file.OpenReadStream();
            using var parser = new TextFieldParser(stream);
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;
            parser.TrimWhiteSpace = false;

            if (parser.EndOfData)
                return BadRequest(new { error = "The CSV file has no header row." });
            var headerRow = parser.ReadFields();
            if (headerRow is null || headerRow.Length == 0)
                return BadRequest(new { error = "The CSV file has no header row." });
            var columns = headerRow
                .Select((name, index) => new { Name = NormalizeHeader(name), Index = index })
                .Where(row => row.Name.Length > 0)
                .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
            if (!columns.ContainsKey("name") && !columns.ContainsKey("customername"))
                return BadRequest(new { error = "The CSV must include a 'name' or 'customer_name' column." });

            while (!parser.EndOfData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fields = parser.ReadFields();
                if (fields is null || fields.All(string.IsNullOrWhiteSpace))
                    continue;
                rowsRead++;
                if (rowsRead > MaximumRows)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "A CSV import is limited to 10,000 data rows." });

                var rowNumber = checked((int)(parser.LineNumber - 1));
                var name = ReadField(fields, columns, "name", "customername")?.Trim();
                var email = NormalizeEmail(ReadField(fields, columns, "email"));
                var phone = Clean(ReadField(fields, columns, "phone"));
                var signupDateText = Clean(ReadField(fields, columns, "signupdate"));
                var city = Clean(ReadField(fields, columns, "city"));
                var channel = Clean(ReadField(fields, columns, "acquisitionchannel"));
                var status = NormalizeStatus(ReadField(fields, columns, "status"));
                var externalId = Clean(ReadField(fields, columns, "externalid"));

                var rowErrors = Validate(name, email, phone, signupDateText, city, channel, status, externalId, out var signupDate);
                if (rowErrors.Count > 0)
                {
                    AddError(errors, rowNumber, string.Join(" ", rowErrors));
                    continue;
                }

                parsed.Add(new ImportRow(rowNumber, name!, email, phone, signupDate, city, channel, status!, externalId));
            }
        }
        catch (MalformedLineException exception)
        {
            return BadRequest(new
            {
                error = "The CSV contains an invalid quoted or malformed row.",
                line = exception.LineNumber
            });
        }
        catch (IOException)
        {
            return BadRequest(new { error = "The CSV upload could not be read." });
        }

        var organizationId = tenant.RequireOrganizationId();
        var existingEmails = await database.Customers.AsNoTracking()
            .Where(row => row.Email != null)
            .Select(row => row.Email!)
            .ToListAsync(cancellationToken);
        var existingExternalIds = await database.Customers.AsNoTracking()
            .Where(row => row.ExternalId != null)
            .Select(row => row.ExternalId!)
            .ToListAsync(cancellationToken);
        var emailSet = new HashSet<string>(existingEmails, StringComparer.OrdinalIgnoreCase);
        var externalIdSet = new HashSet<string>(existingExternalIds, StringComparer.OrdinalIgnoreCase);
        var ownerUserId = User.IsInRole(Roles.SalesExecutive) ? tenant.RequireUserId() : (Guid?)null;
        var customers = new List<Customer>(parsed.Count);

        foreach (var row in parsed)
        {
            if ((row.Email is not null && emailSet.Contains(row.Email))
                || (row.ExternalId is not null && externalIdSet.Contains(row.ExternalId)))
            {
                AddError(errors, row.RowNumber, "Duplicate email or externalId; row skipped.");
                continue;
            }

            if (row.Email is not null)
                emailSet.Add(row.Email);
            if (row.ExternalId is not null)
                externalIdSet.Add(row.ExternalId);
            customers.Add(new Customer
            {
                OrganizationId = organizationId,
                Name = row.Name,
                Email = row.Email,
                Phone = row.Phone,
                SignupDate = row.SignupDate,
                City = row.City,
                AcquisitionChannel = row.AcquisitionChannel,
                Status = row.Status,
                ExternalId = row.ExternalId,
                OwnerUserId = ownerUserId,
                IsSynthetic = false
            });
        }

        if (customers.Count > 0)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            database.Customers.AddRange(customers);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await audit.RecordAsync(
                organizationId,
                "customers.imported",
                "Customer",
                entityId: null,
                details: new { createdCount = customers.Count, rowsRead },
                cancellationToken: cancellationToken);
        }

        return Ok(new
        {
            rowsRead,
            createdCount = customers.Count,
            skippedCount = rowsRead - customers.Count,
            errorCount = rowsRead - customers.Count,
            errors = errors.Take(MaximumReportedErrors)
        });
    }

    private static List<string> Validate(
        string? name,
        string? email,
        string? phone,
        string? signupDateText,
        string? city,
        string? channel,
        string? status,
        string? externalId,
        out DateOnly? signupDate)
    {
        var errors = new List<string>();
        signupDate = null;
        if (string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 160)
            errors.Add("Name must contain 2–160 characters.");
        if (email is not null && (!MailAddress.TryCreate(email, out var address)
            || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)))
            errors.Add("Email is invalid.");
        if (phone?.Length > 40) errors.Add("Phone cannot exceed 40 characters.");
        if (city?.Length > 100) errors.Add("City cannot exceed 100 characters.");
        if (channel?.Length > 60) errors.Add("Acquisition channel cannot exceed 60 characters.");
        if (externalId?.Length > 100) errors.Add("ExternalId cannot exceed 100 characters.");
        if (status is null)
            errors.Add("Status must be Active, Inactive, or At Risk.");
        if (signupDateText is not null)
        {
            if (!DateOnly.TryParseExact(signupDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                errors.Add("SignupDate must use yyyy-MM-dd.");
            else
                signupDate = parsedDate;
        }
        return errors;
    }

    private static string NormalizeHeader(string? value) =>
        Regex.Replace((value ?? "").Trim().TrimStart('\uFEFF').ToLowerInvariant(), "[^a-z0-9]", "");

    private static string? ReadField(string[] fields, IReadOnlyDictionary<string, int> columns, params string[] names)
    {
        foreach (var name in names)
        {
            if (columns.TryGetValue(name, out var index) && index < fields.Length)
                return Clean(fields[index]);
        }
        return null;
    }

    private static string? NormalizeEmail(string? value) => Clean(value)?.ToLowerInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "active" => "Active",
        "inactive" => "Inactive",
        "at risk" or "at_risk" or "atrisk" => "At Risk",
        _ => null
    };

    private static void AddError(List<object> errors, int rowNumber, string message)
    {
        if (errors.Count < MaximumReportedErrors)
            errors.Add(new { row = rowNumber, error = message });
    }

    private sealed record ImportRow(
        int RowNumber,
        string Name,
        string? Email,
        string? Phone,
        DateOnly? SignupDate,
        string? City,
        string? AcquisitionChannel,
        string Status,
        string? ExternalId);
}
