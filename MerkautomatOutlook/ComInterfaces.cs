using System;
using System.Runtime.InteropServices;

namespace MerkautomatOutlook;

// Declared here instead of referencing the Office interop assemblies; GUIDs, dispatch IDs and marshaling match
// Office's type libraries (Extensibility.dll, office.dll). The "custom" arrays are SAFEARRAY(VARIANT)*: without the
// explicit MarshalAs the CLR treats them as _Array interface pointers and crashes Outlook in OnConnection.

[ComImport, Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IDTExtensibility2
{
    [DispId(1)]
    void OnConnection([In, MarshalAs(UnmanagedType.IDispatch)] object application, [In] int connectMode,
        [In, MarshalAs(UnmanagedType.IDispatch)] object addInInst,
        [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

    [DispId(2)]
    void OnDisconnection([In] int removeMode,
        [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

    [DispId(3)]
    void OnAddInsUpdate([In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

    [DispId(4)]
    void OnStartupComplete([In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

    [DispId(5)]
    void OnBeginShutdown([In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);
}

[ComImport, Guid("000C0396-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IRibbonExtensibility
{
    [DispId(1)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetCustomUI([In, MarshalAs(UnmanagedType.BStr)] string ribbonId);
}

[ComImport, Guid("000C0395-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IRibbonControl
{
    [DispId(1)] string Id { [return: MarshalAs(UnmanagedType.BStr)] get; }
    [DispId(2)] object Context { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
    [DispId(3)] string Tag { [return: MarshalAs(UnmanagedType.BStr)] get; }
}
