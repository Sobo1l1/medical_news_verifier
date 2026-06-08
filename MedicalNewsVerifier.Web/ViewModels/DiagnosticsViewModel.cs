namespace MedicalNewsVerifier.Web.ViewModels;

public class DiagnosticsViewModel
{
    public bool DatabaseOk { get; set; }
    public string? DatabaseError { get; set; }

    public bool OllamaEnabled { get; set; }
    public bool OllamaReachable { get; set; }
    public string? OllamaModel { get; set; }
    public string? OllamaError { get; set; }

    public bool PythonAvailable { get; set; }
    public string? PythonVersion { get; set; }
    public string? PythonError { get; set; }
}
