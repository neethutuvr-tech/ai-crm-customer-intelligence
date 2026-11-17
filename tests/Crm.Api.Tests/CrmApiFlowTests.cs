using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Crm.Api.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CrmApiFlowTests
{
    [TestMethod]
    public async Task RegistrationCustomerCrudAndTenantRolesWorkEndToEnd()
    {
        using var factory = new CrmApiFactory();
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"admin-{Guid.NewGuid():N}@example.invalid";

        var registration = await admin.PostAsJsonAsync("/api/auth/register", new
        {
            organizationName = "Test Organization",
            displayName = "Test Administrator",
            email,
            password = "SecureTest!5982"
        });
        Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);
        var adminProfile = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.AreEqual(email, adminProfile.GetProperty("email").GetString());

        var csrfToken = await GetCsrfTokenAsync(admin);
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);
        var userEmail = $"sales-{Guid.NewGuid():N}@example.invalid";
        var createdUser = await admin.PostAsJsonAsync("/api/users", new
        {
            displayName = "Sales User",
            email = userEmail,
            temporaryPassword = "Temporary!5982Pass",
            role = "Sales Executive"
        });
        Assert.AreEqual(HttpStatusCode.Created, createdUser.StatusCode);
        var userJson = await createdUser.Content.ReadFromJsonAsync<JsonElement>();
        var salesUserId = userJson.GetProperty("id").GetGuid();

        var createdCustomer = await admin.PostAsJsonAsync("/api/customers", new
        {
            name = "Test Customer",
            email = $"customer-{Guid.NewGuid():N}@example.invalid",
            city = "Test City",
            acquisitionChannel = "Web",
            ownerUserId = salesUserId
        });
        Assert.AreEqual(HttpStatusCode.Created, createdCustomer.StatusCode);
        var customerJson = await createdCustomer.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customerJson.GetProperty("id").GetGuid();

        var customers = await admin.GetFromJsonAsync<JsonElement>("/api/customers?page=1&pageSize=10");
        Assert.AreEqual(1, customers.GetProperty("total").GetInt32());
        Assert.AreEqual(customerId, customers.GetProperty("items")[0].GetProperty("id").GetGuid());

        var insights = await admin.GetFromJsonAsync<JsonElement>("/api/insights");
        Assert.AreEqual("tenant_database", insights.GetProperty("dataSource").GetString());
        Assert.AreEqual(1, insights.GetProperty("customerCount").GetInt32());
        Assert.AreEqual(1, insights.GetProperty("realCustomerCount").GetInt32());
        Assert.AreEqual("not_evaluated", insights.GetProperty("churnModel").GetProperty("status").GetString());
        var insufficientSegmentation = await admin.GetAsync("/api/insights/segmentation?clusters=2");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, insufficientSegmentation.StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/customers", new
        {
            name = "Duplicate Customer",
            email = customerJson.GetProperty("email").GetString()
        });
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var sales = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await sales.PostAsJsonAsync("/api/auth/login", new
        {
            email = userEmail,
            password = "Temporary!5982Pass"
        });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        var ownedCustomers = await sales.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.AreEqual(1, ownedCustomers.GetProperty("total").GetInt32());
        Assert.AreEqual(customerId, ownedCustomers.GetProperty("items")[0].GetProperty("id").GetGuid());

        using var otherOrganization = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var secondRegistration = await otherOrganization.PostAsJsonAsync("/api/auth/register", new
        {
            organizationName = "Other Organization",
            displayName = "Other Administrator",
            email = $"other-{Guid.NewGuid():N}@example.invalid",
            password = "AnotherSecure!5912"
        });
        Assert.AreEqual(HttpStatusCode.Created, secondRegistration.StatusCode);
        var isolatedCustomers = await otherOrganization.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.AreEqual(0, isolatedCustomers.GetProperty("total").GetInt32());
        var crossTenantProfile = await otherOrganization.GetAsync($"/api/customers/{customerId}");
        Assert.AreEqual(HttpStatusCode.NotFound, crossTenantProfile.StatusCode);

        var anonymous = factory.CreateClient();
        var anonymousResponse = await anonymous.GetAsync("/api/customers");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        return response.GetProperty("token").GetString()!;
    }
}
