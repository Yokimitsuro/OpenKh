using System;

namespace OpenKh.Tools.ModsManager.Extensions
{
    public static class StringExtensions
    {
        public static bool IsGitHubUrl(this string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
                
            // Verificar si el string contiene un formato de usuario/repositorio
            return url.Contains("/") && !url.Contains(" ") && !url.EndsWith(".zip") && !url.EndsWith(".lua");
        }
    }
}
