namespace Crm.Api.Models;

public sealed record AssistantRequestDto(
    string CustomerId,
    string Question,
    int Clusters = 4);

