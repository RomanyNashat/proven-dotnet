using System.Net;
using HomeReadings.Application;

namespace HomeReadings.Tests.Application;

public sealed class AlertFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public void IsOnlyThisAlert_CareTeamRefusedTheAlert_True(HttpStatusCode status)
    {
        AlertFailure.IsOnlyThisAlert(new HttpRequestException("refused", null, status)).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void IsOnlyThisAlert_CareTeamUnavailable_False(HttpStatusCode status)
    {
        AlertFailure.IsOnlyThisAlert(new HttpRequestException("down", null, status)).Should().BeFalse();
    }

    [Fact]
    public void IsOnlyThisAlert_NoResponse_False()
    {
        AlertFailure.IsOnlyThisAlert(new HttpRequestException("no route")).Should().BeFalse();
        AlertFailure.IsOnlyThisAlert(new TimeoutException()).Should().BeFalse();
    }
}

public sealed class AlertFailureDescribeTests
{
    [Fact]
    public void Describe_HttpFailure_GivesTypeAndStatusOnly()
    {
        var ex = new HttpRequestException("body echoing 1012345678", null, System.Net.HttpStatusCode.ServiceUnavailable);

        AlertFailure.Describe(ex).Should().Be("HttpRequestException 503");
    }

    [Fact]
    public void Describe_OtherFailure_GivesTypeOnly()
    {
        AlertFailure.Describe(new TimeoutException("1012345678")).Should().Be("TimeoutException");
    }
}
