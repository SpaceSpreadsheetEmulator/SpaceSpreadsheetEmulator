using System.Text;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private string? characterManagerBinding;
    private string? warRegistryBinding;

    private RpcDispatchResult? GetCharacterBootstrapResponse(
        string route,
        MachoRpcRequest request)
    {
        if (selectedCharacter is not { } character)
        {
            return null;
        }

        return route switch
        {
            "officeManager.GetMyCorporationsOffices"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyCorporationOffices()),
            "agentMgr.GetMyJournalDetails"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyJournalDetails()),
            "standingMgr.GetCorpStandings"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyCorporationStandings()),
            "jumpTimers.GetTimers"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyJumpTimers()),
            "accessGroupBookmarkMgr.GetMyActiveBookmarks"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyBookmarkState()),
            "nonDiminishingInjectionMgr.GetAvailableNonDiminishingInjections"
                => Result(new PyInteger(0)),
            "XmppChatMgr.Hostname"
                => Result(new PyText(string.Empty)),
            "charMgr.GetCharacterSettings"
                => Result(Build3396210StationBootstrapMapper.CreateCharacterSettings()),
            "operationsManager.can_character_play_the_tutorial"
                => Result(new PyBoolean(false)),
            "structureDirectory.GetMyDockableStructures"
                => Result(new PyList()),
            "securityMgr.get_modified_systems"
                => Result(Build3396210StationBootstrapMapper.CreateEmptySecurityModifiers()),
            "fwWarzoneSolarsystem.GetLocalOccupationState"
                => Result(new PyTuple(new PyInteger(character.SolarSystemId), PyNull.Instance)),
            "achievementTrackerMgr.GetCompletedAchievementsAndClientEventCount" or
            "monolithTaskTrackerMgr.GetCompletedAchievementsAndClientEventCount"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyAchievementState()),
            "fighterMgr.GetFightersForShip"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyFighterState()),
            "charMgr.GetSettingsInfo"
                => Result(Build3396210StationBootstrapMapper.CreateSettingsInfo(profile)),
            "ProjectDiscovery.initialize_tutorial_status"
                => Result(new PyBoolean(false)),
            "charMgr.GetContactList"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyContactList()),
            "bound.GetCorporateContacts"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyCorporateContacts()),
            "onlineStatus.GetInitialState"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyOnlineState()),
            "charMgr.LogSettings" or
            "milestoneMgr.ProcessCharacterLogon" or
            "eventLog.LogClientStats" or
            "clientStatLogger.LogString" or
            "bound.CheckAndSendNotifications"
                => Result(PyNull.Instance),
            "dynamicBountyMgr.GetOutputForClientSolarSystem"
                => Result(new PyTuple(new PyInteger(1), new PyBoolean(false))),
            "station.GetGuests" when character.HasStationId
                => Result(Build3396210StationBootstrapMapper.CreateStationGuests(character)),
            "chatAuthenticationService.GetAuthenticationToken"
                => Result(new PyText(string.Empty)),
            "config.GetMultiLocationsEx"
                => Result(Build3396210StationBootstrapMapper.CreateLocations(character)),
            "XmppChatMgr.ResyncSystemChannelAccess"
                => Result(Build3396210StationBootstrapMapper.CreateSystemChannelAccess(character)),
            "browserLockdownSvc.GetFlaggedSitesHash"
                => Result(new PyBuffer(Encoding.ASCII.GetBytes("d751713988987e9331980363e24189ce"))),
            "shipKillCounter.GetItemKillCountPlayer"
                => Result(new PyInteger(0)),
            "bountyProxy.GetMyKillRights"
                => Result(CreateEmptyKillRights()),
            "shipCosmeticsMgr.GetEnabledCosmetics" or
            "userSvc.GetMultiCharactersTrainingSlots" or
            "bound.GetWars" or
            "bound.GetSkills" or
            "bound.GetAllSkills" or
            "bound.GetBoosters" or
            "bound.GetImplants"
                => Result(new PyDictionary()),
            "bound.GetMyApplications"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyApplications()),
            "bound.GetSkillQueueAndFreePoints"
                => Result(new PyTuple(new PyList(), new PyInteger(0))),
            "contractProxy.GetLoginInfo"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyContractLogin()),
            "bound.GetSkillHistory"
                => Result(Build3396210StationBootstrapMapper.CreateEmptySkillHistory()),
            "notificationMgr.GetAllNotifications"
                => Result(Build3396210StationBootstrapMapper.CreateEmptyNotifications()),
            "bound.GetAttributes"
                => Result(CreateBaseCharacterAttributes()),
            "insurgencySolarsystem.GetAllVisibleCampaigns"
                => Result(new PyList()),
            "air_npe.get_air_npe_state"
                => Result(new PyInteger(3)),
            "charMgr.MachoResolveObject" when character.HasStationId
                => ResolveCharacterManager(request),
            "charMgr.MachoBindObject" when character.HasStationId
                => BindCharacterManager(request),
            "charMgr.GetPublicInfo"
                => Result(Build3396210StationBootstrapMapper.CreatePublicCharacterInfo(character)),
            "warRegistry.MachoResolveObject"
                => ResolveWarRegistry(request),
            "warRegistry.MachoBindObject"
                => BindWarRegistry(request),
            _ => null,
        };
    }

    private RpcDispatchResult ResolveCharacterManager(MachoRpcRequest request)
        => selectedCharacter is not null
            && request.Arguments.Items.Length == 1
                ? Result(new PyInteger(ProxyNodeId))
                : Result(PyNull.Instance);

    private RpcDispatchResult BindCharacterManager(MachoRpcRequest request)
    {
        if (selectedCharacter is null
            || request.Arguments.Items.Length != 2
            || Unwrap(request.Arguments.Items[1]) is not PyTuple { Items.Length: 3 } nested
            || !string.Equals(ReadText(nested.Items[0]), "ListStations", StringComparison.Ordinal)
            || Unwrap(nested.Items[1]) is not PyTuple { Items.Length: 0 })
        {
            return Result(PyNull.Instance);
        }

        characterManagerBinding ??=
            $"N={ProxyNodeId}:{checked((request.CallId * 2) + 80_000)}";
        return Result(new PyTuple(
            CreateLease(characterManagerBinding),
            Build3396210StationBootstrapMapper.CreateCharacterStations(selectedCharacter)));
    }

    private RpcDispatchResult ResolveWarRegistry(MachoRpcRequest request)
        => selectedCharacter is not null
            && request.Arguments.Items.Length == 1
                ? Result(new PyInteger(ProxyNodeId))
                : Result(PyNull.Instance);

    private RpcDispatchResult BindWarRegistry(MachoRpcRequest request)
    {
        if (request.Arguments.Items.Length != 2)
        {
            return Result(PyNull.Instance);
        }

        warRegistryBinding ??= $"N={ProxyNodeId}:{checked((request.CallId * 2) + 70_000)}";
        return Result(new PyTuple(CreateLease(warRegistryBinding), PyNull.Instance));
    }

    private static PyExtendedObject CreateEmptyKillRights()
        => Build3396210PackedRowBuilder.CreateRowset(
        [
            new Build3396210RowField("killRightID", 3),
            new Build3396210RowField("fromID", 3),
            new Build3396210RowField("toID", 3),
            new Build3396210RowField("expiryTime", 64),
            new Build3396210RowField("price", 6),
            new Build3396210RowField("restrictedTo", 3),
        ]);

    private static PyDictionary CreateBaseCharacterAttributes()
        => new(
            new PyDictionaryEntry(new PyInteger(168), new PyInteger(20)),
            new PyDictionaryEntry(new PyInteger(164), new PyInteger(20)),
            new PyDictionaryEntry(new PyInteger(165), new PyInteger(20)),
            new PyDictionaryEntry(new PyInteger(166), new PyInteger(20)),
            new PyDictionaryEntry(new PyInteger(167), new PyInteger(19)));
}
