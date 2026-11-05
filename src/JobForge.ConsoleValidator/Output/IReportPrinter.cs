namespace JobForge.ConsoleValidator.Output;

public interface IReportPrinter
{
    void Print(ValidationReportDto report);
}
