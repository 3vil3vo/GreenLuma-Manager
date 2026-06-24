namespace GreenLuma_Manager.Models;

public class AppListProgressReport
{
    public required string Status { get; set; }
    public int Current { get; set; }
    public int Total { get; set; }
    public double Percentage => Total > 0 ? (double)Current / Total * 100.0 : 0.0;
    public bool IsIndeterminate { get; set; }
}