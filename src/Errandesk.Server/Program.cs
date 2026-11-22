using Errandesk.Server;
using Errandesk.Server.Configuration;

var loadResult = ServerSettingsLoader.FromEnvironment().Load();
if (!loadResult.IsValid)
{
    Console.Error.WriteLine("Errandesk cannot start because its configuration is invalid:");
    foreach (var error in loadResult.Errors) Console.Error.WriteLine($"  - {error}");
    return 1;
}

var builder = ErrandeskServerApplication.CreateBuilder(new WebApplicationOptions { Args = args }, loadResult.Settings);
var application = ErrandeskServerApplication.Build(builder);
await application.RunAsync();
return 0;
