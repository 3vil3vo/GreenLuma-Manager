namespace GreenLuma_Manager.Models;

public class GreenLumaDeploymentResult
{
    public bool Success { get; set; }
    public List<string> DeployedFiles { get; set; } = [];
    public string? ErrorMessage { get; set; }
}