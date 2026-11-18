namespace Elsa.NativeAot;

internal static class ScenarioAssert
{
    public static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
