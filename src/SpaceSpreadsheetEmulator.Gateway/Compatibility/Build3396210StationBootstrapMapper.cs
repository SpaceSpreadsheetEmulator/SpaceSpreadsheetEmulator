using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Creates the non-authoritative empty/default contracts observed after character selection.
/// </summary>
internal static class Build3396210StationBootstrapMapper
{
    private static readonly Build3396210RowField[] ContactFields =
    [
        new("contactID", 3),
        new("inWatchlist", 11),
        new("relationshipID", 5),
        new("labelMask", 20),
    ];

    public static PyExtendedObject CreateEmptyCorporationOffices()
        => Build3396210PackedRowBuilder.CreateRowset(
        [
            new Build3396210RowField("leaseID", 20),
            new Build3396210RowField("stationID", 20),
            new Build3396210RowField("rentorID", 3),
            new Build3396210RowField("startDate", 64),
            new Build3396210RowField("rentalPeriod", 2),
            new Build3396210RowField("rentalPrice", 6),
            new Build3396210RowField("billID", 3),
            new Build3396210RowField("dueDate", 64),
            new Build3396210RowField("officeID", 20),
            new Build3396210RowField("stationTypeID", 3),
            new Build3396210RowField("solarsystemID", 20),
        ]);

    public static PyTuple CreateEmptyJournalDetails()
        => new(new PyList(), new PyList());

    public static PyExtendedObject CreateEmptyCorporationStandings()
        => Build3396210PackedRowBuilder.CreateRowset(
        [
            new Build3396210RowField("fromID", 3),
            new Build3396210RowField("standing", 5),
        ]);

    public static PyTuple CreateEmptyJumpTimers()
        => new(PyNull.Instance, PyNull.Instance, PyNull.Instance);

    public static PyExtendedObject CreateHomeStation(StationSummary station)
        => new(
            2,
            new PyTuple(
                new PyTuple(new PyToken("homestation.types.StationData")),
                new PyDictionary(
                    Entry("is_fallback", new PyBoolean(false)),
                    Entry("solar_system_id", new PyInteger(station.SolarSystemId)),
                    Entry("id", new PyInteger(station.StationId)),
                    Entry("type_id", new PyInteger(station.StationTypeId)))));

    public static PyFloat CreateCashBalance(CharacterSummary character)
        => new((double)decimal.Parse(
            character.Balance,
            NumberStyles.Number,
            CultureInfo.InvariantCulture));

    public static PyTuple CreateEmptyBookmarkState()
    {
        Build3396210RowField[] bookmarkFields =
        [
            new("bookmarkID", 20),
            new("folderID", 3),
            new("itemID", 20),
            new("typeID", 3),
            new("memo", 130),
            new("created", 64),
            new("expiry", 64),
            new("x", 5),
            new("y", 5),
            new("z", 5),
            new("locationID", 3),
            new("note", 130),
            new("creatorID", 3),
            new("subfolderID", 3),
        ];
        return new PyTuple(
            new PyList(),
            Build3396210PackedRowBuilder.CreateRowset(bookmarkFields),
            Build3396210PackedRowBuilder.CreateRowset(bookmarkFields));
    }

    public static PyObject CreateCharacterSettings()
        => KeyVal(
            Entry("__doc__", Bytes("Settings KeyVal/Dictionary")),
            Entry("crimewatchSafetyLevel", new PyText("2")),
            Entry("autoRejectDuelInvitations", new PyText("1")));

    public static PyExtendedObject CreateEmptySecurityModifiers()
        => Build3396210PackedRowBuilder.CreateIndexedRowset(
        [
            new Build3396210RowField("solarSystemID", 3),
            new Build3396210RowField("security", 5),
            new Build3396210RowField("modifiedSecurity", 5),
        ],
        "solarSystemID");

    public static PyDictionary CreateEmptyAchievementState()
        => new(
            Entry("eventDict", new PyDictionary()),
            Entry("completedDict", new PyDictionary()));

    public static PyTuple CreateEmptyFighterState()
        => new(new PyList(), new PyList(), new PyDictionary());

    public static PyTuple CreateSettingsInfo(ProtocolProfile profile)
        => new(
            new PySubstream(ImmutableArray.Create(BlueMarshalCodec.Encode(PyNull.Instance, profile))),
            new PyBuffer(ReadOnlySpan<byte>.Empty));

    public static PyObject CreateEmptyContactList()
        => KeyVal(
            Entry("addresses", Build3396210PackedRowBuilder.CreateRowset(ContactFields)),
            Entry(
                "blocked",
                Build3396210PackedRowBuilder.CreateRowset(
                    [new Build3396210RowField("senderID", 3)])));

    public static PyExtendedObject CreateEmptyCorporateContacts()
        => Build3396210PackedRowBuilder.CreateIndexedRowset(ContactFields, "contactID");

    public static PyExtendedObject CreateEmptyOnlineState()
        => Build3396210PackedRowBuilder.CreateRowset(
        [
            new Build3396210RowField("contactID", 3),
            new Build3396210RowField("online", 3),
        ]);

    public static PyExtendedObject CreateEmptyApplications()
        => Build3396210PackedRowBuilder.CreateFilterRowset(
        [
            new Build3396210RowField("applicationID", 3),
            new Build3396210RowField("corporationID", 3),
            new Build3396210RowField("characterID", 3),
            new Build3396210RowField("applicationText", 130),
            new Build3396210RowField("status", 17),
            new Build3396210RowField("applicationDateTime", 64),
            new Build3396210RowField("deleted", 11),
            new Build3396210RowField("responseText", 129),
        ],
        "corporationID");

    public static PyList CreateStationGuests(CharacterSummary character)
        => new(new PyList(
            new PyInteger(character.CharacterId),
            new PyInteger(character.CorporationId),
            PyNull.Instance,
            PyNull.Instance));

    public static PyTuple CreateLocations(CharacterSummary character)
        => new(
            new PyList(
                new PyText("locationID"),
                new PyText("locationName"),
                new PyText("solarSystemID"),
                new PyText("x"),
                new PyText("y"),
                new PyText("z"),
                new PyText("locationNameID")),
            new PyList(new PyList(
                new PyInteger(character.ShipId),
                new PyText(character.ShipName),
                PyNull.Instance,
                new PyInteger(0),
                new PyInteger(0),
                new PyInteger(0),
                PyNull.Instance)));

    public static PyList CreateSystemChannelAccess(CharacterSummary character)
        => new(
            Bytes($"local_{character.SolarSystemId}"),
            Bytes($"corp_{character.CorporationId}"));

    public static PyExtendedObject CreateEmptySkillHistory()
        => Build3396210PackedRowBuilder.CreateRowset(
        [
            new Build3396210RowField("logDate", 64),
            new Build3396210RowField("eventTypeID", 3),
            new Build3396210RowField("skillTypeID", 3),
            new Build3396210RowField("absolutePoints", 3),
            new Build3396210RowField("relativePoints", 3),
        ]);

    public static PyExtendedObject CreateEmptyNotifications()
        => Build3396210PackedRowBuilder.CreateRowset(
        [
            new Build3396210RowField("notificationID", 20),
            new Build3396210RowField("typeID", 3),
            new Build3396210RowField("senderID", 3),
            new Build3396210RowField("receiverID", 3),
            new Build3396210RowField("processed", 11),
            new Build3396210RowField("created", 64),
            new Build3396210RowField("data", 130),
            new Build3396210RowField("deleted", 11),
        ]);

    public static PyExtendedObject CreateCharacterStations(CharacterSummary character)
        => Build3396210PackedRowBuilder.CreateRowset(
        [
            new Build3396210RowField("stationID", 20),
            new Build3396210RowField("solarSystemID", 20),
            new Build3396210RowField("typeID", 3),
            new Build3396210RowField("itemCount", 3),
            new Build3396210RowField("upkeepState", 17),
        ],
        [
            new PyInteger(character.StationId),
            new PyInteger(character.SolarSystemId),
            new PyInteger(character.StationTypeId),
            new PyInteger(1),
            PyNull.Instance,
        ]);

    public static PyObject CreatePublicCharacterInfo(CharacterSummary character)
        => KeyVal(
            Entry("typeID", new PyInteger(character.CharacterTypeId)),
            Entry("ancestryID", new PyInteger(character.AncestryId)),
            Entry("corporationID", new PyInteger(character.CorporationId)),
            Entry("bloodlineID", new PyInteger(character.BloodlineId)),
            Entry("characterName", new PyText(character.Name)),
            Entry("characterID", new PyInteger(character.CharacterId)),
            Entry("raceID", new PyInteger(character.RaceId)),
            Entry("gender", new PyBoolean(false)));

    public static PyObject CreateEmptyContractLogin()
        => KeyVal(
            Entry("assignedToMe", new PyInteger(0)),
            Entry("needsAttention", new PyInteger(0)),
            Entry("inProgress", new PyInteger(0)));

    private static PyObject KeyVal(params PyDictionaryEntry[] entries)
        => new(Bytes("utillib.KeyVal"), new PyDictionary(entries));

    private static PyDictionaryEntry Entry(string key, PyValue value)
        => new(Bytes(key), value);

    private static PyBuffer Bytes(string value)
        => new(Encoding.UTF8.GetBytes(value));
}
