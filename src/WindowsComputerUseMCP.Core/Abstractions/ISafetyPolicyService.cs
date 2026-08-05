using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Core.Abstractions;

/// <summary>危険操作の検出、許可/拒否判定を行う抽象。実装は WindowsComputerUseMCP.Safety が提供する。</summary>
public interface ISafetyPolicyService
{
    SafetyDecision Evaluate(SafetyCheckRequest request);

    /// <summary>連続操作回数・操作間隔の制約を満たしているかを判定し、満たしていれば内部カウンターを更新する。</summary>
    SafetyDecision CheckRateLimit(string toolName);
}
