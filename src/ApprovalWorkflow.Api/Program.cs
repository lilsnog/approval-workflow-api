using System.Text.Json.Serialization;
using ApprovalWorkflow.Application;
using ApprovalWorkflow.Domain;
using ApprovalWorkflow.Endpoints;
using ApprovalWorkflow.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();

builder.Services
    .AddAuthentication(ApiKeyOptions.Scheme)
    .AddScheme<ApiKeyOptions, ApiKeyHandler>(ApiKeyOptions.Scheme, o =>
        builder.Configuration.GetSection("ApiKeys").Bind(o.Users));
builder.Services.AddAuthorization();

var workflowOptions = new WorkflowOptions();
builder.Configuration.Bind(workflowOptions);
var slaOptions = builder.Configuration.GetSection("Sla").Get<SlaOptions>() ?? new SlaOptions();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IWorkflowCatalog>(new ConfigWorkflowCatalog(workflowOptions));
builder.Services.AddSingleton<IRequestRepository, InMemoryRequestRepository>();
builder.Services.AddSingleton<INotifier, LoggingNotifier>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddSingleton(slaOptions);
builder.Services.AddHostedService<SlaEscalationService>();

var app = builder.Build();

// Map domain errors to RFC 7807 problem responses.
app.UseExceptionHandler(errors => errors.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = error switch
    {
        ForbiddenException => (StatusCodes.Status403Forbidden, "Not allowed"),
        DomainException => (StatusCodes.Status422UnprocessableEntity, "Request cannot be processed"),
        NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
    };

    context.Response.StatusCode = status;
    await Results.Problem(
        title: title,
        detail: status < 500 ? error?.Message : null,
        statusCode: status).ExecuteAsync(context);
}));

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRequestEndpoints();

app.Run();

public partial class Program;
