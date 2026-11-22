using Uno.UI.Hosting;

namespace Errandesk.Client.iOS;

public static class EntryPoint
{
    public static void Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create().App(() => new App()).UseAppleUIKit(builder => builder.UseUIApplicationDelegate<ErrandeskApplicationDelegate>()).Build();
        host.Run();
    }
}
