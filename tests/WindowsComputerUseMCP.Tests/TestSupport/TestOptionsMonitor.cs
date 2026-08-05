using Microsoft.Extensions.Options;
using WindowsComputerUseMCP.Core.Configuration;

namespace WindowsComputerUseMCP.Tests.TestSupport;

/// <summary>
/// テスト用の単純な <see cref="IOptionsMonitor{TOptions}"/> 実装。
/// 値の変更通知は行わず、常に構築時に渡した値を返す。
/// </summary>
public sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = currentValue;

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
