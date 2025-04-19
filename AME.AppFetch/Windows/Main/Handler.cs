using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AME.AppFetch.Services;
using AME.FluentUI;
using AME.FluentUI.Controls;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DynamicData;
using ReactiveUI;

namespace AME.AppFetch.Windows.Main;

public class StoreItem : ReactiveObject
{
    public required Bitmap? Icon { get; set => this.SetValue(ref field, value); }
    public required string Name { get; set => this.SetValue(ref field, value); }
    public string? Description { get; set => this.SetValue(ref field, value); }

    public object InstallButtonContent { get; set => this.SetValue(ref field, value); } = "Install";
    public bool InstallButtonClickable { get; set => this.SetValue(ref field, value); } = true;
    public bool InstallButtonEnabled { get; set => this.SetValue(ref field, value); } = true;

    
    public StoreService.StoreProductListDto Package { get; set; }
    public Handler Handler { get; set; }
    public async void InstallButtonCommand()
    {
        if (InstallButtonContent is not string command)
            return;
        
        PendingStoreItems.Add(this);
        
        InstallButtonClickable = false;
        var spinner = new ArcSpinner() { Foreground = Brushes.DodgerBlue, SpeedMultiplier = 1.8, IsSpinning = true, ThicknessMultiplier = 0.8, Height = 22, Width = 22 };
        InstallButtonContent = spinner;

        try {
            if (command == "Uninstall")
            {
                var toUninstall = StoreService.Instance.InstalledPackages.FirstOrDefault(x => x.PublisherName == this.Package.PublisherName && x.Title == this.Package.Title) ?? StoreService.Instance.InstalledPackages.First(x => x.PublisherName == this.Package.PublisherName && x.ApplicationTitles.Any(y => y.Equals(this.Package.Title)));
                await StoreService.Instance.UninstallApp(toUninstall.FullName);
                
                InstallButtonContent = "Install";
            }
            else
            {
                var installPackages = await StoreService.Instance.GetPackages(Package.ProductId!, true);

                var progress = new ArcProgress() { ArtificialProgress = true, Foreground = Brushes.White, Background = Brushes.DodgerBlue, ThicknessMultiplier = 0.8, Height = 22, Width = 22 };
                InstallButtonContent = progress;

                await StoreService.Instance.DownloadAndInstallPackagesAsync(installPackages, new Progress<double>(value => progress.Progress = value));
                try
                {
                    await StoreService.Instance.PrepareDataAsync();
                }
                catch (Exception e)
                {
                }

                await Task.Delay(2900);

                var installedMatch =
                    StoreService.Instance.InstalledPackages.FirstOrDefault(x => x.PublisherName == this.Package.PublisherName && x.Title == this.Package.Title) ?? StoreService.Instance.InstalledPackages.FirstOrDefault(x => x.PublisherName == this.Package.PublisherName && x.ApplicationTitles.Any(y => y.Equals(this.Package.Title)));
                if (installedMatch is not null)
                {
                    installedMatch.ProductID = this.Package.ProductId;

                    InstallButtonContent = "Uninstall";
                }
                else
                {
                    StoreService.Instance.InstalledPackages.Add(new StoreService.InstalledPackage()
                    {
                        ProductID = Package.ProductId,
                    });

                    InstallButtonContent = "Installed";
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            
            Handler.ErrorTitle = $"Failed to {(command == "Uninstall" ? "uninstall" : "install")} " + Name;
            if (e.Message == "Microsoft Edge is required")
                Handler.ErrorDescription = "Microsoft Edge is required to install " + Name + ".";
            else
                Handler.ErrorDescription = $"An unexpected error occured while attempting to {(command == "Uninstall" ? "uninstall" : "install")} " + Name + ".";

            Handler.Error = true;
            
            InstallButtonContent = "Install";
        }

        PendingStoreItems.Remove(this);

        InstallButtonEnabled = (string?)InstallButtonContent != "Installed";
        InstallButtonClickable = true;
    }
    
    public static List<StoreItem> PendingStoreItems = new ();
}

public class Handler : ReactiveObject
{
    public ObservableCollection<StoreItem> Items { get; private set; } = new ObservableCollection<StoreItem>();
    public bool IsLoading { get; set => this.SetValue(ref field, value); } = false;
    
    public string? ErrorTitle { get; set => this.SetValue(ref field, value); }
    public string? ErrorDescription { get; set => this.SetValue(ref field, value); }
    public bool Error { get; set => this.SetValue(ref field, value); }

    public async Task Search(string searchTerm)
    {
        Items.Clear();
        IsLoading = true;
        Error = false;
        
        var guiList = new ObservableCollection<StoreItem>();
        try
        {
            var list = await StoreService.Instance.SearchProductsAsync(searchTerm);
            try
            {
                await Program.InstalledPackagesLoaded;
            }
            catch (Exception e)
            {
            }

            await Task.Run(() =>
            {
                foreach (var result in list)
                {
                    if (StoreItem.PendingStoreItems.Any(x => x.Package.ProductId == result.ProductId))
                    {
                        guiList.Add(StoreItem.PendingStoreItems.First(x => x.Package.ProductId == result.ProductId));
                        continue;
                    }
                    guiList.Add(new StoreItem()
                    {
                        Icon = result.IconBitmap,
                        Name = result.Title ?? "Unknown",
                        Package = result,
                        Handler = this,
                    });

                    if (result.Description != null)
                    {
                        var splitDescription = result.Description.Split("\n");
                        if (splitDescription.First().Length >= 100)
                        {
                            var periodSplitDescription = result.Description.Split(".").First() + ".";
                            if (periodSplitDescription.Length < 100 || periodSplitDescription.Length < splitDescription.Length)
                                guiList.Last().Description = periodSplitDescription.Trim();
                            else
                                guiList.Last().Description = splitDescription.First().Trim();
                        } else
                            guiList.Last().Description = splitDescription.First().Trim();
                    }
                    
                    var installedMatch = StoreService.Instance.InstalledPackages.FirstOrDefault(x => (x.PublisherName == result.PublisherName && x.Title == result.Title) || x.ProductID == result.ProductId) ?? StoreService.Instance.InstalledPackages.FirstOrDefault(x => x.PublisherName == result.PublisherName && x.ApplicationTitles.Any(y => y.Equals(result.Title)));
                    if (installedMatch != null)
                    {
                        var item = result;
                        var guiItem = guiList.Last();
                        Dispatcher.UIThread.Invoke(() =>
                            guiItem.InstallButtonContent = new ArcSpinner() { Foreground = Brushes.DodgerBlue, SpeedMultiplier = 1.8, ThicknessMultiplier = 0.8, IsSpinning = true, Height = 22, Width = 22 });
                        Task.Run(async () =>
                        {
                            try
                            {
                                if (StoreService.Instance.InstalledPackages.Any(x => x.ProductID == item.ProductId))
                                {
                                    bool matchFound = !string.IsNullOrEmpty(StoreService.Instance.InstalledPackages.First(x => x.ProductID == item.ProductId).FullName);
                                    guiItem.InstallButtonContent = !matchFound ? "Installed" : "Uninstall";
                                    guiItem.InstallButtonEnabled = matchFound;
                                    return;
                                }

                                guiItem.InstallButtonClickable = false;
                                var installPackages = await StoreService.Instance.GetPackages(item.ProductId!, false);
                                
                                var match = installPackages.FirstOrDefault(x =>
                                    x.Name!.Split('_').First() == installedMatch.FullName.Split('_').First() &&
                                    x.Name!.Split('_').Last() == installedMatch.FullName.Split('_').Last());
                    
                                if (match == null)
                                    throw new Exception("Match not found");
                    
                                if (match.Name!.Split('_')[1] != installedMatch.FullName.Split('_')[1])
                                    guiItem.InstallButtonContent = "Update";
                                else
                                    guiItem.InstallButtonContent = "Uninstall";
                            }
                            catch (Exception e)
                            {
                                guiItem.InstallButtonContent = "Install";
                            }
                            
                            
                            guiItem.InstallButtonEnabled = (string?)guiItem.InstallButtonContent != "Installed";
                            guiItem.InstallButtonClickable = true;
                        });
                    }
                }
            });
        }
        catch (Exception e)
        {
            ErrorTitle = "Error searching for apps";
            ErrorDescription = "Network request failed. Ensure you have a stable internet connection.";
            Error = true;
        }
        
        IsLoading = false;
        await Task.Delay(50);
        Items.AddRange(guiList);
    }
}