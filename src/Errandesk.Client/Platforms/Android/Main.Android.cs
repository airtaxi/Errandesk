using Android.App;
using Android.Runtime;
using Uno.UI.Hosting;

namespace Errandesk.Client.Droid;

[global::Android.App.Application(Label = "@string/ApplicationName", Icon = "@mipmap/icon", LargeHeap = true, HardwareAccelerated = true, Theme = "@style/Theme.App.Starting")]
public class Application(IntPtr javaReference, JniHandleOwnership transfer) : Uno.UI.Runtime.Android.NativeApplication(javaReference, transfer)
{
    protected override UnoPlatformHost CreateHost() => UnoPlatformHostBuilder.Create().App(() => new App()).UseAndroid().Build();
}
