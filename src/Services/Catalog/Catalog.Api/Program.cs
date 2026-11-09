using Catalog.Application.DependencyInjection;
using Catalog.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddCatalogApplication()
    .AddCatalogInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

await app.RunAsync();
