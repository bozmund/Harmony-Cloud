using Harmony.Cloud.Api.Configuration;
using Harmony.Cloud.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Harmony.Cloud.UnitTests;

public sealed class AccountIdentityTests
{
    [Fact]
    public void Same_subject_is_stable_but_raw_subject_is_not_exposed()
    {
        var service = new AccountIdentity(new CloudOptions
        {
            IdentityHmacKey = "test-only-key-with-at-least-thirty-two-characters"
        });
        var first = Context("auth0|private-user");
        var second = Context("auth0|private-user");

        var firstId = service.Resolve(first);

        Assert.Equal(firstId, service.Resolve(second));
        Assert.DoesNotContain("private-user", firstId, StringComparison.Ordinal);
        Assert.Equal(64, firstId.Length);
    }

    private static DefaultHttpContext Context(string subject)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestEnvironment())
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers["X-Test-Subject"] = subject;
        return context;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
