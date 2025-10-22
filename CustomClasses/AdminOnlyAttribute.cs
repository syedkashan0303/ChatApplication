using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SignalRMVC.CustomClasses
{
    public class AdminOnlyAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User;

            if (!user.Identity.IsAuthenticated || !user.IsInRole("Manager"))
            {
                // Optional: redirect to access denied page or return 403
                context.Result = new ForbidResult(); // or RedirectToAction("AccessDenied", "Account");
            }

            base.OnActionExecuting(context);
        }
    }
}
