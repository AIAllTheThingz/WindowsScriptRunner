using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WindowsScriptRunner.Web.Security;

namespace WindowsScriptRunner.Web.Pages;

[Authorize(Policy = AuthorizationPolicies.Administrator)]
public sealed class AdministrationModel : PageModel
{
    public void OnGet()
    {
    }
}
