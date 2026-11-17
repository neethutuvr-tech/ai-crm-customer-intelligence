using Crm.Api.Models;

namespace Crm.Api.Services;

public interface ICustomerRepository
{
    Task<IReadOnlyList<CustomerRecord>> GetAllAsync(CancellationToken cancellationToken);
}

