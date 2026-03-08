using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Pii;

internal sealed class HmacPiiAnonymizer : IPiiAnonymizer
{
    private readonly byte[] _salt;
    private readonly bool _maskEmployeeNumber;

    public HmacPiiAnonymizer(IOptions<InfrastructureOptions> options)
    {
        var piiOptions = options.Value.Pii;
        _maskEmployeeNumber = piiOptions.MaskEmployeeNumber;

        var configuredSalt = Environment.GetEnvironmentVariable("INFRASTRUCTURE__PII__TENANT_SALT")
            ?? Environment.GetEnvironmentVariable("INFRASTRUCTURE__PII__TENANTSALT")
            ?? piiOptions.TenantSaltSecretName;

        _salt = Encoding.UTF8.GetBytes(configuredSalt);
    }

    public string HashIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        using var hmac = new HMACSHA256(_salt);
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = hmac.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string MaskEmployeeNumber(string employeeNumber)
    {
        if (!_maskEmployeeNumber)
        {
            return employeeNumber;
        }

        if (string.IsNullOrWhiteSpace(employeeNumber))
        {
            return string.Empty;
        }

        return employeeNumber.Length <= 2
            ? new string('*', employeeNumber.Length)
            : string.Concat(employeeNumber.AsSpan(0, 2), new string('*', employeeNumber.Length - 2));
    }
}
