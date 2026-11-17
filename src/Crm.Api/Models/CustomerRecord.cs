namespace Crm.Api.Models;

public sealed record CustomerRecord(
    string CustomerId,
    DateOnly SignupDate,
    string City,
    string AcquisitionChannel);

