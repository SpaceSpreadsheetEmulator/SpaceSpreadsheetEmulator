namespace SpaceSpreadsheetEmulator.Gateway.LocalEdge;

internal static class PublicGatewayEndpoints
{
    private const int MaximumRequestBytes = 1024 * 1024;

    public static void MapPublicGateway(this WebApplication app)
    {
        app.MapPost("/eve_public.gateway.Notices/Consume", KeepNoticeStreamOpenAsync);
        app.MapPost("/eve_public.gateway.Requests/Send", CompleteGrpcCallAsync);
        app.MapPost("/eve_public.gateway.Events/Publish", CompleteGrpcCallAsync);
    }

    private static async Task KeepNoticeStreamOpenAsync(HttpContext context)
    {
        PrepareGrpcResponse(context);
        await DrainBoundedAsync(context.Request, context.RequestAborted);
        await context.Response.StartAsync(context.RequestAborted);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
    }

    private static async Task CompleteGrpcCallAsync(HttpContext context)
    {
        PrepareGrpcResponse(context);
        await DrainBoundedAsync(context.Request, context.RequestAborted);
        await context.Response.Body.WriteAsync(new byte[5], context.RequestAborted);
        context.Response.AppendTrailer("grpc-status", "0");
    }

    private static void PrepareGrpcResponse(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/grpc";
        context.Response.DeclareTrailer("grpc-status");
    }

    private static async Task DrainBoundedAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        int total = 0;
        while (true)
        {
            int read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            total = checked(total + read);
            if (total > MaximumRequestBytes)
            {
                throw new BadHttpRequestException("The public-gateway request exceeds the local compatibility limit.", StatusCodes.Status413PayloadTooLarge);
            }
        }
    }
}
