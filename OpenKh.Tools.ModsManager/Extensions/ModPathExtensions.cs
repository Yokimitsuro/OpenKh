using System;
using System.IO;
using OpenKh.Tools.ModsManager.Services;

namespace OpenKh.Tools.ModsManager.Extensions
{
    /// <summary>
    /// Extensiones para manejo de rutas de directorios de mods
    /// </summary>
    public static class ModPathExtensions
    {
        /// <summary>
        /// Crea todos los directorios necesarios para un mod, asegurando que la estructura completa exista
        /// </summary>
        /// <param name="repositoryName">Nombre del repositorio en formato usuario/repo</param>
        /// <param name="progressOutput">Acción para reportar progreso</param>
        /// <returns>La ruta completa al directorio del mod</returns>
        public static string CreateModDirectories(string repositoryName, Action<string> progressOutput = null)
        {
            string modPath;
            
            // Verificar si el repositorio tiene formato usuario/repo
            if (repositoryName.Contains("/"))
            {
                string[] parts = repositoryName.Split('/');
                if (parts.Length == 2)
                {
                    string userName = parts[0];
                    string repoName = parts[1];
                    
                    // Ruta base para los mods
                    string basePath = ConfigurationService.ModCollectionPath;
                    
                    // Asegurarnos de que exista el directorio base
                    if (!Directory.Exists(basePath))
                    {
                        progressOutput?.Invoke($"Creating base directory: {basePath}");
                        Directory.CreateDirectory(basePath);
                    }
                    
                    // Crear directorio del usuario
                    string userDir = Path.Combine(basePath, userName);
                    if (!Directory.Exists(userDir))
                    {
                        progressOutput?.Invoke($"Creating user directory: {userDir}");
                        Directory.CreateDirectory(userDir);
                    }
                    
                    // Ruta completa del mod
                    modPath = Path.Combine(userDir, repoName);
                    
                    // Crear directorio del mod si no existe
                    if (!Directory.Exists(modPath))
                    {
                        progressOutput?.Invoke($"Creating mod directory: {modPath}");
                        Directory.CreateDirectory(modPath);
                    }
                }
                else
                {
                    // Usar el comportamiento por defecto
                    modPath = ModsService.GetModPath(repositoryName);
                    if (!Directory.Exists(modPath))
                    {
                        progressOutput?.Invoke($"Creating mod directory: {modPath}");
                        Directory.CreateDirectory(modPath);
                    }
                }
            }
            else
            {
                // No es formato usuario/repo, usar comportamiento por defecto
                modPath = ModsService.GetModPath(repositoryName);
                if (!Directory.Exists(modPath))
                {
                    progressOutput?.Invoke($"Creating mod directory: {modPath}");
                    Directory.CreateDirectory(modPath);
                }
            }
            
            return modPath;
        }
    }
}
