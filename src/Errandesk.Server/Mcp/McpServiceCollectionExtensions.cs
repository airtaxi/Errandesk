using Errandesk.Contracts;
using Errandesk.Server.Authentication;
using Errandesk.Server.Services;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

namespace Errandesk.Server.Mcp;

public static class McpServiceCollectionExtensions
{
    private const string ServerInstructions = "These tools read and change the tasks and notes of the Errandesk account that owns the access token. Call get_current_time to learn the user's local date and time before working with dates. Dates are yyyy-MM-dd; reminder times are local yyyy-MM-ddTHH:mm in the user's time zone. Search before acting and use only IDs returned by the tools. Tools marked as destructive delete data: ask the user before calling them.";

    /// <summary>
    /// Registers the MCP server over stateless Streamable HTTP. Each request gets the assistant tools of the user its personal access token belongs to.
    /// </summary>
    public static IServiceCollection AddErrandeskMcp(this IServiceCollection services)
    {
        services.AddSingleton<McpToolCollectionFactory>();
        services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "errandesk", Title = "Errandesk", Version = ServerInfoService.ServerVersion };
            options.ServerInstructions = ServerInstructions;
        }).WithHttpTransport(options =>
        {
            options.SessionMode = HttpServerSessionMode.Stateless;
            options.ConfigureSessionOptions = (httpContext, serverOptions, _) =>
            {
                serverOptions.ToolCollection = httpContext.RequestServices.GetRequiredService<McpToolCollectionFactory>().Create(httpContext.User.GetUserId());
                return Task.CompletedTask;
            };
        });
        return services;
    }

    /// <summary>Maps the MCP endpoint at <c>/mcp</c>, which requires a personal access token.</summary>
    public static IEndpointConventionBuilder MapErrandeskMcp(this IEndpointRouteBuilder endpoints) => endpoints.MapMcp($"/{PersonalAccessTokenFormat.McpEndpointPath}").RequireAuthorization(ErrandeskAuthenticationSchemes.PersonalAccessTokenPolicy).WithMetadata(new IdempotencyExemptAttribute());
}
