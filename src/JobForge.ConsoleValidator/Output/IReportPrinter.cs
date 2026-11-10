using JobForge.Core.Dtos;
using JobForge.Core.Models;

namespace JobForge.ConsoleValidator.Output;

public interface IReportPrinter
{
    void Print(ValidationReportDto report);
}
