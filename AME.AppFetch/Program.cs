using Avalonia;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AME.AppFetch.Services;
using AME.Core;
using Microsoft.Win32;

namespace AME.AppFetch;

sealed class Program
{
    public const string Version = "1.0";
    public static Task InstalledPackagesLoaded { get; private set; } = null!;
    
    private static void ConfigureCulture()
    {
        CultureInfo culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = "."; // Force use . insted of ,
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
    }
    
    public static SemaphoreSlim _updatesChecked = new SemaphoreSlim(0, 1);
    public static string? _availableUpdate = null;
    public static bool _updated = false;
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureCulture();
        
        if (args.Length >= 2 && args[0] == "--update")
        {
            try
            {
                try
                {
                    int i = 0;
                    Process? process;
                    while ((process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(args[1])).FirstOrDefault(x => x.Id != Process.GetCurrentProcess().Id)) != null)
                    {
                        if (i > 10)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch (Exception e) { }
                        }

                        if (i > 20)
                        {
                            throw new Exception("Update process timed out: ");
                            Environment.Exit(0);
                        }
                        Thread.Sleep(250);
                        i++;
                    }
                }
                catch (Exception e)
                {
                }
                
                File.Copy(Win32.ProcessEx.GetCurrentProcessFileLocation(), args[1], true);
                Process.Start(new ProcessStartInfo(args[1], $"--updated \"{Win32.ProcessEx.GetCurrentProcessFileLocation()}\"")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                });
                Environment.Exit(0);
            }
            catch (Exception e)
            {
                Win32.MessageBox.ShowError("Error while attempting to update: " + e.ToString(), "Update error");
            }

            return;
        }
        
        
        if (args.Length >= 2 && args[0] == "--updated")
        {
            _updated = true;
            
            try
            {
                int i = 0;
                Process? process;
                while ((process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(args[1])).FirstOrDefault(x => x.Id != Process.GetCurrentProcess().Id)) != null)
                {
                    Thread.Sleep(100);

                    if (i > 10)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception e)
                        {
                        }
                    }
                    if (i > 20)
                    {
                        break;
                    }

                    i++;
                }
                    
                File.Delete(args[1]);
            }
            catch (Exception e)
            {
            }
        }
        
        if (args.Length >= 1 && args[0] == "--uninstall")
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AppFetch", false);

                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"timeout /t 3 /nobreak & del /q /f \"\"{Win32.ProcessEx.GetCurrentProcessFileLocation()}\"\"\"")
                    { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
                    
                Environment.Exit(0);
            }
            catch (Exception e)
            {
                Win32.MessageBox.ShowError("Error while attempting to uninstall: " + e.ToString(), "Uninstall error");
            }

            return;
        }

        InstalledPackagesLoaded = StoreService.Instance.PrepareDataAsync();

        if (!_updated)
        {
            Task.Run(async () =>
            {
                try
                {
                    _availableUpdate = await Update.CheckForUpdate();
                    _updatesChecked.Release();
                }
                catch (Exception e) { }
            });
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    
    

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}