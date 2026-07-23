using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Handshake;

namespace SpaceSpreadsheetEmulator.Gateway.Backplane;

/// <summary>
/// Adapts Gateway login operations to the Worker's versioned login gRPC contract.
/// </summary>
public sealed class GrpcLoginBackend : ILoginBackend, IDisposable
{
    private readonly GatewayBackplaneOptions options;
    private readonly GrpcChannel channel;
    private readonly LoginGameplay.LoginGameplayClient client;

    public GrpcLoginBackend(IOptions<GatewayBackplaneOptions> options)
    {
        this.options = options.Value;
        channel = GrpcChannel.ForAddress(this.options.Address);
        client = new LoginGameplay.LoginGameplayClient(channel);
    }

    public async Task<bool> IsCompatibleAsync(CancellationToken cancellationToken)
    {
        try
        {
            CompatibilityResponse response = await client.GetCompatibilityAsync(
                new CompatibilityRequest(),
                cancellationToken: cancellationToken);
            return response is { ClientBuild: 3_396_210, ProtocolProfile: 3_396_210, SdeBuild: 3_396_210 };
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Unavailable or StatusCode.Cancelled)
        {
            return false;
        }
    }

    public async Task<BackendLoginSession?> AuthenticateAsync(
        ulong gatewaySessionId,
        LoginCredentials credentials,
        CancellationToken cancellationToken)
    {
        var request = new AuthenticateRequest
        {
            Context = CreateContext(gatewaySessionId),
            UserName = credentials.UserName,
            CredentialProof = ByteString.CopyFrom(credentials.PasswordProof.AsSpan()),
            LanguageId = credentials.LanguageId,
            CountryCode = credentials.CountryCode,
        };
        AuthenticateResponse response = await client.AuthenticateAsync(request, cancellationToken: cancellationToken);
        if (!response.Success || response.LoginTicket.IsEmpty)
        {
            return null;
        }

        return new BackendLoginSession(
            response.LoginTicket.ToByteArray(),
            response.AccountId,
            response.UserName,
            response.LanguageId,
            response.CountryCode,
            response.Role);
    }

    public async Task<CharacterSelectionResponse?> GetCharacterSelectionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
    {
        CharacterSelectionResponse response = await client.GetCharacterSelectionAsync(
            new CharacterSelectionRequest
            {
                Context = CreateContext(gatewaySessionId),
                LoginTicket = ByteString.CopyFrom(loginTicket.Span),
            },
            cancellationToken: cancellationToken);
        return string.IsNullOrEmpty(response.Error?.Code) ? response : null;
    }

    public async Task<StationCatalogResponse?> GetStationCatalogAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
    {
        StationCatalogResponse response = await client.GetStationCatalogAsync(
            new StationCatalogRequest
            {
                Context = CreateContext(gatewaySessionId),
                LoginTicket = ByteString.CopyFrom(loginTicket.Span),
            },
            cancellationToken: cancellationToken);
        return string.IsNullOrEmpty(response.Error?.Code) ? response : null;
    }

    public async Task<NpcAgentCatalogResponse?> GetNpcAgentCatalogAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
    {
        NpcAgentCatalogResponse response = await client.GetNpcAgentCatalogAsync(
            new NpcAgentCatalogRequest
            {
                Context = CreateContext(gatewaySessionId),
                LoginTicket = ByteString.CopyFrom(loginTicket.Span),
            },
            cancellationToken: cancellationToken);
        return string.IsNullOrEmpty(response.Error?.Code) ? response : null;
    }

    public async Task CloseSessionAsync(
        ulong gatewaySessionId,
        ReadOnlyMemory<byte> loginTicket,
        CancellationToken cancellationToken)
    {
        if (loginTicket.IsEmpty)
        {
            return;
        }

        try
        {
            await client.CloseSessionAsync(
                new CloseSessionRequest
                {
                    Context = CreateContext(gatewaySessionId),
                    LoginTicket = ByteString.CopyFrom(loginTicket.Span),
                },
                cancellationToken: cancellationToken);
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Unavailable or StatusCode.Cancelled)
        {
        }
    }

    public void Dispose() => channel.Dispose();

    private RequestContext CreateContext(ulong gatewaySessionId)
        => new()
        {
            GatewayId = options.GatewayId,
            GatewaySessionId = gatewaySessionId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ClientBuild = 3_396_210,
        };
}
