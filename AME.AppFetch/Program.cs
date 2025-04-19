using Avalonia;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AME.AppFetch.Services;

namespace AME.AppFetch;

sealed class Program
{
    public static Task InstalledPackagesLoaded { get; private set; } = null!;
    
    private static void ConfigureCulture()
    {
        CultureInfo culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = "."; // Force use . insted of ,
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
    }
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureCulture();

        InstalledPackagesLoaded = StoreService.Instance.PrepareDataAsync();
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    
    

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}