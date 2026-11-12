using System.Runtime.CompilerServices;
using Merkautomat.Services;

namespace Merkautomat.Tests;

/// <summary>Every test runs with its own random data key, as the app does after it opened the real one.</summary>
internal static class TestDataKey
{
    [ModuleInitializer]
    internal static void Init() => DataProtection.Use(DataKey.Create());
}
