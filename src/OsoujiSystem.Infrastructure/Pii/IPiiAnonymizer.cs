namespace OsoujiSystem.Infrastructure.Pii;

public interface IPiiAnonymizer
{
    string HashIdentifier(string value);

    string MaskEmployeeNumber(string employeeNumber);
}
