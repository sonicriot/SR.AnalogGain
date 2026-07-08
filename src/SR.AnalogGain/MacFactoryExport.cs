#if MACOS
using System;
using System.Runtime.InteropServices;

namespace NPlug.Interop;

/// <summary>
/// VST3 bundle entry points required by many macOS hosts.
/// NPlug 0.5.0.380's NuGet package references a missing file for this, so we supply it in the plugin project.
/// </summary>
internal static partial class NPlugFactoryExport
{
    [UnmanagedCallersOnly(EntryPoint = "bundleEntry")]
    private static bool BundleEntry(IntPtr bundle)
    {
        return true;
    }

    [UnmanagedCallersOnly(EntryPoint = "bundleExit")]
    private static void BundleExit()
    {
    }
}
#endif
