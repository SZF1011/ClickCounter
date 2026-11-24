using System;
using System.IO;
using System.Text;

namespace KeyRank
{
    internal static class FileExport
    {
        public static bool WriteTextFile(string targetPath, string content, string scratchDir, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(targetPath))
            {
                error = "路径为空";
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(targetPath, content, new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
