using System.Net;
using KommaKorrektus.Data.Entities;

namespace KommaKorrektus.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public sealed class SignInTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public SignInTests(AppFixture app) => _app = app;

    [DatabaseFact]
    public async Task A_wrong_password_is_refused_and_the_right_one_signs_in()
    {
        await _app.EnsureAdminAsync();
        await _app.CreateUserAsync("login@example.test", "Richtig-Richtig-1", UserRole.User);
        using var client = _app.NewClient();

        var wrong = await AppFixture.PostFormAsync(client, "/Account/Login", new Dictionary<string, string> { ["Input.Email"] = "login@example.test", ["Input.Password"] = "falsch" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.DoesNotContain("Richtig-Richtig-1", await AppFixture.TextAsync(wrong));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Customers")).StatusCode);

        await AppFixture.LoginAsync(client, "login@example.test", "Richtig-Richtig-1");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Customers")).StatusCode);

        // a member of the staff has no administration
        var admin = await client.GetAsync("/Admin/Users");
        Assert.NotEqual(HttpStatusCode.OK, admin.StatusCode);
    }

    [DatabaseFact]
    public async Task Too_many_wrong_passwords_lock_the_account_for_a_while()
    {
        await _app.EnsureAdminAsync();
        await _app.CreateUserAsync("lock@example.test", "Richtig-Richtig-2", UserRole.User);
        using var client = _app.NewClient();

        for (var i = 0; i < 6; i++)
        {
            await AppFixture.PostFormAsync(client, "/Account/Login", new Dictionary<string, string> { ["Input.Email"] = "lock@example.test", ["Input.Password"] = "falsch" + i });
        }

        // even the right password is refused during the lock-out
        var locked = await AppFixture.PostFormAsync(client, "/Account/Login", new Dictionary<string, string> { ["Input.Email"] = "lock@example.test", ["Input.Password"] = "Richtig-Richtig-2" });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Customers")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_missing_page_says_so_in_words()
    {
        await _app.EnsureAdminAsync();
        await _app.CreateUserAsync("missing@example.test", "Richtig-Richtig-3", UserRole.User);
        using var client = _app.NewClient();
        await AppFixture.LoginAsync(client, "missing@example.test", "Richtig-Richtig-3");

        var response = await client.GetAsync("/Customers/Details?id=987654");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Das gibt es nicht", await AppFixture.TextAsync(response));
    }
}
