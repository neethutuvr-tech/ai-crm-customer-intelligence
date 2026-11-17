using System.ComponentModel.DataAnnotations;

namespace Crm.Api.Models;

public sealed record AssistantRequestDto(
    [property: Required, StringLength(36, MinimumLength = 32)] string CustomerId,
    [property: Required, StringLength(800, MinimumLength = 5)] string Question);

