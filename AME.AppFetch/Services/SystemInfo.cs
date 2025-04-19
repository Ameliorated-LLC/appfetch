using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace AME.AppFetch.Services;

public static class SystemInfo
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle GetCurrentProcess();
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct RTL_OSVERSIONINFOEX
    {
        internal uint dwOSVersionInfoSize;
        internal uint dwMajorVersion;
        internal uint dwMinorVersion;
        internal uint dwBuildNumber;
        internal uint dwPlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string szCSDVersion;
    }

    [DllImport("ntdll")]
    public static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX lpVersionInformation);

    public enum MachineType : ushort
    {
        IMAGE_FILE_MACHINE_UNKNOWN = 0x0,
        IMAGE_FILE_MACHINE_ALPHA = 0x184, //Digital Equipment Corporation (DEC) Alpha (32-bit)
        IMAGE_FILE_MACHINE_AM33 = 0x1d3, //Matsushita AM33, now MN103 (32-bit) part of Panasonic Corporation

        IMAGE_FILE_MACHINE_AMD64 =
            0x8664, //AMD (64-bit) - was Advanced Micro Devices, now means x64  - OVERLOADED _AMD64 = 0x8664 - http://msdn.microsoft.com/en-us/library/windows/desktop/ms680313(v=vs.85).aspx  
        IMAGE_FILE_MACHINE_ARM = 0x1c0, //ARM little endian (32-bit), ARM Holdings, later versions 6+ used in iPhone, Microsoft Nokia N900
        IMAGE_FILE_MACHINE_ARMV7 = 0x1c4, //ARMv7 or IMAGE_FILE_MACHINE_ARMNT (or higher) Thumb mode only (32 bit).
        IMAGE_FILE_MACHINE_ARM64 = 0xaa64, //ARM8+ (64-bit)
        IMAGE_FILE_MACHINE_EBC = 0xebc, //EFI byte code (32-bit), now (U)EFI or (Unified) Extensible Firmware Interface
        IMAGE_FILE_MACHINE_I386 = 0x14c, //Intel 386 or later processors and compatible processors (32-bit)
        IMAGE_FILE_MACHINE_I860 = 0x14d, //Intel i860 (aka 80860) (32-bit) was a RISC microprocessor design introduced by Intel in 1989, this was depricated in 90's
        IMAGE_FILE_MACHINE_IA64 = 0x200, //Intel Itanium architecture processor family, (64-bit)
        IMAGE_FILE_MACHINE_M68K = 0x268, //Motorola 68000 Series (32-bit) CISC microprocessors
        IMAGE_FILE_MACHINE_M32R = 0x9041, //Mitsubishi M32R little endian (32-bit) now owned by Renesas Electronics Corporation
        IMAGE_FILE_MACHINE_MIPS16 = 0x266, //MIPS16 (16-bit instruction codes, 8to32bit bus)- Microprocessor without Interlocked Pipeline Stages Architecture
        IMAGE_FILE_MACHINE_MIPSFPU = 0x366, //MIPS with FPU, MIPS Technologies (32-bit)
        IMAGE_FILE_MACHINE_MIPSFPU16 = 0x466, //MIPS16 with FPU (Floating Point Unit aka a math co-processesor)(16-bit instruction codes, 8to32bit bus)
        IMAGE_FILE_MACHINE_POWERPC = 0x1f0, //Power PC little endian, Performance Optimization With Enhanced RISC – Performance Computing (32-bit) one of the first
        IMAGE_FILE_MACHINE_POWERPCFP = 0x1f1, //Power PC with floating point support (FPU) (32-bit), designed by AIM Alliance (Apple, IBM, and Motorola)
        IMAGE_FILE_MACHINE_POWERPCBE = 0x01F2, //Power PC Big Endian (64?-bits)
        IMAGE_FILE_MACHINE_R3000 = 0x0162, //R3000 (32-bit) RISC processor
        IMAGE_FILE_MACHINE_R4000 = 0x166, //R4000 MIPS (64-bit) - claims to be first true 64-bit processor

        IMAGE_FILE_MACHINE_R10000 =
            0x0168, //R10000 MIPS IV is a (64-bit) architecture, but the R10000 did not implement the entire physical or virtual address to reduce cost. Instead, it has a 40-bit physical address and a 44-bit virtual address, thus it is capable of addressing 1 TB of physical memory and 16 TB of virtual memory. These comments by metadataconsulting.ca
        IMAGE_FILE_MACHINE_SH3 = 0x1a2, //Hitachi SH-3 (32-bit) - SuperH processor (SH3) core family
        IMAGE_FILE_MACHINE_SH3DSP = 0x1a3, //Hitachi SH-3 DSP (32-bit)
        IMAGE_FILE_MACHINE_SH4 = 0x1a6, //Hitachi SH-4 (32-bit)

        IMAGE_FILE_MACHINE_SH5 =
            0x1a8, //Hitachi SH-5, (64-bit) core with a 128-bit vector FPU (64 32-bit registers) and an integer unit which includes the SIMD support and 63 64-bit registers.
        IMAGE_FILE_MACHINE_TRICORE = 0x0520, //Infineon AUDO (Automotive unified processor) (32-bit) - Tricore architecture a unified RISC/MCU/DSP microcontroller core
        IMAGE_FILE_MACHINE_THUMB = 0x1c2, //ARM or Thumb (interworking), (32-bit) core instruction set, used in Nintendo Gameboy Advance
        IMAGE_FILE_MACHINE_WCEMIPSV2 = 0x169, //MIPS Windows Compact Edition v2
        IMAGE_FILE_MACHINE_ALPHA64 = 0x284 //DEC Alpha AXP (64-bit) or IMAGE_FILE_MACHINE_AXP64
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool IsWow64Process2(
        IntPtr process,
        out MachineType processMachine,
        out MachineType nativeMachine
    );
}

public static class SystemInfoEx
{
    private static WindowsVersionInfo? _windowsVersion;
    public static WindowsVersionInfo WindowsVersion => _windowsVersion ??= RtlGetVersion();

    private static ulong? _systemMemory;
    public static ulong SystemMemory => _systemMemory ??= GetSystemMemoryInBytes();

    private static Architecture? _systemArchitecture;
    public static Architecture SystemArchitecture => _systemArchitecture ??= GetArchitecture();

    public static ulong GetSystemMemoryInBytes()
    {
        SystemInfo.MEMORYSTATUSEX memStatus = new SystemInfo.MEMORYSTATUSEX();
        if (SystemInfo.GlobalMemoryStatusEx(memStatus))
            return memStatus.ullTotalPhys;
        else
            return 0;
    }

    public static Architecture GetArchitecture()
    {
        SystemInfo.MachineType processType = SystemInfo.MachineType.IMAGE_FILE_MACHINE_UNKNOWN;
        SystemInfo.MachineType hostType = SystemInfo.MachineType.IMAGE_FILE_MACHINE_UNKNOWN;
        SystemInfo.IsWow64Process2(SystemInfo.GetCurrentProcess().DangerousGetHandle(), out processType, out hostType);

        switch (hostType)
        {
            case SystemInfo.MachineType.IMAGE_FILE_MACHINE_ARMV7:
            case SystemInfo.MachineType.IMAGE_FILE_MACHINE_ARM:
                return Architecture.Arm;
            case SystemInfo.MachineType.IMAGE_FILE_MACHINE_ARM64:
                return Architecture.Arm64;
            case SystemInfo.MachineType.IMAGE_FILE_MACHINE_I386:
                return Architecture.X86;
            case SystemInfo.MachineType.IMAGE_FILE_MACHINE_AMD64:
            case SystemInfo.MachineType.IMAGE_FILE_MACHINE_I860:
                return Architecture.X64;
            default:
                return RuntimeInformation.OSArchitecture;
        }
    }

    public class WindowsVersionInfo
    {
        public int MajorVersion { get; set; }
        public int BuildNumber { get; set; }
        public int UpdateNumber { get; set; }
        public string Edition { get; set; } = null!;
    }

    public static WindowsVersionInfo RtlGetVersion()
    {
        var result = new WindowsVersionInfo();

        bool failed = false;
        try
        {
            result.BuildNumber =
                Int32.Parse((string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", (string)"-1")!);
            result.UpdateNumber = (int)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", (int)0)!;
            failed = result.BuildNumber == -1;
        }
        catch (Exception e)
        {
            failed = true;
        }

        try
        {
            SystemInfo.RTL_OSVERSIONINFOEX v = new SystemInfo.RTL_OSVERSIONINFOEX();
            v.dwOSVersionInfoSize = (uint)Marshal.SizeOf<SystemInfo.RTL_OSVERSIONINFOEX>();
            if (SystemInfo.RtlGetVersion(ref v) == 0)
            {
                result.BuildNumber = result.BuildNumber > (int)v.dwBuildNumber ? result.BuildNumber : (int)v.dwBuildNumber;
                result.MajorVersion = result.BuildNumber < 22000 ? 10 : 11;
                result.MajorVersion = result.MajorVersion > (int)v.dwMajorVersion ? result.MajorVersion : (int)v.dwMajorVersion;
                failed = false;
            }
            else
                result.MajorVersion = result.BuildNumber < 22000 ? 10 : 11;
        }
        catch (Exception e)
        {
            result.MajorVersion = result.BuildNumber < 22000 ? 10 : 11;
        }

        if (failed)
            throw new Exception("RtlGetVersion failed.");

        try
        {
            var edition = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Core")!;
            result.Edition = edition switch
            {
                "Core" => "Home",
                "Professional" => "Pro",
                "ProfessionalWorkstation" => "Pro Workstation",
                "Enterprise" => "Enterprise",
                "EnterpriseN" => "Enterprise",
                "EnterpriseG" => "Enterprise",
                "EnterpriseS" => "Enterprise S",
                "EnterpriseSN" => "Enterprise S",
                "Education" => "Education",
                "ProfessionalEducation" => "Pro Education",
                "ServerStandard" => "Server",
                "ServerDatacenter" => "Server",
                "ServerSolution" => "Server",
                "ServerStandardEval" => "Server Eval",
                "ServerDatacenterEval" => "Server Eval",
                "Cloud" => "Cloud",
                "CloudN" => "Cloud S",
                "CoreCountrySpecific" => "Home",
                "CoreSingleLanguage" => "Home",
                "IoTCore" => "IoT Core",
                "IoTEnterprise" => "IoT Enterprise",
                "IoTEnterpriseS" => "IoT Enterprise S",
                "IoTUAP" => "IoT Enterprise",
                "Team" => "Team",
                _ => String.IsNullOrWhiteSpace(edition) ? "Home" : edition
            };
        }
        catch (Exception e)
        {
            result.Edition = "Home";
        }

        return result;
    }
}