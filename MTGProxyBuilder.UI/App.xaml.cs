using System.Windows;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var cache = new CacheManager();
            cache.ClearAllCaches();
        }
        catch { }

        base.OnExit(e);
    }
}
