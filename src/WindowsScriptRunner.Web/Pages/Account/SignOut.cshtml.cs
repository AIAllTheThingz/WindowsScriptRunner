using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WindowsScriptRunner.Web.Pages.Account;

[Authorize]
public sealed class SignOutModel : PageModel
{
    public void OnGet()
    {
    }
}
