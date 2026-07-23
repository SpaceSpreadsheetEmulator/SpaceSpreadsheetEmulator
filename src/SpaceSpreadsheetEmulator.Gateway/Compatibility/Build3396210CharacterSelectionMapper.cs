using System.Globalization;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Backplane;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Maps neutral character-selection data to the rowsets expected by client build 3396210.
/// </summary>
internal static class Build3396210CharacterSelectionMapper
{
    private static readonly Build3396210RowField[] AccountFields =
    [
        new("userName", 130),
        new("characterSlots", 17),
        new("maxCharacterSlots", 130),
        new("subscriptionEndTime", 64),
        new("creationDate", 64),
    ];

    private static readonly Build3396210RowField[] TrainingFields =
    [
        new("trainingEnds", 64),
    ];

    private static readonly Build3396210RowField[] CharacterFields =
    [
        new("characterID", 3),
        new("logoffDate", 64),
        new("skillPoints", 3),
        new("paperdollState", 17),
        new("characterName", 130),
        new("typeID", 3),
        new("gender", 11),
        new("bloodlineID", 17),
        new("raceID", 17),
        new("deletePrepareDateTime", 64),
        new("balance", 6),
        new("balanceChange", 6),
        new("corporationID", 3),
        new("allianceID", 3),
        new("factionID", 3),
        new("unreadMailCount", 3),
        new("shipTypeID", 3),
        new("solarSystemID", 3),
        new("stationID", 20),
        new("locationSecurity", 5),
        new("finishedSkills", 2),
        new("skillsInQueue", 2),
        new("skillTypeID", 3),
        new("toLevel", 17),
        new("trainingStartTime", 64),
        new("trainingEndTime", 64),
        new("queueEndTime", 64),
        new("finishSP", 3),
        new("trainedSP", 3),
        new("lockTypeID", 17),
        new("daysTotal", 3),
        new("daysCompleted", 3),
    ];

    private static readonly Build3396210RowField[] WarFields =
    [
        new("characterID", 3),
        new("warID", 3),
        new("declaredByID", 3),
        new("againstID", 3),
        new("mutual", 11),
        new("ally", 3),
    ];

    public static PyList Create(
        BackendLoginSession session,
        CharacterSelectionResponse? response,
        DateTimeOffset authenticatedAt,
        out CharacterSelectionResponse? selectable)
    {
        ArgumentNullException.ThrowIfNull(session);
        selectable = null;
        IReadOnlyList<PyValue>[] characterRows = [];
        if (response is not null && IsValid(session, response))
        {
            characterRows = response.Characters.Select(CreateCharacterRow).ToArray();
            selectable = response;
        }

        return new PyList(
            Build3396210PackedRowBuilder.CreateRowset(
                AccountFields,
                AccountRow(session.UserName, authenticatedAt)),
            Build3396210PackedRowBuilder.CreateRowset(TrainingFields),
            Build3396210PackedRowBuilder.CreateRowset(CharacterFields, characterRows),
            Build3396210PackedRowBuilder.CreateRowset(WarFields));
    }

    private static IReadOnlyList<PyValue> AccountRow(string userName, DateTimeOffset authenticatedAt)
        =>
        [
            new PyText(userName),
            new PyInteger(3),
            new PyText("3"),
            PyNull.Instance,
            new PyInteger(authenticatedAt.UtcDateTime.ToFileTimeUtc()),
        ];

    private static IReadOnlyList<PyValue> CreateCharacterRow(CharacterSummary character)
    {
        decimal balance = decimal.Parse(character.Balance, NumberStyles.Number, CultureInfo.InvariantCulture);
        long integralBalance = decimal.ToInt64(balance);
        return
        [
            new PyInteger(character.CharacterId),
            new PyInteger(DateTimeOffset
                .FromUnixTimeMilliseconds(character.LastLoginUnixMilliseconds)
                .UtcDateTime
                .ToFileTimeUtc()),
            new PyInteger(character.SkillPoints),
            new PyInteger(0),
            new PyText(character.Name),
            new PyInteger(character.CharacterTypeId),
            new PyBoolean(false),
            new PyInteger(character.BloodlineId),
            new PyInteger(character.RaceId),
            PyNull.Instance,
            new PyInteger(integralBalance),
            new PyInteger(0),
            new PyInteger(character.CorporationId),
            PyNull.Instance,
            PyNull.Instance,
            new PyInteger(0),
            new PyInteger(character.ShipTypeId),
            new PyInteger(character.SolarSystemId),
            new PyInteger(character.StationId),
            new PyFloat(0),
            new PyInteger(0),
            new PyInteger(0),
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
            PyNull.Instance,
        ];
    }

    private static bool IsValid(BackendLoginSession session, CharacterSelectionResponse response)
    {
        if (response.Error is not null
            || response.AccountId != session.AccountId
            || response.Characters.Count > 3)
        {
            return false;
        }

        var characterIds = new HashSet<long>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CharacterSummary character in response.Characters)
        {
            if (character.CharacterId is <= 0 or > int.MaxValue
                || string.IsNullOrWhiteSpace(character.Name)
                || !characterIds.Add(character.CharacterId)
                || !names.Add(character.Name)
                || character.SkillPoints is < int.MinValue or > int.MaxValue
                || character.CharacterTypeId <= 0
                || character.BloodlineId is <= 0 or > byte.MaxValue
                || character.RaceId is <= 0 or > byte.MaxValue
                || character.CorporationId <= 0
                || character.ShipTypeId <= 0
                || character.SolarSystemId <= 0
                || character.StationId <= 0
                || character.ShipId <= 0
                || character.LastLoginUnixMilliseconds is < 0 or > 253_402_300_799_999
                || !decimal.TryParse(
                    character.Balance,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal balance)
                || balance != decimal.Truncate(balance)
                || balance is < long.MinValue or > long.MaxValue)
            {
                return false;
            }
        }

        return true;
    }
}
