using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Build-3396210 startup compatibility. When a private replay bundle is configured,
/// its decoded static results take precedence over the independently authored
/// fallbacks below.
/// </summary>
internal sealed class Build3396210StartupProfile
{
    private readonly CapturedStartupReplay? replay;

    public Build3396210StartupProfile(GatewayCompatibilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);
        replay = CapturedStartupReplay.LoadOptional(
            options.CapturedStartupDataDirectory,
            profile);
    }

    public CapturedStartupReplayCursor CreateCursor() => new(replay);

    public static StartupResponse? CreateResponse(
        CapturedStartupReplayCursor cursor,
        string route,
        PyTuple arguments)
    {
        if (cursor.TryTake(route, arguments, out PyValue? captured))
        {
            return Response(captured!);
        }

        return route switch
        {
            "machoNet.GetServiceInfo" => Response(CreateServiceInfo()),
            "machoNet.GetConnectedClusterName" => Response(new PyText("LOCAL DEVELOPMENT")),
            "alert.BeanCount" => Response(new PyTuple(new PyInteger(0), new PyBuffer("B"u8))),
            "config.GetBlackListedPlanets" or
            "config.GetOldStationData" => Response(new PyList()),
            "config.GetAverageMarketPrices" => Response(new PyDictionary()),
            "air_npe.is_air_npe_enabled" or
            "raffleProxy.AmIBanned" => Response(new PyBoolean(false)),
            "invbroker.GetItemDescriptor" => Response(CreateItemDescriptor()),
            "map.GetSecurityModifiedSystems" => Response(CreateEmptyRowset(
                ("solarSystemID", 3),
                ("security", 5),
                ("modifiedSecurity", 5))),
            "userSvc.GetRedeemTokens" => Response(CreateEmptyRowset(
                ("tokenID", 3),
                ("massTokenID", 3),
                ("typeID", 3),
                ("quantity", 3),
                ("blueprintRuns", 3),
                ("blueprintMaterialLevel", 3),
                ("blueprintProductivityLevel", 3),
                ("label", 130),
                ("description", 130),
                ("dateTime", 64),
                ("expireDateTime", 64),
                ("availableDateTime", 64),
                ("stationID", 20),
                ("soulbound", 3))),
            "subscriptionMgr.GetCloneGrade" => Response(new PyInteger(2)),
            "loginCampaignManager.get_client_campaign_state" => Response(new PyDictionary()),
            "seasonalLoginCampaignManager.get_active_campaign" => Response(new PyList(
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance)),
            "charUnboundMgr.GetCharOmegaDowngradeStatus" => Response(new PyBoolean(false)),
            "charUnboundMgr.GetCharacterLockType" or
            "eventLog.LogPlayerRequestedDisconnect" or
            "objectCaching.GetCachableObject" => Response(PyNull.Instance),
            "config.GetMultiOwnersEx" or
            "config.GetMultiCorpTickerNamesEx" => Response(new PyList()),
            _ => null,
        };
    }

    private static PyValue CreateServiceInfo()
    {
        (string Service, string? Route)[] services =
        [
            ("air_npe", null),
            ("alert", null),
            ("beyonce", "solarsystem"),
            ("charUnboundMgr", null),
            ("config", null),
            ("eventLog", null),
            ("invbroker", null),
            ("loginCampaignManager", null),
            ("machoNet", null),
            ("map", null),
            ("objectCaching", null),
            ("raffleProxy", null),
            ("seasonalLoginCampaignManager", null),
            ("ship", "station"),
            ("subscriptionMgr", null),
            ("userSvc", null),
        ];
        return new PyDictionary(services
            .Select(service => new PyDictionaryEntry(
                new PyText(service.Service),
                service.Route is null ? PyNull.Instance : new PyText(service.Route)))
            .ToArray());
    }

    private static PyValue CreateItemDescriptor()
    {
        (string Name, int Encoding)[] columns =
        [
            ("itemID", 20),
            ("typeID", 3),
            ("ownerID", 3),
            ("locationID", 20),
            ("flagID", 2),
            ("quantity", 3),
            ("groupID", 3),
            ("categoryID", 3),
            ("customInfo", 129),
        ];
        var virtualColumns = new PyTuple(
            new PyTuple(new PyText("stacksize"), new PyToken("eve.common.script.sys.eveCfg.StackSize")),
            new PyTuple(new PyText("singleton"), new PyToken("eve.common.script.sys.eveCfg.Singleton")));
        return CreateDescriptor(columns, virtualColumns);
    }

    private static PyValue CreateEmptyRowset(params (string Name, int Encoding)[] columns)
    {
        PyValue descriptor = CreateDescriptor(columns);
        return new PyExtendedObject(
            2,
            new PyTuple(
                new PyTuple(new PyToken("carbon.common.script.sys.crowset.CRowset")),
                new PyDictionary(new PyDictionaryEntry(new PyText("header"), descriptor))),
            ImmutableArray<PyValue>.Empty,
            ImmutableArray<PyDictionaryEntry>.Empty);
    }

    private static PyExtendedObject CreateDescriptor(
        IEnumerable<(string Name, int Encoding)> columns,
        PyTuple? virtualColumns = null)
    {
        var columnValues = new PyTuple(columns
            .Select(column => (PyValue)new PyTuple(
                new PyText(column.Name),
                new PyInteger(column.Encoding)))
            .ToArray());
        PyTuple descriptorArguments = virtualColumns is null
            ? new PyTuple(columnValues)
            : new PyTuple(columnValues, virtualColumns);
        return new PyExtendedObject(
            1,
            new PyTuple(new PyToken("blue.DBRowDescriptor"), descriptorArguments));
    }

    private static StartupResponse Response(PyValue value) => new(value);
}

internal sealed record StartupResponse(PyValue Value);
