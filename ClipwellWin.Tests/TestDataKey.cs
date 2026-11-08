using System.Runtime.CompilerServices;
using ClipwellWin.Services;

namespace ClipwellWin.Tests;

/// <summary>Every test runs with its own random data key, as the app does after it opened the real one.</summary>
internal static class TestDataKey
{
    [ModuleInitializer]
    internal static void Init() => DataProtection.Use(DataKey.Create());
}
