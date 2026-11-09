using Orders.Application.DependencyInjection;
using Orders.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOrdersApplication()
    .AddOrdersInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

await app.RunAsync();
