using AwesomeAssertions;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Pii;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class PiiAnonymizerTests
{
    [Fact]
    public void HashIdentifier_ShouldBeDeterministic()
    {
        Environment.SetEnvironmentVariable("INFRASTRUCTURE__PII__TENANT_SALT", "test-salt");
        var anonymizer = Create(maskEmployeeNumber: true);

        var first = anonymizer.HashIdentifier("user-001");
        var second = anonymizer.HashIdentifier("user-001");

        first.Should().Be(second);
        first.Should().NotBe("user-001");
    }

    [Fact]
    public void MaskEmployeeNumber_ShouldMaskAfterFirstTwoChars_WhenEnabled()
    {
        Environment.SetEnvironmentVariable("INFRASTRUCTURE__PII__TENANT_SALT", "test-salt");
        var anonymizer = Create(maskEmployeeNumber: true);

        anonymizer.MaskEmployeeNumber("123456").Should().Be("12****");
    }

    [Fact]
    public void MaskEmployeeNumber_ShouldReturnOriginal_WhenDisabled()
    {
        Environment.SetEnvironmentVariable("INFRASTRUCTURE__PII__TENANT_SALT", "test-salt");
        var anonymizer = Create(maskEmployeeNumber: false);

        anonymizer.MaskEmployeeNumber("123456").Should().Be("123456");
    }

    private static HmacPiiAnonymizer Create(bool maskEmployeeNumber)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new InfrastructureOptions
        {
            Pii = new PiiOptions
            {
                TenantSaltSecretName = "fallback-salt",
                MaskEmployeeNumber = maskEmployeeNumber
            }
        });

        return new HmacPiiAnonymizer(options);
    }
}
