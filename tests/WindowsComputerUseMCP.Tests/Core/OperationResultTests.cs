using WindowsComputerUseMCP.Core.Results;

namespace WindowsComputerUseMCP.Tests.Core;

public class OperationResultTests
{
    [Fact]
    public void Ok_SetsSuccessAndComputesDuration()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMilliseconds(-10);

        var result = OperationResult<string>.Ok("op_1", startedAt, data: "payload", message: "done");

        Assert.True(result.Success);
        Assert.Equal("op_1", result.OperationId);
        Assert.Equal("payload", result.Data);
        Assert.Equal("done", result.Message);
        Assert.Null(result.ErrorCode);
        Assert.True(result.DurationMs >= 0);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Fail_SetsErrorCodeAndSuccessFalse()
    {
        var startedAt = DateTimeOffset.UtcNow;

        var result = OperationResult<string>.Fail("op_2", startedAt, ErrorCodes.NotFound, "not found");

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal("not found", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Ok_PreservesProvidedWarnings()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var warnings = new List<string> { "画像サイズが大きいため縮小しました。" };

        var result = OperationResult<int>.Ok("op_3", startedAt, data: 42, warnings: warnings);

        Assert.Single(result.Warnings);
        Assert.Equal(warnings[0], result.Warnings[0]);
    }
}
