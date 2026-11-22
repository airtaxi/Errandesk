using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace Errandesk.Server.Data;

/// <summary>
/// Registers the process-wide BSON serializers and conventions used by every stored document.
/// </summary>
/// <remarks>
/// GUIDs use the standard UUID representation. <see cref="DateOnly"/> values are stored as <c>yyyy-MM-dd</c> strings, which sort chronologically and carry no time zone.
/// Documents in the <c>Errandesk.Server</c> namespaces use camelCase element names, string enums, and ignore unknown elements.
/// </remarks>
public static class MongoDbSerialization
{
    private const string ConventionPackName = "Errandesk";

    private static readonly Lock s_registrationLock = new();
    private static bool s_isRegistered;

    public static void Register()
    {
        lock (s_registrationLock)
        {
            if (s_isRegistered) return;

            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.TryRegisterSerializer(new DateOnlySerializer(BsonType.String));

            var conventionPack = new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new EnumRepresentationConvention(BsonType.String),
                new IgnoreExtraElementsConvention(true),
            };
            ConventionRegistry.Register(ConventionPackName, conventionPack, type => type.Namespace?.StartsWith("Errandesk.Server", StringComparison.Ordinal) == true);

            s_isRegistered = true;
        }
    }
}
