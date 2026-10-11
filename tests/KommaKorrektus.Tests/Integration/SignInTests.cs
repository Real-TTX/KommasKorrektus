using System.Net;
using KommaKorrektus.Core.Security;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    [DatabaseFact]
    public async Task With_the_second_factor_a_password_alone_is_not_enough_and_a_recovery_code_works_once()
    {
        await _app.EnsureAdminAsync();
        var user = await _app.CreateUserAsync("totp@example.test", "Totp-Passwort-2026", UserRole.User);
        IReadOnlyList<string> recovery = [];
        string secret = "";
        await _app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<KkDbContext>();
            var twoFactor = sp.GetRequiredService<TwoFactorService>();
            var tracked = await db.User.Include(u => u.Totp).FirstAsync(u => u.Id == user.Id);
            secret = (await twoFactor.BeginEnrollmentAsync(tracked)).Secret;
            recovery = (await twoFactor.ConfirmEnrollmentAsync(tracked, Totp.Code(secret, Totp.StepOf(DateTimeOffset.UtcNow))))!;
        });
        Assert.NotEmpty(recovery);

        // the password opens the second page, not the application
        using var client = _app.NewClient();
        var first = await AppFixture.PostFormAsync(client, "/Account/Login", new Dictionary<string, string> { ["Input.Email"] = "totp@example.test", ["Input.Password"] = "Totp-Passwort-2026" });
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal("/Account/TwoFactor", AppFixture.LocationPath(first));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Customers")).StatusCode);

        // a wrong code is refused, a recovery code signs in
        var wrong = await AppFixture.PostFormAsync(client, "/Account/TwoFactor", new Dictionary<string, string> { ["Code"] = "000000" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        var ok = await AppFixture.PostFormAsync(client, "/Account/TwoFactor", new Dictionary<string, string> { ["Code"] = recovery[0] });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Customers")).StatusCode);

        // the same recovery code does not work a second time
        using var again = _app.NewClient();
        await AppFixture.PostFormAsync(again, "/Account/Login", new Dictionary<string, string> { ["Input.Email"] = "totp@example.test", ["Input.Password"] = "Totp-Passwort-2026" });
        var reused = await AppFixture.PostFormAsync(again, "/Account/TwoFactor", new Dictionary<string, string> { ["Code"] = recovery[0] });
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await again.GetAsync("/Customers")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_person_without_a_password_signs_in_with_a_code_from_an_email()
    {
        await _app.EnsureAdminAsync();
        await _app.CreateUserAsync("code@example.test", "", UserRole.User);
        using var client = _app.NewClient();

        // the page that asks for the address is the same for a known and an unknown address
        var request = await AppFixture.PostFormAsync(client, "/Account/LoginCode", new Dictionary<string, string> { ["Email"] = "code@example.test" }, "/Account/LoginCode?handler=Request");
        Assert.Equal(HttpStatusCode.Redirect, request.StatusCode);
        var unknown = await AppFixture.PostFormAsync(client, "/Account/LoginCode", new Dictionary<string, string> { ["Email"] = "nobody@example.test" }, "/Account/LoginCode?handler=Request");
        Assert.Equal(HttpStatusCode.Redirect, unknown.StatusCode);
        var flow = System.Web.HttpUtility.ParseQueryString(new Uri(new Uri("http://localhost"), request.Headers.Location!).Query)["flow"]!;

        // without a mail server the text of the mail (with the code) is kept in the log (development)
        var body = await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().MailLog.AsNoTracking().Where(m => m.ToAddress == "code@example.test").OrderByDescending(m => m.Id).Select(m => m.BodyText).FirstAsync());
        var code = System.Text.RegularExpressions.Regex.Match(body, @"\b\d{6}\b").Value;
        Assert.Equal(6, code.Length);
        Assert.Empty(await _app.WithScopeAsync(sp => sp.GetRequiredService<KkDbContext>().MailLog.AsNoTracking().Where(m => m.ToAddress == "nobody@example.test").ToListAsync()));

        var wrong = await AppFixture.PostFormAsync(client, $"/Account/LoginCode?flow={flow}", new Dictionary<string, string> { ["Code"] = code == "123456" ? "654321" : "123456" }, $"/Account/LoginCode?flow={flow}&handler=Verify");
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        var right = await AppFixture.PostFormAsync(client, $"/Account/LoginCode?flow={flow}", new Dictionary<string, string> { ["Code"] = code }, $"/Account/LoginCode?flow={flow}&handler=Verify");
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Customers")).StatusCode);

        // a code works once
        using var second = _app.NewClient();
        var reuse = await AppFixture.PostFormAsync(second, $"/Account/LoginCode?flow={flow}", new Dictionary<string, string> { ["Code"] = code }, $"/Account/LoginCode?flow={flow}&handler=Verify");
        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await second.GetAsync("/Customers")).StatusCode);
    }
}
