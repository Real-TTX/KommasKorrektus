using System.Text.RegularExpressions;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.Services.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace KommaKorrektus.Tests.Integration;

/// <summary>
/// A running application against a real PostgreSQL: its own database (made and dropped here) and its own data folder. The tests need a server and
/// skip themselves without one: <c>KOMMAKORREKTUS_TEST_DB</c> is a connection string such as <c>Host=localhost;Port=5432;Username=postgres;Password=...</c>.
/// </summary>
public sealed class AppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Variable = "KOMMAKORREKTUS_TEST_DB";

    public static bool Available => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable));

    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "kk-it-" + Guid.NewGuid().ToString("N")[..8]);

    public string DatabaseName { get; } = "kk_it_" + Guid.NewGuid().ToString("N")[..10];

    // The application reads where it lives from the environment while it starts. The test classes share one collection, so they start one after the other.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var b = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(Variable));
        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable("KOMMAKORREKTUS_DATA", DataDir);
        Environment.SetEnvironmentVariable("KOMMAKORREKTUS__Database__Host", b.Host);
        Environment.SetEnvironmentVariable("KOMMAKORREKTUS__Database__Port", b.Port.ToString());
        Environment.SetEnvironmentVariable("KOMMAKORREKTUS__Database__Username", b.Username);
        Environment.SetEnvironmentVariable("KOMMAKORREKTUS__Database__Password", b.Password);
        Environment.SetEnvironmentVariable("KOMMAKORREKTUS__Database__Database", DatabaseName);
        return base.CreateHost(builder);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (!Available)
        {
            return;
        }

        try
        {
            var b = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(Variable)) { Database = "postgres", Pooling = false };
            await using var connection = new NpgsqlConnection(b.ConnectionString);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
            Directory.Delete(DataDir, recursive: true);
        }
        catch (Exception ex) when (ex is NpgsqlException or IOException)
        {
            // a temporary database that stays behind is not worth failing a test run
        }
    }

    // ------------------------------------------------------------------------------------------------ what the tests start from

    /// <summary>Runs something with the services of a request scope (a context, the services of the application).</summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public Task WithScopeAsync(Func<IServiceProvider, Task> action) => WithScopeAsync<int>(async sp =>
    {
        await action(sp);
        return 0;
    });

    public Task<User> CreateUserAsync(string email, string password, UserRole role, params long[] customerIds)
        => WithScopeAsync(sp => sp.GetRequiredService<UserService>().CreateAsync(email, "Test " + role, role, password, customerIds));

    /// <summary>Until there is an administrator the only page is the setup; most tests start with one.</summary>
    public async Task EnsureAdminAsync()
    {
        if (!await WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().User.AnyAsync(u => u.Role == UserRole.Admin)))
        {
            await CreateUserAsync("admin@example.test", "Admin-Passwort-2026", UserRole.Admin);
        }
    }

    /// <summary>A customer with a project and a text document that has two versions (the second one has changes).</summary>
    public Task<(Customer Customer, Project Project, Document Document)> CreateCustomerWithDocumentAsync(string name)
        => WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var customer = new Customer { Number = "K-" + Guid.NewGuid().ToString("N")[..6], Kind = CustomerKind.Person, FirstName = "Test", LastName = name, Email = name.ToLowerInvariant() + "@example.test" };
            db.Customer.Add(customer);
            await db.SaveChangesAsync();
            var project = new Project { Number = "P-" + Guid.NewGuid().ToString("N")[..6], CustomerId = customer.Id, Title = "Projekt " + name };
            db.Project.Add(project);
            await db.SaveChangesAsync();

            var documents = sp.GetRequiredService<DocumentService>();
            var import = await documents.ImportAsync(project, "Text " + name, "text.txt", System.Text.Encoding.UTF8.GetBytes("Es war einmal eine Hexe.\nSie hate einen Besen und flog los."));
            await db.SaveChangesAsync();
            await documents.SaveAsync(import.Document, "Es war einmal eine Hexe.\nSie hatte einen Besen und flog los.\nDer Rabe folgte ihr.", VersionOrigin.Manual, null, null);
            await db.SaveChangesAsync();
            return (customer, project, await db.Document.AsNoTracking().FirstAsync(d => d.Id == import.Document.Id));
        });

    // ------------------------------------------------------------------------------------------------ talking to the pages

    public HttpClient NewClient(bool followRedirects = false)
        => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects, HandleCookies = true });

    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>The anti-forgery token of the form on a page (the cookie that belongs to it is kept by the client).</summary>
    public static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return Token.Match(html) is { Success: true } m ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : throw new InvalidOperationException("No form on " + url);
    }

    /// <summary>Posts a form the way a browser does: loads the page (for the token), then posts the fields.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pageUrl, IDictionary<string, string> fields, string? postUrl = null)
    {
        var token = await TokenAsync(client, pageUrl);
        var body = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        return await client.PostAsync(postUrl ?? pageUrl, new FormUrlEncodedContent(body));
    }

    /// <summary>The path a redirect points to (the address may be relative).</summary>
    public static string LocationPath(HttpResponseMessage response) => new Uri(new Uri("http://localhost"), response.Headers.Location!).AbsolutePath;

    /// <summary>The text of a page as a person reads it: Razor writes "ü" as an HTML entity.</summary>
    public static async Task<string> TextAsync(HttpResponseMessage response) => System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    public static async Task<string> TextAsync(HttpClient client, string url) => System.Net.WebUtility.HtmlDecode(await client.GetStringAsync(url));

    public static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var response = await PostFormAsync(client, "/Account/Login", new Dictionary<string, string> { ["Input.Email"] = email, ["Input.Password"] = password });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection
{
    public const string Name = "integration";
}

/// <summary>A test that needs the PostgreSQL of <see cref="AppFixture.Variable"/>; without it the test is skipped (and says so).</summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!AppFixture.Available)
        {
            Skip = $"{AppFixture.Variable} is not set: no PostgreSQL to run the application against.";
        }
    }
}
