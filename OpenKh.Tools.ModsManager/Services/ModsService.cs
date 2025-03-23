using LibGit2Sharp;
using OpenKh.Common;
using OpenKh.Patcher;
using OpenKh.Tools.ModsManager.Exceptions;
using OpenKh.Tools.ModsManager.Extensions;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using static OpenKh.Tools.ModsManager.Helpers;

namespace OpenKh.Tools.ModsManager.Services
{
    public static class ModsService
    {
        private const string ModMetadata = "mod.yml";
        private const string DefaultGitBranch = "main";

        private static string[] _gameList = new string[]
        {
            "OpenKH",
            "PCSX2-EX",
            "PC"
        };

        private static string[] _langList = new string[]
        {
            "Default",
            "Japanese",
            "English [US]",
            "English [UK]",
            "Italian",
            "Spanish",
            "German",
            "French",
            "Final Mix"
        };

        public static IEnumerable<string> Mods
        {
            get
            {
                var allMods = UnorderedMods.ToList();
                var enabledMods = EnabledMods.ToHashSet();
                var disabledMods = new List<string>(allMods.Count);
                foreach (var mod in allMods)
                {
                    if (!enabledMods.Contains(mod))
                        disabledMods.Add(mod);
                }

                return enabledMods.Concat(disabledMods);
            }
        }

        public static IEnumerable<string> UnorderedMods
        {
            get
            {
                var modsPath = ConfigurationService.ModCollectionPath;
                foreach (var dir in Directory.GetDirectories(modsPath))
                {
                    var authorName = Path.GetFileName(dir);
                    foreach (var subdir in Directory.GetDirectories(dir))
                    {
                        var repoName = Path.GetFileName(subdir);
                        if (File.Exists(Path.Combine(subdir, ModMetadata)))
                            yield return $"{authorName}/{repoName}";
                    }

                    if (File.Exists(Path.Combine(dir, ModMetadata)))
                        yield return authorName;
                }
            }
        }

        public static IEnumerable<string> EnabledMods
        {
            get
            {
                var mods = UnorderedMods.ToList();
                foreach (var mod in ConfigurationService.EnabledMods)
                {
                    if (mods.Contains(mod))
                        yield return mod;
                }
            }
        }

        public static bool IsModBlocked(string repositoryName)
            {
            if (ConfigurationService.BlacklistedMods != null)
                return ConfigurationService.BlacklistedMods.Any(x => x.Equals(repositoryName, StringComparison.InvariantCultureIgnoreCase));
            return false;
            }

        public static bool IsUserBlocked(string repositoryName) =>
            IsModBlocked(Path.GetDirectoryName(repositoryName));

        public static async Task InstallMod(
            string name,
            bool zip = false,
            bool lua = false,
            Action<string> progressOutput = null,
            Action<float> progressNumber = null)
        {
            if (zip)
            {
                await Task.Run(() => InstallModFromZip(name, progressOutput, progressNumber));
                return;
            }
            else if (lua)
            {
                await Task.Run(() => InstallModFromLua(name));
                return;
            }
            else
            {
                try 
                {
                    // Primero instalar el mod principal
                    progressOutput?.Invoke($"Installing main mod: {name}");
                    await InstallModFromGithub(name, progressOutput, progressNumber);
                    
                    // Verificar si el mod tiene un mod.yml válido
                    string modName = name;
                    string modPath = "";
                    
                    if (name.Contains("/"))
                    {
                        string[] parts = name.Split('/');
                        if (parts.Length == 2)
                        {
                            string userName = parts[0];
                            string repoName = parts[1];
                            modPath = Path.Combine(ConfigurationService.ModCollectionPath, userName, repoName);
                        }
                        else
                        {
                            modPath = GetModPath(name);
                        }
                    }
                    else
                    {
                        modPath = GetModPath(name);
                    }
                    
                    if (!Directory.Exists(modPath))
                    {
                        progressOutput?.Invoke($"Error: Mod directory not found after installation: {modPath}");
                        return;
                    }
                    
                    string modYmlPath = Path.Combine(modPath, "mod.yml");
                    
                    // Intentar obtener el metadata para las dependencias
                    Metadata metadata = null;
                    if (File.Exists(modYmlPath))
                    {
                        try
                        {
                            progressOutput?.Invoke($"Loading mod.yml for dependency analysis...");
                            metadata = Metadata.Read(modYmlPath);
                        }
                        catch (Exception ex)
                        {
                            progressOutput?.Invoke($"Warning: Error reading mod.yml file: {ex.Message}");
                            File.Delete(modYmlPath); // Eliminar el archivo corrupto
                            metadata = null;
                        }
                    }
                    
                    // Si el metadata es nulo o no hay mod.yml, crear uno básico
                    if (metadata == null || !File.Exists(modYmlPath))
                    {
                        progressOutput?.Invoke($"Warning: Valid mod.yml not found. Creating a basic one...");
                        
                        var metadata1 = new Metadata
                        {
                            Title = name.Contains("/") ? name.Split('/')[1] : name,
                            Description = "Auto-generated mod metadata",
                            Game = "kh2", // Valor predeterminado
                            Author = name.Contains("/") ? name.Split('/')[0] : "Unknown",
                            Version = "1.0"
                        };
                        
                        // Guardar el archivo generado
                        using (var stream = File.Create(modYmlPath))
                        {
                            Metadata.Write(stream, metadata1);
                        }
                        progressOutput?.Invoke($"Created basic mod.yml file at: {modYmlPath}");
                        
                        // Recargar metadata
                        try {
                            metadata = Metadata.Read(modYmlPath);
                        }
                        catch (Exception ex) {
                            progressOutput?.Invoke($"Error creating mod.yml: {ex.Message}");
                            return;
                        }
                    }
                    
                    // Ahora instalar dependencias si existen
                    if (metadata?.Dependencies != null && metadata.Dependencies.Count > 0)
                    {
                        progressOutput?.Invoke($"Installing {metadata.Dependencies.Count} dependencies...");
                        await InstallDependencies(metadata.Dependencies, progressOutput, progressNumber);
                    }
                    else
                    {
                        progressOutput?.Invoke("No dependencies to install.");
                    }
                    
                    // Final de la instalación
                    progressOutput?.Invoke($"Mod {name} installed successfully!");
                    progressNumber?.Invoke(1.0f);
                    return;
                }
                catch (Exception ex)
                {
                    progressOutput?.Invoke($"Error during installation: {ex.Message}");
                    throw;
                }
            }
        }
        
        // Método para instalar dependencias
        private static async Task InstallDependencies(List<Metadata.Dependency> dependencies, Action<string> progressOutput = null, Action<float> progressNumber = null)
        {
            if (dependencies == null || dependencies.Count == 0)
                return;

            // Obtener la lista de mods instalados
            var installedMods = GetAllMods().ToList();
            
            foreach (var dependency in dependencies)
            {
                try
                {
                    // Comprobar si la dependencia ya está instalada
                    bool isAlreadyInstalled = installedMods.Any(mod => 
                        mod.Metadata?.Title?.Equals(dependency.Name, StringComparison.OrdinalIgnoreCase) == true ||
                        mod.Name?.Equals(dependency.Name, StringComparison.OrdinalIgnoreCase) == true);

                    if (isAlreadyInstalled)
                    {
                        progressOutput?.Invoke($"Dependency {dependency.Name} is already installed. Skipping.");
                        continue;
                    }

                    progressOutput?.Invoke($"Installing dependency: {dependency.Name}");
                    
                    // Verificar si el nombre de la dependencia tiene el formato de repositorio de GitHub (usuario/repo)
                    string repoName = dependency.Name;
                    if (!repoName.Contains("/"))
                    {
                        progressOutput?.Invoke($"Warning: Dependency name '{repoName}' does not appear to be a valid GitHub repository name (should be in format 'username/repository').");
                        continue;
                    }
                    
                    try 
                    {
                        // Crear el directorio del mod primero
                        var path = repoName.Split('/');
                        if (path.Length == 2)
                        {
                            var modPath = Path.Combine(ConfigurationService.ModCollectionPath, path[0], path[1]);
                            
                            // Verificar si ya existe y eliminarlo
                            if (Directory.Exists(modPath))
                            {
                                progressOutput?.Invoke($"Dependency {dependency.Name} directory already exists. Removing it...");
                                
                                try
                                {
                                    // Quitar atributos de solo lectura
                                    foreach (var filePath in Directory.GetFiles(modPath, "*", SearchOption.AllDirectories))
                                    {
                                        var attributes = File.GetAttributes(filePath);
                                        if (attributes.HasFlag(FileAttributes.ReadOnly))
                                            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                                    }
                                    
                                    // Eliminar directorio
                                    Directory.Delete(modPath, true);
                                    
                                    // Verificar que se haya eliminado
                                    if (Directory.Exists(modPath))
                                    {
                                        progressOutput?.Invoke($"Warning: Could not remove existing dependency directory");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    progressOutput?.Invoke($"Warning: Error removing existing dependency: {ex.Message}");
                                }
                            }
                            
                            // Crear el directorio si no existe o fue eliminado
                            if (!Directory.Exists(modPath))
                            {
                                progressOutput?.Invoke($"Creating dependency directory: {modPath}");
                                Directory.CreateDirectory(modPath);
                            }
                            
                            // Configurar opciones de clon
                            var options = new CloneOptions
                            {
                                RecurseSubmodules = true
                            };
                            
                            await Task.Run(() => {
                                try
                                {
                                    Repository.Clone($"https://github.com/{repoName}", modPath, options);
                                    
                                    // Verificar si existe el archivo mod.yml después de clonar
                                    string modYmlPath = Path.Combine(modPath, "mod.yml");
                                    
                                    if (!File.Exists(modYmlPath))
                                    {
                                        progressOutput?.Invoke($"Warning: mod.yml file not found in dependency. Generating a basic mod.yml file.");
                                        
                                        // Crear un archivo mod.yml básico
                                        var metadata = new Metadata
                                        {
                                            Title = repoName.Split('/')[1],
                                            Description = "Auto-generated dependency metadata",
                                            Game = "kh2", // Valor predeterminado
                                            Author = repoName.Split('/')[0],
                                            Version = "1.0"
                                        };
                                        
                                        // Guardar el archivo generado
                                        using (var stream = File.Create(modYmlPath))
                                        {
                                            Metadata.Write(stream, metadata);
                                        }
                                        progressOutput?.Invoke($"Created basic mod.yml file for dependency at: {modYmlPath}");
                                    }
                                    
                                    progressOutput?.Invoke($"Dependency {dependency.Name} installed successfully");
                                }
                                catch (Exception ex)
                                {
                                    progressOutput?.Invoke($"Error cloning dependency: {ex.Message}");
                                    
                                    // Intentar limpiar el directorio si falló la clonación
                                    try
                                    {
                                        if (Directory.Exists(modPath))
                                        {
                                            Directory.Delete(modPath, true);
                                        }
                                    }
                                    catch { /* Ignorar errores de limpieza */ }
                                    
                                    throw;
                                }
                            });
                        }
                        else
                        {
                            progressOutput?.Invoke($"Warning: Invalid repository format for {repoName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Manejo específico para errores
                        progressOutput?.Invoke($"Warning: Could not install dependency {dependency.Name}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    progressOutput?.Invoke($"Warning: Error processing dependency {dependency.Name}: {ex.Message}");
                }
            }
        }

        public static void InstallModFromZip(
            string fileName,
            Action<string> progressOutput = null,
            Action<float> progressNumber = null)
        {
            var modName = Path.GetFileNameWithoutExtension(fileName);
            progressOutput?.Invoke($"Opening '{modName}' zip archive...");

            using var zipFile = ZipFile.OpenRead(fileName);

            var isModPatch = fileName.ToLower().Contains(".kh2pcpatch") || fileName.ToLower().Contains(".kh1pcpatch") || fileName.ToLower().Contains(".compcpatch") || fileName.ToLower().Contains(".bbspcpatch") || fileName.ToLower().Contains(".dddpcpatch") ? true : false;
            var isValidMod = zipFile.GetEntry(ModMetadata) != null || isModPatch;

            if (!isValidMod)
                throw new ModNotValidException(modName);

            var modPath = GetModPath(modName);
            if (Directory.Exists(modPath))
            {
                var errorMessage = MessageBox.Show($"A mod with the name '{modName}' already exists. Do you want to overwrite the mod install.", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);

                switch (errorMessage)
                {
                    case MessageBoxResult.Yes:
                        MainViewModel.overwriteMod = true;
                        Directory.Delete(modPath, true);
                        break;
                    case MessageBoxResult.No:
                        throw new ModAlreadyExistsExceptions(modName);
                }
            }

            Directory.CreateDirectory(modPath);

            var entryExtractCount = 0;
            var entryCount = zipFile.Entries.Count;

            foreach (var entry in zipFile.Entries.Where(x => (x.ExternalAttributes & 0x10) != 0x10))
            {
                var _str = entry.FullName;
                var _strSplitter = _str.IndexOf('/') > -1 ? "/" : "\\";

                var _splitStr = _str.Split(_strSplitter);
                var _package = _splitStr[0];

                if (isModPatch)
                {
                    if (_str.Contains("original"))
                        _str = String.Join("\\", _splitStr.Skip(2));

                    else
                        _str = String.Join("\\", _splitStr.Skip(1));
                }

                progressOutput?.Invoke($"Extracting '{_str}'...");
                progressNumber?.Invoke((float)entryExtractCount / entryCount);
                var dstFileName = Path.Combine(modPath, _str);
                var dstFilePath = Path.GetDirectoryName(dstFileName);
                if (!Directory.Exists(dstFilePath))
                    Directory.CreateDirectory(dstFilePath);
                File.Create(dstFileName)
                    .Using(outStream =>
                    {
                        using var zipStream = entry.Open();
                        zipStream.CopyTo(outStream);
                    });

                entryExtractCount++;
            }

            if (isModPatch)
            {
                var metadata = new Metadata();
                if (fileName.ToLower().Contains(".kh2pcpatch"))
                {
                    metadata.Title = modName + " (KH2PCPATCH)";
                    metadata.Game = "kh2";
                    metadata.Description = "This is an automatically generated metadata for this KH2PCPATCH Modification.";
                }
                else if (fileName.ToLower().Contains(".kh1pcpatch"))
                {
                    metadata.Title = modName + " (KH1PCPATCH)";
                    metadata.Game = "kh1";
                    metadata.Description = "This is an automatically generated metadata for this KH1PCPATCH Modification.";
                }
                else if (fileName.ToLower().Contains(".compcpatch"))
                {
                    metadata.Title = modName + " (COMPCPATCH)";
                    metadata.Game = "Recom";
                    metadata.Description = "This is an automatically generated metadata for this COMPCPATCH Modification.";
                }
                else if (fileName.ToLower().Contains(".bbspcpatch"))
                {
                    metadata.Title = modName + " (BBSPCPATCH)";
                    metadata.Game = "bbs";
                    metadata.Description = "This is an automatically generated metadata for this BBSPCPATCH Modification.";
                }
                else if (fileName.ToLower().Contains(".dddpcpatch"))
                {
                    metadata.Title = modName + " (DDDPCPATCH)";
                    metadata.Game = "kh3d";
                    metadata.Description = "This is an automatically generated metadata for this DDDPCPATCH Modification.";
                }
                metadata.OriginalAuthor = "Unknown";
                metadata.Assets = new List<AssetFile>();

                foreach (var entry in zipFile.Entries.Where(x => (x.ExternalAttributes & 0x10) != 0x10))
                {
                    var _str = entry.FullName;
                    var _strSplitter = _str.IndexOf('/') > -1 ? "/" : "\\";

                    var _splitStr = _str.Split(_strSplitter);
                    var _package = _splitStr[0];

                    if (_str.Contains("original"))
                        _str = String.Join("\\", _splitStr.Skip(2));

                    else
                        _str = String.Join("\\", _splitStr.Skip(1));

                    var _assetFile = new AssetFile();
                    var _assetSource = new AssetFile();

                    _assetSource.Name = _str;

                    _assetFile.Method = "copy";
                    _assetFile.Name = _str;
                    _assetFile.Package = _package;
                    _assetFile.Source = new List<AssetFile>() { _assetSource };
                    _assetFile.Platform = "pc";

                    metadata.Assets.Add(_assetFile);
                }

                var _yamlPath = Path.Combine(modPath + "/mod.yml");

                using (var stream = File.Create(_yamlPath))
                {
                    Metadata.Write(stream, metadata);
                }
            }
        }

        public static async Task InstallModFromGithub(
            string repositoryName,
            Action<string> progressOutput = null,
            Action<float> progressNumber = null)
        {
            try
            {
                var branchName = DefaultGitBranch;
                progressOutput?.Invoke($"Fetching file {ModMetadata} from {branchName}");
                var isValidMod = await RepositoryService.IsFileExists(repositoryName, branchName, ModMetadata);
                if (!isValidMod)
                {
                    progressOutput?.Invoke($"{ModMetadata} not found, fetching default branch name");
                    branchName = await RepositoryService.GetMainBranchFromRepository(repositoryName);
                    if (branchName == null)
                        throw new RepositoryNotFoundException(repositoryName);

                    progressOutput?.Invoke($"Fetching file {ModMetadata} from {branchName}");
                    isValidMod = await RepositoryService.IsFileExists(repositoryName, branchName, ModMetadata);
                }

                if (!isValidMod)
                    throw new ModNotValidException(repositoryName);

                // Determinar la ruta completa del mod y crear todos los directorios necesarios
                string modPath;
                
                try {
                    // Para repositorios con formato usuario/repo
                    if (repositoryName.Contains("/"))
                    {
                        string[] parts = repositoryName.Split('/');
                        if (parts.Length == 2)
                        {
                            string userName = parts[0];
                            string repoName = parts[1];
                            
                            // Obtener el directorio base para los mods
                            string baseDir = ConfigurationService.ModCollectionPath;
                            progressOutput?.Invoke($"Base directory: {baseDir}");
                            
                            // Asegurarse de que existe el directorio base
                            if (!Directory.Exists(baseDir))
                            {
                                progressOutput?.Invoke($"Creating base directory: {baseDir}");
                                Directory.CreateDirectory(baseDir);
                            }
                            
                            // Crear el directorio del usuario si no existe
                            string userDir = Path.Combine(baseDir, userName);
                            if (!Directory.Exists(userDir))
                            {
                                progressOutput?.Invoke($"Creating user directory: {userDir}");
                                Directory.CreateDirectory(userDir);
                            }
                            
                            // Ahora sí, la ruta completa del mod
                            modPath = Path.Combine(userDir, repoName);
                        }
                        else
                        {
                            // Formato no esperado, usar el comportamiento por defecto
                            modPath = GetModPath(repositoryName);
                        }
                    }
                    else
                    {
                        // No es un formato usuario/repo, usar el comportamiento por defecto
                        modPath = GetModPath(repositoryName);
                    }
                    
                    progressOutput?.Invoke($"Mod path: {modPath}");
                    
                    // Verificar si el mod ya existe
                    if (Directory.Exists(modPath))
                    {
                        var errorMessage = MessageBox.Show($"A mod with the name '{repositoryName}' already exists. Do you want to overwrite the mod install.", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);

                        switch (errorMessage)
                        {
                            case MessageBoxResult.Yes:
                                try
                                {
                                    progressOutput?.Invoke($"Removing existing mod directory: {modPath}");
                                    MainViewModel.overwriteMod = true;
                                    
                                    // Quitar atributos de solo lectura de todos los archivos
                                    foreach (var filePath in Directory.GetFiles(modPath, "*", SearchOption.AllDirectories))
                                    {
                                        var attributes = File.GetAttributes(filePath);
                                        if (attributes.HasFlag(FileAttributes.ReadOnly))
                                            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                                    }

                                    // Eliminar directorio
                                    Directory.Delete(modPath, true);
                                    
                                    // Verificar que se haya eliminado correctamente
                                    int retries = 0;
                                    while (Directory.Exists(modPath) && retries < 5)
                                    {
                                        progressOutput?.Invoke($"Waiting for directory to be removed... Attempt {retries+1}");
                                        System.Threading.Thread.Sleep(500); // Esperar un poco
                                        retries++;
                                        
                                        if (Directory.Exists(modPath))
                                        {
                                            try 
                                            {
                                                // Intento adicional de eliminar
                                                Directory.Delete(modPath, true);
                                            }
                                            catch (Exception ex)
                                            {
                                                progressOutput?.Invoke($"Warning: Failed to delete directory on retry: {ex.Message}");
                                            }
                                        }
                                    }
                                    
                                    if (Directory.Exists(modPath))
                                    {
                                        progressOutput?.Invoke($"Error: Could not remove existing mod directory");
                                        throw new IOException($"Could not remove directory: {modPath}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    progressOutput?.Invoke($"Error removing existing mod: {ex.Message}");
                                    throw;
                                }
                                break;
                            case MessageBoxResult.No:
                                throw new ModAlreadyExistsExceptions(repositoryName);
                        }
                    }
                    
                    // Crear el directorio del mod si no existe o fue eliminado
                    if (!Directory.Exists(modPath))
                    {
                        progressOutput?.Invoke($"Creating mod directory: {modPath}");
                        Directory.CreateDirectory(modPath);
                    }
                }
                catch (Exception ex)
                {
                    progressOutput?.Invoke($"Error creating directory structure: {ex.Message}");
                    throw;
                }

                progressOutput?.Invoke($"Mod found, initializing cloning process");
                await Task.Run(() =>
                {
                    var options = new CloneOptions
                    {
                        RecurseSubmodules = true,
                    };
                    options.FetchOptions.OnProgress = (serverProgressOutput) =>
                    {
                        progressOutput?.Invoke(serverProgressOutput);
                        return true;
                    };
                    options.FetchOptions.OnTransferProgress = (progress) =>
                    {
                        var nProgress = ((float)progress.ReceivedObjects / (float)progress.TotalObjects);
                        progressNumber?.Invoke(nProgress);

                        progressOutput?.Invoke("Received Bytes: " + (progress.ReceivedBytes / 1048576) + " MB");
                        return true;
                    };
                    
                    try
                    {
                        Repository.Clone($"https://github.com/{repositoryName}", modPath, options);
                        
                        // Verificar si existe el archivo mod.yml después de clonar
                        string modYmlPath = Path.Combine(modPath, "mod.yml");
                        
                        if (!File.Exists(modYmlPath))
                        {
                            progressOutput?.Invoke($"Warning: mod.yml file not found in repository. Generating a basic mod.yml file.");
                            
                            // Crear un archivo mod.yml básico
                            var metadata = new Metadata
                            {
                                Title = repositoryName.Contains("/") ? repositoryName.Split('/')[1] : repositoryName,
                                Description = "Auto-generated mod metadata",
                                Game = "kh2", // Valor predeterminado
                                Author = repositoryName.Contains("/") ? repositoryName.Split('/')[0] : "Unknown",
                                Version = "1.0"
                            };
                            
                            // Guardar el archivo generado
                            using (var stream = File.Create(modYmlPath))
                            {
                                Metadata.Write(stream, metadata);
                            }
                            progressOutput?.Invoke($"Created basic mod.yml file at: {modYmlPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        progressOutput?.Invoke($"Error cloning repository {repositoryName}: {ex.Message}");
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                progressOutput?.Invoke($"Error in InstallModFromGithub: {ex.Message}");
                throw;
            }
        }

        public static void InstallModFromLua(string fileName)
        {
            string modName = Path.GetFileNameWithoutExtension(fileName);
            string modAuthor = null;
            string modDescription = null;

            if (!fileName.Contains(".lua"))
                throw new ModNotValidException(modName);

            string modPath = GetModPath(modName);
            if (Directory.Exists(modPath))
            {
                var errorMessage = MessageBox.Show($"A mod with the name '{modName}' already exists. Do you want to overwrite the mod install.", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);

                switch (errorMessage)
                {
                    case MessageBoxResult.Yes:
                        MainViewModel.overwriteMod = true;
                        Directory.Delete(modPath, true);
                        break;
                    case MessageBoxResult.No:
                        throw new ModAlreadyExistsExceptions(modName);
                }
            }
            Directory.CreateDirectory(modPath);
            File.Copy(fileName, Path.Combine(modPath, Path.GetFileName(fileName)));

            StreamReader r = new StreamReader(Path.Combine(modPath, Path.GetFileName(fileName)));
            while (!r.EndOfStream)
            {
                string line = r.ReadLine();

                if (line.Contains("LUAGUI"))
                {
                    string _lineGib = "";
                    string _lineLead = "";

                    _lineGib = line.Substring(line.IndexOf("=") + 1).Replace("\"", "").Replace("\'", "").Trim();
                    _lineLead = string.Concat(line.Take(11));

                    switch (_lineLead)
                    {
                        case "LUAGUI_NAME":
                            modName = "\"" + _lineGib + "\"";
                            break;
                        case "LUAGUI_AUTH":
                            modAuthor = "\"" + _lineGib + "\"";
                            break;
                        case "LUAGUI_DESC":
                            modDescription = "\"" + _lineGib + "\"";
                            break;
                    }

                    if (modName != null && modAuthor != null && modDescription != null)
                        break;
                }
            }
            r.Close();
            modDescription ??= "This is an automatically generated metadata for a Lua Zip Mod";
            string yaml = $"title: {modName}\r\n{(modAuthor != null ? $"author: {modAuthor}\r\n" : "")}description: {modDescription}\r\n" +
                $"assets:\r\n- name: scripts/{Path.GetFileName(fileName)}\r\n  method: copy\r\n  source:\r\n  - name: {Path.GetFileName(fileName)}";
            string _yamlPath = Path.Combine(modPath + "/mod.yml");

            using (var stream = File.Create(_yamlPath))
            {
                using var writer = new StreamWriter(stream);
                writer.Write(yaml);
            }
        }

        public static string GetModPath(string author, string repo) => 
            Path.Combine(ConfigurationService.ModCollectionPath, author, repo);

        public static string GetModPath(string repositoryName)
        {
            // Para repositorios con formato usuario/repo
            if (repositoryName.Contains("/"))
            {
                string[] parts = repositoryName.Split('/');
                if (parts.Length == 2)
                {
                    string userName = parts[0];
                    string repoName = parts[1];
                    
                    // Obtener el directorio base para los mods
                    string basePath = ConfigurationService.ModCollectionPath;
                    
                    // Crear el directorio base si no existe
                    if (!Directory.Exists(basePath))
                    {
                        Directory.CreateDirectory(basePath);
                    }
                    
                    // Crear el directorio del usuario si no existe
                    string userDir = Path.Combine(basePath, userName);
                    if (!Directory.Exists(userDir))
                    {
                        Directory.CreateDirectory(userDir);
                    }
                    
                    // Devolver la ruta completa (y crear el directorio del repo si no existe)
                    string repoPath = Path.Combine(userDir, repoName);
                    if (!Directory.Exists(repoPath))
                    {
                        Directory.CreateDirectory(repoPath);
                    }
                    
                    return repoPath;
                }
            }
            
            // No es formato usuario/repo, usar formato estándar
            string defaultPath = Path.Combine(ConfigurationService.ModCollectionPath, repositoryName);
            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }
            
            return defaultPath;
        }

        public static IEnumerable<ModModel> GetMods(IEnumerable<string> modNames)
        {
            var enabledMods = ConfigurationService.EnabledMods;
            foreach (var modName in modNames)
            {
                var modPath = GetModPath(modName);
                yield return new ModModel
                {
                    Name = modName,
                    Path = modPath,
                    IconImageSource = Path.Combine(modPath, "icon.png"),
                    PreviewImageSource = Path.Combine(modPath, "preview.png"),
                    Metadata = File.OpenRead(Path.Combine(modPath, ModMetadata)).Using(Metadata.Read),
                    IsEnabled = enabledMods.Contains(modName)
                };
            }
        }

        public static IEnumerable<ModModel> GetAllMods()
        {
            var enabledMods = ConfigurationService.EnabledMods;
            var baseModsPath = ConfigurationService.ModCollectionPath;
            
            // Depuración
            System.Diagnostics.Debug.WriteLine($"Base mods path: {baseModsPath}");
            
            // Lista para almacenar todos los directorios que podrían contener mods
            List<string> allPotentialModPaths = new List<string>();
            
            // 1. Buscar mods directamente en el directorio base (estructura antigua)
            try
            {
                if (Directory.Exists(baseModsPath))
                {
                    allPotentialModPaths.AddRange(Directory.EnumerateDirectories(baseModsPath));
                    
                    // 2. Buscar mods en estructura usuario/repo (directorios anidados)
                    foreach (var userDir in Directory.EnumerateDirectories(baseModsPath))
                    {
                        // Por cada directorio de usuario, buscar sus repositorios
                        allPotentialModPaths.AddRange(Directory.EnumerateDirectories(userDir));
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"¡Directorio base de mods no existe!: {baseModsPath}");
                    Directory.CreateDirectory(baseModsPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error accediendo a directorios de mods: {ex.Message}");
            }
            
            // Depuración
            System.Diagnostics.Debug.WriteLine($"Número de directorios potenciales de mods: {allPotentialModPaths.Count}");
            foreach (var dir in allPotentialModPaths)
            {
                System.Diagnostics.Debug.WriteLine($"Directorio potencial: {dir}");
            }
            
            // Procesar todos los directorios potenciales de mods
            foreach (var modPath in allPotentialModPaths)
            {
                string modYmlPath = Path.Combine(modPath, ModMetadata);
                
                // Depuración
                System.Diagnostics.Debug.WriteLine($"Comprobando mod.yml en: {modYmlPath}");
                
                // Solo consideramos directorios que tengan un archivo mod.yml
                if (File.Exists(modYmlPath))
                {
                    ModModel modModel = null;
                    
                    try
                    {
                        // Determinar el nombre del mod basado en la estructura
                        string modName;
                        
                        // Si es estructura usuario/repo (el parent no es el directorio base)
                        string parentDirPath = Directory.GetParent(modPath).FullName;
                        if (parentDirPath != baseModsPath)
                        {
                            string userName = Path.GetFileName(parentDirPath);
                            string repoName = Path.GetFileName(modPath);
                            modName = $"{userName}/{repoName}";
                        }
                        else
                        {
                            // Estructura antigua, solo el nombre del directorio
                            modName = Path.GetFileName(modPath);
                        }
                        
                        // Comprobar y establecer las rutas de imágenes
                        string iconPath = Path.Combine(modPath, "icon.png");
                        string previewPath = Path.Combine(modPath, "preview.png");
                        
                        // Depuración
                        System.Diagnostics.Debug.WriteLine($"Mod: {modName}, Icon: {iconPath} (Existe: {File.Exists(iconPath)})");
                        
                        modModel = new ModModel
                        {
                            Name = modName,
                            Path = modPath,
                            IconImageSource = File.Exists(iconPath) ? iconPath : null,
                            PreviewImageSource = File.Exists(previewPath) ? previewPath : null,
                            Metadata = File.OpenRead(modYmlPath).Using(Metadata.Read),
                            IsEnabled = enabledMods.Contains(modName)
                        };
                    }
                    catch (Exception ex)
                    {
                        // Loguear el error para diagnóstico
                        System.Diagnostics.Debug.WriteLine($"Error cargando mod en {modPath}: {ex.Message}");
                        continue;
                    }
                    
                    if (modModel != null)
                    {
                        yield return modModel;
                    }
                }
            }
        }

        public static async IAsyncEnumerable<ModUpdateModel> FetchUpdates()
        {
            foreach (var modName in Mods)
            {
                var modPath = GetModPath(modName);
                var updateCount = await RepositoryService.FetchUpdate(modPath);
                if (updateCount > 0)
                    yield return new ModUpdateModel
                    {
                        Name = modName,
                        UpdateCount = updateCount
                    };
            }
        }

        public static Task Update(string modName,
            Action<string> progressOutput = null,
            Action<float> progressNumber = null) =>
            RepositoryService.FetchAndResetUponOrigin(GetModPath(modName), progressOutput, progressNumber);

        public static Task<bool> RunPacherAsync(bool fastMode) => Task.Run(() => Handle(() =>
        {
            if (Directory.Exists(Path.Combine(ConfigurationService.GameModPath, ConfigurationService.LaunchGame)))
            {
                try
                {
                    Directory.Delete(Path.Combine(ConfigurationService.GameModPath, ConfigurationService.LaunchGame), true);
                }
                catch (Exception ex)
                {
                    Log.Warn("Unable to fully clean the mod directory:\n{0}", ex.Message);
                }
            }

            Directory.CreateDirectory(Path.Combine(ConfigurationService.GameModPath, ConfigurationService.LaunchGame));

            var patcherProcessor = new PatcherProcessor();
            var modsList = GetMods(EnabledMods).ToList();
            var packageMap = new ConcurrentDictionary<string, string>();

            for (var i = modsList.Count - 1; i >= 0; i--)
            {
                var mod = modsList[i];
                Log.Info($"Building {mod.Name} for {_gameList[ConfigurationService.GameEdition]} - {_langList[ConfigurationService.RegionId]}");

                patcherProcessor.Patch(
                    Path.Combine(ConfigurationService.GameDataLocation, ConfigurationService.LaunchGame),
                    Path.Combine(ConfigurationService.GameModPath, ConfigurationService.LaunchGame),
                    mod.Metadata,
                    mod.Path,
                    ConfigurationService.GameEdition,
                    fastMode,
                    packageMap,
                    ConfigurationService.LaunchGame,
                    ConfigurationService.PcReleaseLanguage);
            }

            using var packageMapWriter = new StreamWriter(Path.Combine(Path.Combine(ConfigurationService.GameModPath, ConfigurationService.LaunchGame), "patch-package-map.txt"));
            foreach (var entry in packageMap)
                packageMapWriter.WriteLine(entry.Key + " $$$$ " + entry.Value);
            packageMapWriter.Flush();
            packageMapWriter.Close();

            return true;
        }));

        private static string GetSourceFromUrl(string url)
        {
            var projectNameIndex = url.LastIndexOf('/');
            if (projectNameIndex < 0)
                return null;
            var projectName = url.Substring(projectNameIndex + 1);
            if (projectName.EndsWith(".git"))
                projectName = projectName.Substring(0, projectName.Length - 4);

            var firstPart = url.Substring(0, projectNameIndex);
            var authorNameIndex = firstPart.LastIndexOf('/');
            if (authorNameIndex < 0)
                authorNameIndex = firstPart.LastIndexOf(':');
            if (authorNameIndex < 0)
                return null;
            var authorName = firstPart.Substring(authorNameIndex + 1);

            return $"{authorName}/{projectName}";
        }

        private static string GetSourceFromRepository(Repository repository)
        {
            if (repository == null)
                return null;

            var remote = repository.Network.Remotes.FirstOrDefault();
            return remote != null ? GetSourceFromUrl(remote.Url) : null;
        }
    }
}
