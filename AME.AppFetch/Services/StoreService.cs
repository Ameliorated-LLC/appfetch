using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Media.Imaging;
using Microsoft.Win32;

namespace AME.AppFetch.Services;

public partial class StoreService
{
    public static StoreService Instance { get; private set; } = new StoreService();
    
    public static async Task Run()
    {
        //GetResourceValue(@"C:\Users\Styris\Downloads\resources.pri", "ms-resource:AppStoreName");
        //return;
        
        var service = new StoreService();

        await service.PrepareDataAsync();
        
        var result = await service.SearchProductsAsync("WhatsApp");
        Console.WriteLine(result.Count);
        foreach (var item in result)
        {
            Console.WriteLine(item.ProductId + " : " + item.PublisherName + " : " + item.Title);
            var installedMatch = service.InstalledPackages.FirstOrDefault(x => x.PublisherName == item.PublisherName && x.Title == item.Title);
            if (installedMatch != null)
            {
                try
                {
                    Console.WriteLine("Waiting");
                    var installPackages = await service.GetPackages(item.ProductId!, false);
                    Console.WriteLine("Waited");
                    var match = installPackages.FirstOrDefault(x =>
                        x.Name!.Split('_').First() == installedMatch.FullName.Split('_').First() &&
                        x.Name!.Split('_').Last() == installedMatch.FullName.Split('_').Last());
                    
                    if (match == null)
                        throw new Exception("Match not found");
                    
                    if (match.Name!.Split('_')[1] != installedMatch.FullName.Split('_')[1])
                        Console.WriteLine("Update");
                    else
                        Console.WriteLine("Uninstall");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine("Install");
                }
            } else
                Console.WriteLine("Install");
        }

        Console.Write("Enter product ID: ");
        var selection = Console.ReadLine()!.Trim();
        Console.WriteLine("Selected " + selection);


        var packages = await service.GetPackages(selection, true);
        foreach (var package in packages)
        {
            Console.WriteLine(package.UpdateIdentifier);
            Console.WriteLine(package.FileExtension);
            Console.WriteLine(package.Name);
            Console.WriteLine(package.PackageId);
            Console.WriteLine(package.ResourceUri);
        }

    }

    public async Task UninstallApp(string fullName)
    {
        var process = Process.Start(new ProcessStartInfo()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoP -C \"Remove-AppxPackage -Package '{fullName}'\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        process!.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
                Console.WriteLine(args.Data);
        };
        process!.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
                Console.WriteLine(args.Data);
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process!.WaitForExitAsync();


        if (process.ExitCode != 0)
            throw new Exception("PowerShell exited with code " + process.ExitCode);
    }
    
    private static bool IsUWP(string productId) => !productId.StartsWith("xp", StringComparison.OrdinalIgnoreCase);
    private static bool IsSupported(string productId) => !productId.StartsWith("xm", StringComparison.OrdinalIgnoreCase);

    public async Task<List<StorePackageDto>> GetPackages(string productId, bool getDownloadUrl)
    {
        if (!IsUWP(productId))
            return await SearchInstallerProductsAsync(productId);

        var cookie = await GetCookieAsync();
        var categoryId = await GetCategoryIDAsync(productId);
        var xmlList = await FetchFileListXMLAsync(categoryId, cookie, "Retail");

        var packages = await ParsePackagesAsync(xmlList, "Retail", getDownloadUrl);
        packages.Sort((x, y) => DateTime.Compare(y.LastModified.GetValueOrDefault(), x.LastModified.GetValueOrDefault()));
        packages.RemoveAll(x => x != packages.FirstOrDefault(y => _namePatternRegex.Match(x.Name!).Value == _namePatternRegex.Match(y.Name!).Value));
        return packages;
    }

    public async Task DownloadAndInstallPackagesAsync(List<StorePackageDto> packages, IProgress<double> progress)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString().Replace("-", "").Replace("{", "").Replace("}", ""));
        
        using var client = new HttpProgressClient();

        double currentProgress = 0;
        var totalSize = packages.Sum(x => x.Size.GetValueOrDefault());
        double finishedBytes = 0;
        
        client.ProgressChanged += (size, downloaded, percentage) =>
        {
            var progressValue = Math.Min((((double)Math.Round((downloaded + finishedBytes) / totalSize, 1) * 100) / 2), 50);
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (progressValue != currentProgress)
            {
                currentProgress = progressValue;
                progress.Report(currentProgress);
            }
        };

        try
        {
            Directory.CreateDirectory(Path.Combine(tempFolder, "Dependencies"));
            
            for (var i = 0; i < packages.Count; i++)
            {
                
                Console.WriteLine(packages[i].Name);
                var path = IsDependency(packages[i].Name!)
                    ? Path.Combine(tempFolder, "Dependencies", packages[i].Name + "." + packages[i].FileExtension)
                    : Path.Combine(tempFolder, packages[i].Name + "." + packages[i].FileExtension);
                await client.StartDownload(packages[i].ResourceUri!, path);
                finishedBytes += packages[i].Size.GetValueOrDefault();
            }

            bool foundMatch = false;
            int c = 0;
            foreach (var package in packages.Where(x => IsDependency(x.Name!) && !x.Name!.Contains("Microsoft.Advertising")).Concat(packages.Where(x => !IsDependency(x.Name!) && !x.Name!.Contains("Microsoft.Advertising"))))
            {
                c++;
 
                var result = await InstallPackage(package, tempFolder);
            
                var progressValue = ((c / packages.Count(x => !x.Name!.Contains("Microsoft.Advertising")) * 100) / 2) + 50;
                progress.Report(progressValue);

                if (result.ExitCode != 0 && result.AdvertisingRequired)
                {
                    foreach (var advertisingPackage in packages.Where(x => x.Name!.Contains("Microsoft.Advertising")))
                        await InstallPackage(advertisingPackage, tempFolder);
                    
                    result = await InstallPackage(package, tempFolder);
                }
                    
                if (result.ExitCode != 0 && !result.HigherVersionInstalled && !IsDependency(package.Name!) && !result.WrongArch)
                    throw new Exception(result.EdgeRequired ? "Microsoft Edge is required" : "PowerShell exited with code " + result.ExitCode);
                else if (!IsDependency(package.Name!) && !result.HigherVersionInstalled && !result.WrongArch)
                    foundMatch = true;
            }
            if (!foundMatch)
                throw new Exception("Found no matching package for architecture");
        }
        finally
        {
            try
            {
                Directory.Delete(tempFolder, true);
            }
            catch (Exception e)
            { }
        }
    }

    private async Task<(int ExitCode, bool HigherVersionInstalled, bool WrongArch, bool EdgeRequired, bool AdvertisingRequired)> InstallPackage(StorePackageDto package, string downloadFolder)
    {
        var file = IsDependency(package.Name!)
            ? Path.Combine(downloadFolder, "Dependencies", package.Name + "." + package.FileExtension)
            : Path.Combine(downloadFolder, package.Name + "." + package.FileExtension);
        var startInfo = new ProcessStartInfo()
        {
            FileName = IsUWP(package.PackageId!) ? "powershell.exe" : file.EndsWith(".msi") ? "msiexec.exe" : file,
            Arguments = IsUWP(package.PackageId!) ? $"-NoP -C \"Add-AppxPackage -Path '{file}' -ForceApplicationShutdown\"" :
                file.EndsWith(".msi") ? $"/i \"{file}\" /qn" : package.CommandLines,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Process process = new Process();
        process.StartInfo = startInfo;

        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.RedirectStandardError = false;
            process.StartInfo.RedirectStandardOutput = false;
            process.Start();
        }

        bool higherVersionInstalled = false;
        bool edgeRequired = false;
        bool advertisingRequired = false;
        bool wrongArch = false;
        process!.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                if (args.Data.Contains("0x80073D06"))
                    higherVersionInstalled = true;
                if (args.Data.Contains("0x80073CF1"))
                    wrongArch = true;
                if (args.Data.Contains("Microsoft.MicrosoftEdge.Stable"))
                    edgeRequired = true;
                if (args.Data.Contains("Microsoft.Advertising.Xaml"))
                    advertisingRequired = true;
                Console.WriteLine(args.Data);
            }
        };
        process!.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                if (args.Data.Contains("0x80073D06"))
                    higherVersionInstalled = true;
                if (args.Data.Contains("0x80073CF1"))
                    wrongArch = true;
                if (args.Data.Contains("Microsoft.MicrosoftEdge.Stable"))
                    edgeRequired = true;
                if (args.Data.Contains("Microsoft.Advertising.Xaml"))
                    advertisingRequired = true;
                Console.WriteLine(args.Data);
            }
        };
        if (!process.StartInfo.UseShellExecute)
        {
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }

        await process!.WaitForExitAsync();

        return (process.ExitCode, higherVersionInstalled, wrongArch, edgeRequired, advertisingRequired);
    }
    
    public static readonly List<string> DependencyList = new List<string>
    {
        "Microsoft.VCLibs",
        "Microsoft.NET",
        "Microsoft.UI",
        "Microsoft.WinJS",
        "Microsoft.WindowsAppRuntime",
        "Microsoft.Advertising"
    };

    public static bool IsDependency(string name)
    {
        return DependencyList.Any(dep => name.StartsWith(dep, StringComparison.OrdinalIgnoreCase));
    }

    public class InstalledPackage
    {
        public string PublisherName { get; set; }
        public string Title { get; set; }
        public List<string> ApplicationTitles { get; set; }
        public string Version { get; set; }
        public string FullName { get; set; }
        public string? ProductID { get; set; }
    }
    public List<InstalledPackage> InstalledPackages { get; } = new();
    
    public async Task PrepareDataAsync()
    {
        await Task.Run(() =>
        {
            InstalledPackages.Clear();
            try
            {
                using var dataKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\StateRepository\Cache\Package\Data");
                if (dataKey == null)
                    return;
                
                foreach (string subKeyName in dataKey.GetSubKeyNames())
                {
                    using var subKey = dataKey.OpenSubKey(subKeyName);
                    if (subKey == null)
                        continue;
                    
                    string? fullName = (string?)subKey.GetValue("PackageFullName", null);
                    string? installLocation = (string?)subKey.GetValue("InstalledLocation", null);
                    if (fullName == null || installLocation == null || !File.Exists(Path.Combine(installLocation, "AppxManifest.xml")))
                        continue;
                    
                    XDocument doc = XDocument.Load(Path.Combine(installLocation, "AppxManifest.xml"));

                    // Get the default namespace dynamically from the root element
                    XNamespace ns = doc.Root!.GetDefaultNamespace();

                    XElement identityElement = doc.Root!.Element(ns + "Identity")!;
                    string identityVersion = identityElement!.Attribute("Version")!.Value;

                    XElement propertiesElement = doc.Root!.Element(ns + "Properties")!;

                    string displayName = propertiesElement!.Element(ns + "DisplayName")!.Value;
                    if (displayName.StartsWith("ms-resource:"))
                    {
                        var resource = LoadResource(displayName, fullName, Path.Combine(installLocation, "resources.pri"));
                        if (resource == null)
                            continue;
                        displayName = resource;
                    }
                    string publisherDisplayName = propertiesElement!.Element(ns + "PublisherDisplayName")!.Value;
                    if (publisherDisplayName.StartsWith("ms-resource:"))
                    {
                        var resource = LoadResource(publisherDisplayName, fullName, Path.Combine(installLocation, "resources.pri"));
                        if (resource == null)
                            continue;
                        publisherDisplayName = resource;
                    }
                    
                    InstalledPackages.Add(new InstalledPackage()
                    {
                        Title = displayName,
                        PublisherName = publisherDisplayName,
                        Version = identityVersion,
                        FullName = fullName,
                        ApplicationTitles = new List<string>(),
                    });
                    
                    var applicationElements = doc.Root!.Element(ns + "Applications")?.Elements(ns + "Application")!;
                    foreach (var applicationElement in applicationElements ?? [])
                    {
                        var applicationDisplayName = applicationElement.Elements().FirstOrDefault(x => x.Name.LocalName == "VisualElements")?.Attribute("DisplayName")?.Value;
                        if (!string.IsNullOrWhiteSpace(applicationDisplayName))
                        {
                            if (applicationDisplayName.StartsWith("ms-resource:"))
                            {
                                var resource = LoadResource(applicationDisplayName, fullName, Path.Combine(installLocation, "resources.pri"));
                                if (resource == null)
                                    continue;
                                applicationDisplayName = resource;
                            }
                            InstalledPackages.Last().ApplicationTitles.Add(applicationDisplayName);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        });
    }

    private string? LoadResource(string resourceKey, string packageFullName, string resourcesFile)
    {
        if (!File.Exists(resourcesFile))
            return null;

        resourceKey = resourceKey.Replace("ms-resource:resources/", "ms-resource:", StringComparison.OrdinalIgnoreCase);
        string resourceKeyPath = $"ms-resource://{packageFullName.Split('_').First()}/Resources/" + resourceKey.Split(':').Last();
        string resourceReference = $"@{{{resourcesFile}?{resourceKeyPath}}}";
                        
        StringBuilder sb = new StringBuilder(1024);
        int hr = SHLoadIndirectString(resourceReference, sb, sb.Capacity, IntPtr.Zero);

        if (hr != 0)
            return null;

        return sb.ToString();
    }
    
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        StringBuilder pszOutBuf,
        int cchOutBuf,
        IntPtr ppvReserved);

    private const string _fe3DeliveryUrl = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx";

    private const string _storeApiUrl = "https://storeedgefd.dsx.mp.microsoft.com/v9.0";

    // private const string FilteredSearchApiUrl = "https://apps.microsoft.com/store/api/Products/GetFilteredSearch";
    private const string _searchApiUrl = "https://apps.microsoft.com/api/products/search";

    private static readonly Dictionary<string, string> _soapXmlHeaders = new Dictionary<string, string>
    {
        { "user-agent", "Mozilla/5.0 (Windows NT 10.0; rv:107.0) Gecko/20100101 Firefox/107.0" },
        { "Accept", "*/*" },
        { "Content-Type", "application/soap+xml" }
    };

    private static readonly Dictionary<string, string> _jsonHeaders = new Dictionary<string, string>
    {
        { "user-agent", "Mozilla/5.0 (Windows NT 10.0; rv:107.0) Gecko/20100101 Firefox/107.0" },
        { "content-type", "application/json;charset=utf-8" },
        { "accept", "application/json" }
    };

    private static readonly Regex _wuCategoryIdRegex = new Regex("\"WuCategoryId\":\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex _namePatternRegex = new Regex("^[^_]+", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly HttpClient _httpClient = new HttpClient();

    // Primitive types
    [JsonSerializable(typeof(byte))]
    [JsonSerializable(typeof(sbyte))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(short))]
    [JsonSerializable(typeof(ushort))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(byte?))]
    [JsonSerializable(typeof(sbyte?))]
    [JsonSerializable(typeof(int?))]
    [JsonSerializable(typeof(uint?))]
    [JsonSerializable(typeof(short?))]
    [JsonSerializable(typeof(ushort?))]
    [JsonSerializable(typeof(long?))]
    [JsonSerializable(typeof(ulong?))]
    [JsonSerializable(typeof(float?))]
    [JsonSerializable(typeof(double?))]
    [JsonSerializable(typeof(decimal?))]
    [JsonSerializable(typeof(bool?))]

    // Additional types
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(string[]))]

    [JsonSerializable(typeof(Enum))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(DateTime?))]
    [JsonSerializable(typeof(DateTimeOffset?))]
    [JsonSerializable(typeof(Guid?))]
    [JsonSerializable(typeof(Uri))]
    [JsonSerializable(typeof(StorePackageDto))]
    [JsonSerializable(typeof(StoreProductListDto))]
    [JsonSerializable(typeof(StoreSearchResponseDto))]
    [JsonSerializable(typeof(StoreInstallerPackageResponseDto))]
    [JsonSerializable(typeof(Data))]
    [JsonSerializable(typeof(Versions))]
    [JsonSerializable(typeof(DefaultLocale))]
    [JsonSerializable(typeof(Installers))]
    [JsonSerializable(typeof(InstallerSwitches))]
    internal partial class SourceGenerationContext : JsonSerializerContext { }
    
    public async Task<List<StorePackageDto>> SearchInstallerProductsAsync(string productId)
    {
        string requestUrl = $"{_storeApiUrl}/packageManifests/{productId}?Market=US";

        HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);

        if (response.IsSuccessStatusCode)
        {
            var results = new List<StorePackageDto>();
            
            var responseData = await response.Content.ReadFromJsonAsync<StoreInstallerPackageResponseDto>(new JsonSerializerOptions() {TypeInfoResolver = new SourceGenerationContext()});
            if (responseData is null)
                throw new Exception("Invalid response from the server.");

            List<string> urls = new List<string>();
            foreach (var installer in responseData.Data!.Versions!.First().Installers!)
            {
                if (urls.Contains(installer.InstallerUrl!))
                    continue;
                urls.Add(installer.InstallerUrl!);
                string extension = installer.InstallerType ?? installer.InstallerUrl!.Substring(installer.InstallerUrl.LastIndexOf('.'));
                if (String.IsNullOrWhiteSpace(extension) || extension == "exe" || extension == "msi")
                {
                    var filename = responseData.Data!.Versions!.First().DefaultLocale!.PackageName + "-" + installer.Architecture;
                    results.Add(new StorePackageDto()
                    {
                        Name = filename,
                        FileExtension = extension,
                        ResourceUri = installer.InstallerUrl,
                        LastModified = DateTime.Now,
                        PackageId = productId,
                        CommandLines = installer.InstallerSwitches?.Silent
                    });
                }
            }

            return results;
        }

        throw new Exception("Failed to search the product: " + (response.ReasonPhrase ?? response.StatusCode.ToString()));
    }
    public async Task<List<StoreProductListDto>> SearchProductsAsync(string query)
    {
        string requestUrl = $"{_searchApiUrl}?gl=US&hl=en-us&query={Uri.EscapeDataString(query)}&mediaType=all&age=all&price=all&category=all&subscription=all";

        HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);

        if (response.IsSuccessStatusCode)
        {
            var responseData = await response.Content.ReadFromJsonAsync<StoreSearchResponseDto>(new JsonSerializerOptions() {TypeInfoResolver = new SourceGenerationContext()});
            if (responseData is null)
                throw new Exception("Invalid response from the server.");

            var results = new List<StoreProductListDto>();
            if (responseData.HighlightedList != null)
                results.AddRange(responseData.HighlightedList);
            if (responseData.ProductsList != null)
                results.AddRange(responseData.ProductsList);

            results.RemoveAll(x => !IsSupported(x.ProductId!) || (!string.IsNullOrWhiteSpace(x.DisplayPrice) && x.DisplayPrice != "Free" && (!double.TryParse(x.DisplayPrice, out var price) || price != 0)));
            
            foreach (var result in results.Where(x => !string.IsNullOrEmpty(x.IconUrl)))
            {
                try
                {
                    var imageResponse = await _httpClient.GetAsync(result.IconUrl);
                    imageResponse.EnsureSuccessStatusCode();
                    var data = await imageResponse.Content.ReadAsByteArrayAsync();
                    result.IconBitmap = new Bitmap(new MemoryStream(data));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred while downloading image '{result.IconUrl}' : {ex.Message}");
                }
            }

            return results;
        }

        throw new Exception("Failed to search the product: " + (response.ReasonPhrase ?? response.StatusCode.ToString()));
    }

    private string? _cookie; 
    public async Task<string> GetCookieAsync()
    {
        if (_cookie != null)
            return _cookie;
        
        HttpRequestMessage request = CreateSoapRequest(CookieContent, _fe3DeliveryUrl);
        HttpResponseMessage response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            string responseString = await response.Content.ReadAsStringAsync();

            var encryptedDataElement = XElement.Parse(responseString).Descendants().FirstOrDefault(x => x.Name.LocalName == "EncryptedData");
            if (encryptedDataElement != null)
            {
                _cookie = encryptedDataElement.Value;
                return encryptedDataElement.Value;
            }

            throw new Exception("EncryptedData element not found in response.");
        }

        throw new Exception("Failed to get a cookie");
    }

    private async Task<string> GetCategoryIDAsync(string id)
    {
        string url = $"{_storeApiUrl}/products/{id}?market=US&locale=en-us&deviceFamily=Windows.Desktop";

        HttpResponseMessage response = await _httpClient.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            string jsonResponse = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(jsonResponse);

            if (document.RootElement.TryGetProperty("Payload", out JsonElement payload) &&
                payload.TryGetProperty("Skus", out JsonElement skus) &&
                skus.ValueKind == JsonValueKind.Array &&
                skus.GetArrayLength() > 0)
            {
                JsonElement firstSku = skus[0];
                if (firstSku.TryGetProperty("FulfillmentData", out JsonElement fulfillmentDataElement))
                {
                    string? fulfillmentData = fulfillmentDataElement.GetString();
                    if (!string.IsNullOrEmpty(fulfillmentData))
                    {
                        var match = _wuCategoryIdRegex.Match(fulfillmentData);
                        if (match.Success && match.Groups.Count > 1)
                        {
                            return match.Groups[1].Value;
                        }
                    }
                }

                throw new Exception("The selected app is not UWP 2");
            }

            throw new Exception("The selected app is not UWP 1");
        }

        throw new Exception("Failed to get category id");
    }

    private async Task<string> FetchFileListXMLAsync(string categoryID, string cookie, string ring)
    {
        string requestXml = WUAContent
            .Replace("{1}", cookie)
            .Replace("{2}", categoryID)
            .Replace("{3}", ring);

        HttpRequestMessage request = CreateSoapRequest(requestXml, _fe3DeliveryUrl);
        HttpResponseMessage response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            string responseData = await response.Content.ReadAsStringAsync();
            string cleanedXml = responseData.Replace("&lt;", "<").Replace("&gt;", ">");

            return cleanedXml;
        }

        throw new Exception("Failed to get file list xml");
    }
    
    private async Task<string> GetUriAsync(string updateID, string revision, string ring, string digets)
    {
        string requestXml = UrlContent
            .Replace("{1}", updateID)
            .Replace("{2}", revision)
            .Replace("{3}", ring);

        HttpRequestMessage request = CreateSoapRequest(requestXml, _fe3DeliveryUrl + "/secured");
        HttpResponseMessage response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            string responseData = await response.Content.ReadAsStringAsync();
            
            var xmlDoc = XDocument.Parse(responseData);

            foreach (var node in xmlDoc.Descendants().Where(x => x.Name.LocalName == "FileLocation"))
            {
                if (node.Descendants().First(x => x.Name.LocalName == "FileDigest").Value == digets)
                    return node.Descendants().First(x => x.Name.LocalName == "Url").Value;
            }
        }

        return "";
    }
    
    private async Task<List<StorePackageDto>> ParsePackagesAsync(string xmlList, string ring, bool getDownloadUrl)
    {
        var result = new List<StorePackageDto>();
        var xmlDoc = XDocument.Parse(xmlList);
        var packageMap = new Dictionary<string, string>();

        // Process all "File" elements
        foreach (var node in xmlDoc.Descendants().Where(x => x.Name.LocalName == "File"))
        {
            var installerId = node.Attribute("InstallerSpecificIdentifier")?.Value;
            if (installerId != null)
            {
                var digest = node.Attribute("Digest")?.Value;
                var modified = node.Attribute("Modified")?.Value;
                var fileName = node.Attribute("FileName")?.Value;
                var size = node.Attribute("Size")?.Value;

                if (digest != null && modified != null && fileName != null && size != null)
                {
                    int lastDot = fileName.LastIndexOf('.');
                    string ext = (lastDot >= 0 && lastDot < fileName.Length - 1)
                        ? fileName.Substring(lastDot + 1)
                        : string.Empty;

                    // Format: "extension|size|digest|modified"
                    var packageData = $"{ext}|{size}|{digest}|{modified}";
                    if (!packageMap.ContainsKey(installerId))
                    {
                        packageMap.Add(installerId, packageData);
                    }
                }
            }
        }

        // Process all "SecuredFragment" elements
        foreach (var node in xmlDoc.Descendants().Where(x => x.Name.LocalName == "SecuredFragment"))
        {
            // Navigate: SecuredFragment -> Parent -> Grandparent -> ApplicabilityRules -> Metadata -> AppxPackageMetadata -> AppxMetadata
            var parent = node.Parent;
            var grandparent = parent?.Parent;
            if (grandparent == null)
                continue;

            var applicabilityRules = grandparent.Elements()
                .FirstOrDefault(x => x.Name.LocalName == "ApplicabilityRules");
            if (applicabilityRules == null)
                continue;

            var metadata = applicabilityRules.Elements()
                .FirstOrDefault(x => x.Name.LocalName == "Metadata");
            if (metadata == null)
                continue;

            var appxPackageMetadata = metadata.Elements()
                .FirstOrDefault(x => x.Name.LocalName == "AppxPackageMetadata");
            if (appxPackageMetadata == null)
                continue;

            var appxMetadata = appxPackageMetadata.Elements()
                .FirstOrDefault(x => x.Name.LocalName == "AppxMetadata");
            if (appxMetadata == null)
                continue;

            var packageMoniker = appxMetadata.Attribute("PackageMoniker")?.Value;
            if (packageMoniker != null)
            {
                if (!packageMap.TryGetValue(packageMoniker, out var packageData))
                    continue;

                var parts = packageData.Split('|');
                if (parts.Length < 4)
                    continue;

                var ext = parts[0];
                if (!double.TryParse(parts[1], out double pkgSize))
                    pkgSize = 0;
                var digest = parts[2];
                if (!DateTime.TryParse(parts[3], out DateTime lastModified))
                    lastModified = DateTime.MinValue;

                var updateIdentity = grandparent.Elements()
                    .FirstOrDefault(x => x.Name.LocalName == "UpdateIdentity");
                if (updateIdentity == null)
                    continue;
                
                if ((packageMoniker.Contains(SystemInfoEx.SystemArchitecture.ToString().ToLower())) ||
                    (packageMoniker.Contains("neutral") && !ext.StartsWith("e")))
                {
                    var updateID = updateIdentity.Attribute("UpdateID")?.Value;
                    var revisionNumber = updateIdentity.Attribute("RevisionNumber")?.Value;
                    if (updateID == null || revisionNumber == null)
                        continue;

                    // Get the package ID from the grandparent's parent ("ID" element)
                    var greatGrandparent = grandparent.Parent;
                    var idElement = greatGrandparent?.Elements()
                        .FirstOrDefault(x => x.Name.LocalName == "ID");
                    if (idElement == null)
                        continue;
                    var packageId = idElement.Value;

                    // Call GetUri (implementation ignored)
                    var resourceUri = !getDownloadUrl ? null : await GetUriAsync(updateID, revisionNumber, ring, digest);

                    result.Add(new StorePackageDto
                    {
                        Name = packageMoniker,
                        FileExtension = ext,
                        ResourceUri = resourceUri,
                        Revision = revisionNumber,
                        UpdateIdentifier = updateID,
                        PackageId = packageId,
                        Size = pkgSize,
                        Checksum = digest,
                        LastModified = lastModified,
                        OriginalIndex = null,
                        CommandLines = null
                    });
                }
            }
        }
        return result;
    }
    

    private HttpRequestMessage CreateSoapRequest(string content, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/soap+xml")
        };

        foreach (var header in _soapXmlHeaders)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }
}