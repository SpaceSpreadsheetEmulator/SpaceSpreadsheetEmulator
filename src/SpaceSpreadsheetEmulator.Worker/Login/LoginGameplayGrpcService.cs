using System.Globalization;
using Google.Protobuf;
using Grpc.Core;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gameplay.Characters;
using SpaceSpreadsheetEmulator.Gameplay.Stations;
using SpaceSpreadsheetEmulator.Identity.Authentication;
using SpaceSpreadsheetEmulator.Inventory.Items;
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
    IStationCatalogQuery stationCatalog,
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

    public override async Task<StationCatalogResponse> GetStationCatalog(
        StationCatalogRequest request,
        ServerCallContext context)
    {
        if (!BackplaneRequestValidator.TryValidateContext(request.Context, out NodeId gatewayId, out GatewaySessionId sessionId)
            || !tickets.TryResolve(request.LoginTicket.Span, gatewayId, sessionId, out _))
        {
            return new StationCatalogResponse
            {
                Error = Error("identity.invalid_session", "The login session is invalid or expired."),
            };
        }

        IReadOnlyList<StationCatalogEntry> stations =
            await stationCatalog.ExecuteAsync(context.CancellationToken);
        var response = new StationCatalogResponse();
        response.Stations.AddRange(stations.Select(MapStation));
        return response;
    }

    public override async Task<NpcAgentCatalogResponse> GetNpcAgentCatalog(
        NpcAgentCatalogRequest request,
        ServerCallContext context)
    {
        if (!BackplaneRequestValidator.TryValidateContext(request.Context, out NodeId gatewayId, out GatewaySessionId sessionId)
            || !tickets.TryResolve(request.LoginTicket.Span, gatewayId, sessionId, out _))
        {
            return new NpcAgentCatalogResponse
            {
                Error = Error("identity.invalid_session", "The login session is invalid or expired."),
            };
        }

        IReadOnlyList<StaticNpcAgent> agents =
            await staticData.ListNpcAgentsAsync(context.CancellationToken);
        var response = new NpcAgentCatalogResponse();
        response.Agents.AddRange(agents.Select(MapAgent));
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
            CharacterGroupId = character.CharacterGroupId,
            CharacterCategoryId = character.CharacterCategoryId,
            CorporationId = character.CorporationId,
            CorporationName = character.CorporationName,
            HeadquartersStationId = character.HeadquartersStationId,
            StationName = character.StationName ?? string.Empty,
            StationOwnerId = character.StationOwnerId ?? 0,
            StationOperationId = character.StationOperationId ?? 0,
            StationTypeId = character.StationTypeId ?? 0,
            StationGroupId = character.StationGroupId ?? 0,
            StationCategoryId = character.StationCategoryId ?? 0,
            SolarSystemId = character.SolarSystemId,
            SolarSystemName = character.SolarSystemName,
            ConstellationId = character.ConstellationId,
            RegionId = character.RegionId,
            ShipId = character.ShipId,
            ShipTypeId = character.ShipTypeId,
            ShipGroupId = character.ShipGroupId,
            ShipCategoryId = character.ShipCategoryId,
            ShipName = character.ShipName,
            Balance = character.Balance.ToString(CultureInfo.InvariantCulture),
            SkillPoints = character.SkillPoints,
            LastLoginUnixMilliseconds = character.LastLoginAt.ToUnixTimeMilliseconds(),
        };
        if (character.StationId is int stationId)
        {
            response.StationId = stationId;
        }

        response.InventoryItems.AddRange(character.InventoryItems.Select(MapInventoryItem));
        response.ShipDogmaAttributes.AddRange(
            (character.ShipDogmaAttributes ?? new Dictionary<int, double>())
            .OrderBy(attribute => attribute.Key)
            .Select(attribute => new DogmaAttributeValue
            {
                AttributeId = attribute.Key,
                Value = attribute.Value,
            }));
        return response;
    }

    private static Backplane.Contracts.V1.CharacterInventoryItem MapInventoryItem(
        Gameplay.Characters.CharacterInventoryItem item)
        => new()
        {
            ItemId = item.ItemId,
            TypeId = item.TypeId,
            OwnerId = item.OwnerId,
            LocationId = item.LocationId,
            LocationKind = item.LocationKind switch
            {
                InventoryLocationKind.Station => CharacterInventoryLocationKind.Station,
                InventoryLocationKind.Item => CharacterInventoryLocationKind.Item,
                _ => throw new InvalidDataException(
                    $"Unsupported starter inventory location kind {item.LocationKind}."),
            },
            Flag = item.Flag switch
            {
                InventoryItemFlag.StationHangar => CharacterInventoryItemFlag.StationHangar,
                InventoryItemFlag.ShipCargo => CharacterInventoryItemFlag.ShipCargo,
                _ => throw new InvalidDataException(
                    $"Unsupported starter inventory flag {item.Flag}."),
            },
            Quantity = item.Quantity,
            Singleton = item.Singleton,
            CustomName = item.CustomName ?? string.Empty,
            GroupId = item.GroupId,
            CategoryId = item.CategoryId,
        };

    private static StationSummary MapStation(StationCatalogEntry station)
        => new()
        {
            StationId = station.StationId,
            SolarSystemId = station.SolarSystemId,
            OperationId = station.OperationId,
            StationTypeId = station.StationTypeId,
            OwnerId = station.OwnerId,
        };

    private static NpcAgentSummary MapAgent(StaticNpcAgent agent)
    {
        var response = new NpcAgentSummary
        {
            AgentId = agent.AgentId,
            AgentTypeId = agent.AgentTypeId,
            DivisionId = agent.DivisionId,
            Level = agent.Level,
            Gender = agent.Gender,
            IsLocatorAgent = agent.IsLocatorAgent,
        };
        if (agent.StationId is long stationId)
        {
            response.StationId = stationId;
        }

        if (agent.BloodlineId is int bloodlineId)
        {
            response.BloodlineId = bloodlineId;
        }

        if (agent.CorporationId is long corporationId)
        {
            response.CorporationId = corporationId;
        }

        return response;
    }

    [LoggerMessage(EventId = 300, Level = LogLevel.Warning, Message = "Login authentication rejected: {Reason}")]
    private static partial void LogAuthenticationRejected(ILogger logger, string reason);
}
