namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// Compares contract values by their JSON form, which also covers collection members that records compare by reference.
/// </summary>
public static class JsonAssert
{
    public static void Equal(object expected, object actual) => Assert.Equal(ApiClient.SerializeJson(expected), ApiClient.SerializeJson(actual));
}
