using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KommasKorrektus.Pages.Account;

/// <summary>Sign in with a code from an e-mail: first the address, then the code. The page after the first step is the same for a known and an unknown address.</summary>
[AllowAnonymous]
public sealed class LoginCodeModel(LoginCodeService codes, SignInService signIn, TwoFactorTicket ticket, ActivityLogger log) : PageModelBase
{
    [BindProperty]
    [Required(ErrorMessage = "Bitte gib deine E-Mail-Adresse ein.")]
    [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
    [Display(Name = "E-Mail-Adresse")]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Bitte gib den Code aus der E-Mail ein.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    /// <summary>The token of the flow: set once a code was asked for.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Flow { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool Available => codes.IsAvailable;

    public IActionResult OnGet()
    {
        ViewData["Title"] = "Anmelden mit E-Mail-Code";
        return Available ? Page() : RedirectToPage("/Account/Login");
    }

    public async Task<IActionResult> OnPostRequestAsync()
    {
        ViewData["Title"] = "Anmelden mit E-Mail-Code";
        ModelState.Remove(nameof(Code));
        if (!ModelState.IsValid || !Available)
        {
            return Page();
        }

        var flow = await codes.RequestAsync(Email, HttpContext.RequestAborted);
        TempData["CodeEmail"] = Email;
        return RedirectToPage(new { flow, returnUrl = ReturnUrl });
    }

    public async Task<IActionResult> OnPostVerifyAsync()
    {
        ViewData["Title"] = "Anmelden mit E-Mail-Code";
        ModelState.Remove(nameof(Email));
        TempData.Keep("CodeEmail");
        if (!ModelState.IsValid || Flow is null)
        {
            return Page();
        }

        var check = await codes.VerifyAsync(Flow.Value, Code, HttpContext.RequestAborted);
        if (check.User is null)
        {
            ModelState.AddModelError(nameof(Code), check.Expired ? "Der Code ist abgelaufen. Bitte fordere einen neuen an."
                : check.TooManyAttempts ? "Zu viele Versuche. Bitte fordere einen neuen Code an."
                : "Der Code stimmt nicht.");
            return Page();
        }

        if (SignInService.NeedsSecondFactor(check.User))
        {
            ticket.Issue(HttpContext, check.User.Id, ReturnUrl);
            return RedirectToPage("/Account/TwoFactor");
        }

        await signIn.StartSessionAsync(HttpContext, check.User, HttpContext.RequestAborted);
        await log.LogAsync("Anmeldung mit E-Mail-Code", "User", check.User.Id, check.User.Email);
        return LocalRedirect(SafeReturn(ReturnUrl, check.User.Role == UserRole.Client ? "/Portal" : "/"));
    }

    public string? ShownEmail => TempData.Peek("CodeEmail") as string;
}
