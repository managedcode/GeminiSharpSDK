using TUnit.Core.Interfaces;

namespace ManagedCode.GeminiSharpSDK.Tests.TestSupport;

internal sealed class GeminiAuthParallelLimit : IParallelLimit
{
    public int Limit => 1;
}
