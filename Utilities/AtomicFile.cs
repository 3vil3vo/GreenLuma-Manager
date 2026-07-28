using System.IO;
using System.Text;

namespace GreenLuma_Manager.Utilities;

public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content, Encoding.UTF8);
        File.Move(tempPath, path, true);
    }
}