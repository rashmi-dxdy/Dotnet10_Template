# Endpoints

HTTP endpoints of the Orders module, mapped from `OrdersModule.MapEndpoints`. One static class per feature area with a
`Map(RouteGroupBuilder group)` method.

- **Public** (`routes.Public`, `/api/orders/...`): for external clients through the gateway. Listed in OpenAPI.
- **Internal** (`routes.Internal`, `/internal/orders/...`): for other modules only. Requires the internal API key
  (added by module clients), hidden from OpenAPI, and must be blocked at the gateway / ingress. Request and
  response types come from `Orders.Contracts`.

Endpoints contain no logic: bind the request, send a command or query with `ISender`, map the result to HTTP.
FluentValidation failures become `400` problem details automatically.

```csharp
internal static class ProductEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("products/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            await sender.Send(new GetProductQuery(id), ct) is { } product ? Results.Ok(product) : Results.NotFound());
}
```

(`ProductEndpoints` and `GetProductQuery` are illustrative names only.)
