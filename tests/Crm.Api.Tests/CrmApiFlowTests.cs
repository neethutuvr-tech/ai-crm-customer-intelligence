using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Crm.Api.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CrmApiFlowTests
{
    [TestMethod]
    public async Task CustomerTransactionsInteractionsAndFollowUpsPersist()
    {
        using var factory = new CrmApiFactory();
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RegisterAdminAsync(admin, "activity");
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync(admin));

        var customerResponse = await admin.PostAsJsonAsync("/api/customers", new { name = "Activity Customer" });
        Assert.AreEqual(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = customer.GetProperty("id").GetGuid();

        var transaction = await admin.PostAsJsonAsync($"/api/customers/{customerId}/transactions", new
        {
            externalTransactionId = $"ORDER-{Guid.NewGuid():N}",
            orderDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-4)),
            amount = 82.50m,
            category = "Renewal"
        });
        Assert.AreEqual(HttpStatusCode.Created, transaction.StatusCode);

        var interaction = await admin.PostAsJsonAsync($"/api/customers/{customerId}/interactions", new
        {
            type = "support_case",
            summary = "Customer asked about account setup.",
            outcome = "Guidance sent"
        });
        Assert.AreEqual(HttpStatusCode.Created, interaction.StatusCode);

        var followUp = await admin.PostAsJsonAsync($"/api/customers/{customerId}/follow-ups", new
        {
            subject = "Call about renewal",
            dueAtUtc = DateTimeOffset.UtcNow.AddDays(2)
        });
        Assert.AreEqual(HttpStatusCode.Created, followUp.StatusCode);
        var followUpJson = await followUp.Content.ReadFromJsonAsync<JsonElement>();
        var followUpId = followUpJson.GetProperty("id").GetGuid();

        var complete = await admin.PatchAsync($"/api/customers/{customerId}/follow-ups/{followUpId}/complete", null);
        Assert.AreEqual(HttpStatusCode.OK, complete.StatusCode);

        var note = await admin.PostAsJsonAsync($"/api/customers/{customerId}/notes", new { body = "Discussed onboarding." });
        Assert.AreEqual(HttpStatusCode.Created, note.StatusCode);

        var profile = await admin.GetFromJsonAsync<JsonElement>($"/api/customers/{customerId}");
        Assert.AreEqual(1, profile.GetProperty("transactions").GetArrayLength());
        Assert.AreEqual(1, profile.GetProperty("interactions").GetArrayLength());
        Assert.AreEqual(1, profile.GetProperty("notes").GetArrayLength());
        Assert.IsNotNull(profile.GetProperty("followUps")[0].GetProperty("completedAtUtc").GetString());
    }

    [TestMethod]
    public async Task LeadCanBeConvertedExactlyOnceIntoCustomer()
    {
        using var factory = new CrmApiFactory();
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RegisterAdminAsync(admin, "lead");
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync(admin));

        var email = $"lead-{Guid.NewGuid():N}@example.invalid";
        var leadResponse = await admin.PostAsJsonAsync("/api/leads", new
        {
            name = "Qualified Prospect",
            email,
            company = "Example Co",
            source = "Referral"
        });
        Assert.AreEqual(HttpStatusCode.Created, leadResponse.StatusCode);
        var lead = await leadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var leadId = lead.GetProperty("id").GetGuid();

        var conversion = await admin.PostAsync($"/api/leads/{leadId}/convert", null);
        Assert.AreEqual(HttpStatusCode.Created, conversion.StatusCode);
        var converted = await conversion.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = converted.GetProperty("customer").GetProperty("id").GetGuid();
        var storedLead = await admin.GetFromJsonAsync<JsonElement>($"/api/leads/{leadId}");
        Assert.AreEqual("Converted", storedLead.GetProperty("status").GetString());
        Assert.AreEqual(customerId, storedLead.GetProperty("convertedCustomerId").GetGuid());

        var secondConversion = await admin.PostAsync($"/api/leads/{leadId}/convert", null);
        Assert.AreEqual(HttpStatusCode.Conflict, secondConversion.StatusCode);
        var customers = await admin.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.AreEqual(1, customers.GetProperty("total").GetInt32());
    }

    [TestMethod]
    public async Task CsvImportValidatesRowsAndSkipsDuplicates()
    {
        using var factory = new CrmApiFactory();
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"import-admin-{Guid.NewGuid():N}@example.invalid";
        var registration = await admin.PostAsJsonAsync("/api/auth/register", new
        {
            organizationName = "Import Test Organization",
            displayName = "Import Administrator",
            email,
            password = "SecureImport!5982"
        });
        Assert.AreEqual(HttpStatusCode.Created, registration.StatusCode);
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync(admin));

        var duplicateEmail = $"imported-{Guid.NewGuid():N}@example.invalid";
        var csv = string.Join("\r\n", [
            "customer_name,email,signup_date,city,external_id,status",
            $"\"Imported, Customer\",{duplicateEmail},2026-01-15,Calicut,IMP-1,Active",
            $"Duplicate Row,{duplicateEmail},2026-01-15,Calicut,IMP-2,Active",
            "Bad Date,bad-date@example.invalid,15/01/2026,Calicut,IMP-3,Active"
        ]);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(csv, Encoding.UTF8, "text/csv"), "file", "customers.csv");

        var response = await admin.PostAsync("/api/customers/import", multipart);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(3, result.GetProperty("rowsRead").GetInt32());
        Assert.AreEqual(1, result.GetProperty("createdCount").GetInt32());
        Assert.AreEqual(2, result.GetProperty("skippedCount").GetInt32());
        Assert.AreEqual(2, result.GetProperty("errorCount").GetInt32());

        var customers = await admin.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.AreEqual(1, customers.GetProperty("total").GetInt32());
        Assert.AreEqual("Imported, Customer", customers.GetProperty("items")[0].GetProperty("name").GetString());
    }

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

    private static async Task RegisterAdminAsync(HttpClient admin, string prefix)
    {
        var response = await admin.PostAsJsonAsync("/api/auth/register", new
        {
            organizationName = $"{prefix} Test Organization",
            displayName = $"{prefix} Administrator",
            email = $"{prefix}-{Guid.NewGuid():N}@example.invalid",
            password = "SecureWorkflow!5982"
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
