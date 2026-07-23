using System.Globalization;
using Google.Protobuf;
using Grpc.Core;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Primitives.Identifiers;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Worker.Login;

/// <summary>
/// Implements authentication and character-selection operations on the Worker data plane.
/// </summary>
public sealed partial class LoginGameplayGrpcService(
    IStaticDataStore staticData,
    IAccountAuthenticator authenticator,
    ICharacterSelectionQuery characterSelection,
    LoginTicketRegistry tickets,
    ILogger<LoginGameplayGrpcService> logger) : LoginGameplay.LoginGameplayBase
{
    public override Task<CompatibilityResponse> GetCompatibility(
        CompatibilityRequest request,
        ServerCallContext context)
    {
        CompatibilityManifest manifest = staticData.Compatibility;
        return Task.FromResult(new CompatibilityResponse
        {
            ClientBuild = checked((uint)manifest.ClientBuild),
            ProtocolProfile = checked((uint)manifest.ProtocolProfile),
            SdeBuild = checked((uint)manifest.SdeBuild),
            SourceSha256 = manifest.SourceSha256,
            ArtifactSha256 = manifest.ArtifactSha256,
        });
    }

    public override async Task<AuthenticateResponse> Authenticate(
        AuthenticateRequest request,
        ServerCallContext context)
    {
        if (!BackplaneRequestValidator.TryValidateContext(request.Context, out NodeId gatewayId, out GatewaySessionId sessionId))
        {
            return AuthenticationError("backplane.invalid_context", "The login request context is invalid.");
        }

        var attempt = new LoginAttempt(
            request.UserName,
            request.CredentialProof.Span,
            request.LanguageId,
            request.CountryCode);
        AuthenticationResult result = await authenticator.AuthenticateAsync(attempt, context.CancellationToken);
        if (!result.IsSuccess)
        {
            LogAuthenticationRejected(logger, result.Failure.ToString());
            return AuthenticationError("identity.login_rejected", "The account credentials were rejected.");
        }

        AuthenticatedAccount account = result.Account!;
        byte[] ticket;
        try
        {
            ticket = tickets.Issue(gatewayId, sessionId, account);
        }
        catch (InvalidOperationException)
        {
            return AuthenticationError("identity.session_capacity", "The login-session capacity is full.");
        }

        try
        {
            return new AuthenticateResponse
            {
                Success = true,
                LoginTicket = ByteString.CopyFrom(ticket),
                AccountId = account.AccountId.Value,
                UserName = account.UserName,
                LanguageId = account.LanguageId,
                CountryCode = account.CountryCode,
                Role = account.Role,
            };
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(ticket);
        }
    }

    public override async Task<CharacterSelectionResponse> GetCharacterSelection(
        CharacterSelectionRequest request,
        ServerCallContext context)
    {
        if (!BackplaneRequestValidator.TryValidateContext(request.Context, out NodeId gatewayId, out GatewaySessionId sessionId)
            || !tickets.TryResolve(request.LoginTicket.Span, gatewayId, sessionId, out AuthenticatedAccount? account))
        {
            return new CharacterSelectionResponse
            {
                Error = Error("identity.invalid_session", "The login session is invalid or expired."),
            };
        }

        CharacterSelection selection = await characterSelection.ExecuteAsync(account!, context.CancellationToken);
        var response = new CharacterSelectionResponse { AccountId = selection.AccountId.Value };
        response.Characters.AddRange(selection.Characters.Select(MapCharacter));
        return response;
    }

    public override Task<CloseSessionResponse> CloseSession(
        CloseSessionRequest request,
        ServerCallContext context)
    {
        bool closed = BackplaneRequestValidator.TryValidateContext(request.Context, out NodeId gatewayId, out GatewaySessionId sessionId)
            && tickets.Close(request.LoginTicket.Span, gatewayId, sessionId);
        return Task.FromResult(new CloseSessionResponse { Closed = closed });
    }

    private static AuthenticateResponse AuthenticationError(string code, string message)
        => new() { Success = false, Error = Error(code, message) };

    private static ServiceError Error(string code, string message) => new() { Code = code, Message = message };

    private static Backplane.Contracts.V1.CharacterSummary MapCharacter(Gameplay.Characters.CharacterSummary character)
    {
        var response = new Backplane.Contracts.V1.CharacterSummary
        {
            CharacterId = character.CharacterId.Value,
            Name = character.Name,
            RaceId = character.RaceId,
            BloodlineId = character.BloodlineId,
            AncestryId = character.AncestryId,
            CharacterTypeId = character.CharacterTypeId,
            CorporationId = character.CorporationId,
            CorporationName = character.CorporationName,
            StationName = character.StationName ?? string.Empty,
            SolarSystemId = character.SolarSystemId,
            SolarSystemName = character.SolarSystemName,
            ConstellationId = character.ConstellationId,
            RegionId = character.RegionId,
            ShipId = character.ShipId,
            ShipTypeId = character.ShipTypeId,
            ShipName = character.ShipName,
            Balance = character.Balance.ToString(CultureInfo.InvariantCulture),
            SkillPoints = character.SkillPoints,
            LastLoginUnixMilliseconds = character.LastLoginAt.ToUnixTimeMilliseconds(),
        };
        if (character.StationId is int stationId)
        {
            response.StationId = stationId;
        }

        return response;
    }

    [LoggerMessage(EventId = 300, Level = LogLevel.Warning, Message = "Login authentication rejected: {Reason}")]
    private static partial void LogAuthenticationRejected(ILogger logger, string reason);
}
