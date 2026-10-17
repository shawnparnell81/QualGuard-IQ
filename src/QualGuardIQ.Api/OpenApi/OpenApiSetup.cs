using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using QualGuardIQ.Api.Auth;

namespace QualGuardIQ.Api.OpenApi;

/// <summary>
/// Shapes the generated OpenAPI document: API info, configurable servers (no hard-coded hosts),
/// both auth schemes the app accepts (session cookie + bearer token), the permission each operation
/// needs, and the standard problem+json error responses.
/// </summary>
public static class OpenApiSetup
{
    public static void Configure(OpenApiOptions o, IConfiguration config)
    {
        o.AddDocumentTransformer((doc, ctx, ct) =>
        {
            doc.Info = new OpenApiInfo
            {
                Title = "QualGuard IQ API", Version = "1.0.0",
                Description = "Manufacturing QMS API. Browser clients use the session cookie (plus the X-CSRF-TOKEN header on writes); API clients may send a bearer token from POST /api/auth/token. "
                    + "AQL plans and values are organization-configured; no proprietary sampling tables are embedded."
            };
            var servers = config.GetSection("OpenApi:Servers").Get<string[]>() is { Length: > 0 } s ? s : ["http://localhost:5080"];
            doc.Servers = servers.Select(u => new OpenApiServer { Url = u }).ToList();
            doc.Components ??= new OpenApiComponents();
            doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            doc.Components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "opaque", Description = "Access token from POST /api/auth/token." };
            doc.Components.SecuritySchemes["cookieAuth"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = "qms.session", Description = "Browser session from POST /api/auth/login." };
            doc.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
            doc.Components.Schemas["ProblemDetails"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object, Required = new HashSet<string> { "title", "status", "traceId" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                    ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                    ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                    ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["errors"] = new OpenApiSchema { Type = JsonSchemaType.Object, AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } } }
                }
            };
            doc.Security = [
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearerAuth", doc)] = [] },
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("cookieAuth", doc)] = [] }];
            return Task.CompletedTask;
        });
        o.AddOperationTransformer((op, ctx, ct) =>
        {
            var meta = ctx.Description.ActionDescriptor.EndpointMetadata;
            if (meta.OfType<IAllowAnonymous>().Any()) { op.Security = []; return Task.CompletedTask; }
            var perms = meta.OfType<RequirePermissionAttribute>().Select(p => p.Policy!.Replace(PermissionPolicyProvider.Prefix, "")).Distinct().ToList();
            var note = perms.Count > 0 ? $"Requires permission: `{string.Join("`, `", perms)}` (site/department scoped)." : "Requires an authenticated user.";
            op.Description = string.IsNullOrEmpty(op.Description) ? note : op.Description + "\n\n" + note;
            if (ctx.Description.ActionDescriptor is ControllerActionDescriptor cad && (op.Tags is null || op.Tags.Count == 0))
                op.Tags = new HashSet<OpenApiTagReference> { new(cad.ControllerName, ctx.Document) };
            op.Responses ??= new OpenApiResponses();
            foreach (var (code, text) in new[] { ("401", "Not signed in"), ("403", "Missing permission or outside site scope") })
                op.Responses.TryAdd(code, new OpenApiResponse { Description = text });
            // v1 endpoints document their error responses as problem+json (the global exception mapping produces them).
            if (ctx.Description.RelativePath?.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase) == true)
                foreach (var (code, text) in new[] { ("400", "Validation or business-rule error"), ("401", "Not signed in"), ("403", "Missing permission or outside site scope"), ("404", "Not found or outside site scope"),
                    ("409", "Conflict: state or version mismatch"), ("422", "Unprocessable request"), ("500", "Unexpected server error") })
                    op.Responses[code] = new OpenApiResponse { Description = text, Content = new Dictionary<string, OpenApiMediaType> { ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", ctx.Document) } } };
            return Task.CompletedTask;
        });
    }
}
