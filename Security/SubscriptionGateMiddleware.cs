using System.Security.Claims;
using CrmLeadManagement.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrmLeadManagement.Security;

/// <summary>
/// Enforces subscription validity. Automatically blocks and stops access to CRM modules
/// when a tenant's free trial (or paid plan) limit finishes.
/// Redirects users to the Subscription page to select a plan and complete payment via Razorpay.
/// </summary>
public class SubscriptionGateMiddleware
{
    private readonly RequestDelegate _next;

    public SubscriptionGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Unauthenticated users are handled by standard auth pipeline
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // 2. Whitelist paths: Subscription renewal, Auth, SuperAdmin, and static assets must remain accessible
        if (path.StartsWith("/subscription") ||
            path.StartsWith("/auth/logout") ||
            path.StartsWith("/auth/login") ||
            path.StartsWith("/superadmin") ||
            path.StartsWith("/api/subscription") ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/uploads") ||
            path.StartsWith("/lib") ||
            path.StartsWith("/favicon.ico"))
        {
            await _next(context);
            return;
        }

        // 3. SuperAdmin is exempt from tenant plan restrictions
        if (context.User.IsInRole("Super Admin"))
        {
            await _next(context);
            return;
        }

        // 4. Query current tenant subscription status
        using var scope = context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var authEmail = context.User.FindFirst(ClaimTypes.Email)?.Value;
        var currentUser = db.Users.FirstOrDefault(u => u.Email == authEmail);
        if (currentUser == null || string.Equals(currentUser.Role, "Super Admin", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var effectiveAdminId = currentUser.GetEffectiveAdminId() ?? currentUser.Id;
        if (effectiveAdminId <= 0)
        {
            await _next(context);
            return;
        }

        var sub = db.CompanySubscriptions.FirstOrDefault(s => s.AdminId == effectiveAdminId);
        bool isExpired = false;

        if (sub != null)
        {
            if (string.Equals(sub.Status, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                isExpired = true;
            }
            else if (DateTime.UtcNow > sub.EndDate)
            {
                isExpired = true;
                if (!string.Equals(sub.Status, "Expired", StringComparison.OrdinalIgnoreCase))
                {
                    sub.Status = "Expired";
                    try { db.SaveChanges(); } catch { }
                }
            }
        }

        // 5. If trial or subscription limit has finished, stop and block access to CRM modules
        if (isExpired)
        {
            if (path.StartsWith("/api/"))
            {
                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    isExpired = true,
                    message = "Your Free Trial has expired. Access to CRM modules has stopped. Please upgrade your subscription plan to continue."
                }));
                return;
            }

            // Redirect web page to Subscription overview with expired alert flag
            context.Response.Redirect("/Subscription?expired=true");
            return;
        }

        await _next(context);
    }
}

