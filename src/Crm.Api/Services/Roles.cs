namespace Crm.Api.Services;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string SalesExecutive = "Sales Executive";
    public static readonly string[] All = [Admin, Manager, SalesExecutive];
}
