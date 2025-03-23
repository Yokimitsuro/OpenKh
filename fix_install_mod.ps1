# Script para modificar ModsService.cs y solucionar el problema de creación de directorios

$filePath = "Z:\KH1 MOD\OpenKHMod\OpenKh-2\OpenKh.Tools.ModsManager\Services\ModsService.cs"
$fileContent = Get-Content -Path $filePath -Raw

# Función para obtener el índice de inicio del método InstallModFromGithub
function Get-MethodStartIndex {
    param ($content, $methodName)
    $pattern = "public static async Task $methodName\("
    if ($content -match $pattern) {
        return $content.IndexOf($matches[0])
    }
    return -1
}

# Función para obtener el índice de fin del método
function Get-MethodEndIndex {
    param ($content, $startIndex)
    $braceLevel = 0
    $insideMethod = $false
    $chars = $content.ToCharArray()
    
    for ($i = $startIndex; $i -lt $chars.Length; $i++) {
        if ($chars[$i] -eq '{') {
            $braceLevel++
            $insideMethod = $true
        }
        elseif ($chars[$i] -eq '}') {
            $braceLevel--
            if ($insideMethod -and $braceLevel -eq 0) {
                return $i + 1  # Incluye la llave de cierre
            }
        }
    }
    return -1
}

# Obtener índices de inicio y fin del método
$methodStartIndex = Get-MethodStartIndex $fileContent "InstallModFromGithub"
$methodEndIndex = Get-MethodEndIndex $fileContent $methodStartIndex

if ($methodStartIndex -ne -1 -and $methodEndIndex -ne -1) {
    # Método mejorado que crea correctamente los directorios
    $newMethod = @"
        public static async Task InstallModFromGithub(
            string repositoryName,
            Action<string> progressOutput = null,
            Action<float> progressNumber = null)
        {
            try
            {
                var branchName = DefaultGitBranch;
                progressOutput?.Invoke(`$"Fetching file {ModMetadata} from {branchName}");
                var isValidMod = await RepositoryService.IsFileExists(repositoryName, branchName, ModMetadata);
                if (!isValidMod)
                {
                    progressOutput?.Invoke(`$"{ModMetadata} not found, fetching default branch name");
                    branchName = await RepositoryService.GetMainBranchFromRepository(repositoryName);
                    if (branchName == null)
                        throw new RepositoryNotFoundException(repositoryName);

                    progressOutput?.Invoke(`$"Fetching file {ModMetadata} from {branchName}");
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
                            progressOutput?.Invoke(`$"Base directory: {baseDir}");
                            
                            // Asegurarse de que existe el directorio base
                            if (!Directory.Exists(baseDir))
                            {
                                progressOutput?.Invoke(`$"Creating base directory: {baseDir}");
                                Directory.CreateDirectory(baseDir);
                            }
                            
                            // Primero asegurarnos de que existe el directorio del usuario
                            string userDir = Path.Combine(baseDir, userName);
                            if (!Directory.Exists(userDir))
                            {
                                progressOutput?.Invoke(`$"Creating user directory: {userDir}");
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
                    
                    progressOutput?.Invoke(`$"Mod path: {modPath}");
                    
                    // Verificar si el mod ya existe
                    if (Directory.Exists(modPath))
                    {
                        var errorMessage = MessageBox.Show(`$"A mod with the name '{repositoryName}' already exists. Do you want to overwrite the mod install.", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);

                        switch (errorMessage)
                        {
                            case MessageBoxResult.Yes:
                                Handle(() =>
                                {
                                    MainViewModel.overwriteMod = true;
                                    foreach (var filePath in Directory.GetFiles(modPath, "*", SearchOption.AllDirectories))
                                    {
                                        var attributes = File.GetAttributes(filePath);
                                        if (attributes.HasFlag(FileAttributes.ReadOnly))
                                            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                                    }

                                    Directory.Delete(modPath, true);
                                });
                                break;
                            case MessageBoxResult.No:
                                throw new ModAlreadyExistsExceptions(repositoryName);
                        }
                    }
                    
                    // Crear el directorio del mod si no existe o fue eliminado
                    if (!Directory.Exists(modPath))
                    {
                        progressOutput?.Invoke(`$"Creating mod directory: {modPath}");
                        Directory.CreateDirectory(modPath);
                    }
                }
                catch (Exception ex)
                {
                    progressOutput?.Invoke(`$"Error creating directory structure: {ex.Message}");
                    throw;
                }

                progressOutput?.Invoke(`$"Mod found, initializing cloning process");
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
                        Repository.Clone(`$"https://github.com/{repositoryName}", modPath, options);
                    }
                    catch (Exception ex)
                    {
                        progressOutput?.Invoke(`$"Error cloning repository {repositoryName}: {ex.Message}");
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                progressOutput?.Invoke(`$"Error in InstallModFromGithub: {ex.Message}");
                throw;
            }
        }
"@

    # Reemplazar el método original con el nuevo
    $newContent = $fileContent.Substring(0, $methodStartIndex) + $newMethod + $fileContent.Substring($methodEndIndex)
    
    # Guardar el archivo modificado
    $newContent | Set-Content -Path $filePath -NoNewline
    
    Write-Host "El método InstallModFromGithub ha sido actualizado correctamente."
    
    # También modificar el método InstallDependencies para usar la misma lógica
    $methodStartIndex = Get-MethodStartIndex $newContent "InstallDependencies"
    $methodEndIndex = Get-MethodEndIndex $newContent $methodStartIndex
    
    if ($methodStartIndex -ne -1 -and $methodEndIndex -ne -1) {
        $updatedContent = Get-Content -Path $filePath -Raw
        
        # Método mejorado para instalar dependencias
        $newDependenciesMethod = @"
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
                        progressOutput?.Invoke(`$"Dependency {dependency.Name} is already installed. Skipping.");
                        continue;
                    }

                    progressOutput?.Invoke(`$"Installing dependency: {dependency.Name}");
                    
                    // Verificar si el nombre de la dependencia tiene el formato de repositorio de GitHub (usuario/repo)
                    string repoName = dependency.Name;
                    if (!repoName.Contains("/"))
                    {
                        progressOutput?.Invoke(`$"Warning: Dependency name '{repoName}' does not appear to be a valid GitHub repository name (should be in format 'username/repository').");
                        continue;
                    }
                    
                    try 
                    {
                        // Crear todos los directorios necesarios
                        string[] parts = repoName.Split('/');
                        if (parts.Length == 2)
                        {
                            string userName = parts[0];
                            string repoName2 = parts[1];
                            
                            // Obtener el directorio base para los mods
                            string baseDir = ConfigurationService.ModCollectionPath;
                            
                            // Asegurarse de que existe el directorio base
                            if (!Directory.Exists(baseDir))
                            {
                                progressOutput?.Invoke(`$"Creating base directory: {baseDir}");
                                Directory.CreateDirectory(baseDir);
                            }
                            
                            // Crear el directorio del usuario
                            string userDir = Path.Combine(baseDir, userName);
                            if (!Directory.Exists(userDir))
                            {
                                progressOutput?.Invoke(`$"Creating user directory: {userDir}");
                                Directory.CreateDirectory(userDir);
                            }
                            
                            // Crear el directorio del mod
                            string modPath = Path.Combine(userDir, repoName2);
                            if (!Directory.Exists(modPath))
                            {
                                progressOutput?.Invoke(`$"Creating mod directory: {modPath}");
                                Directory.CreateDirectory(modPath);
                            }
                            
                            // Configurar opciones de clon
                            var options = new CloneOptions
                            {
                                RecurseSubmodules = true
                            };
                            
                            await Task.Run(() => {
                                Repository.Clone(`$"https://github.com/{repoName}", modPath, options);
                                progressOutput?.Invoke(`$"Dependency {dependency.Name} installed successfully");
                            });
                        }
                        else
                        {
                            progressOutput?.Invoke(`$"Warning: Invalid repository format for {repoName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Manejo específico para errores
                        progressOutput?.Invoke(`$"Warning: Could not install dependency {dependency.Name}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    progressOutput?.Invoke(`$"Warning: Error processing dependency {dependency.Name}: {ex.Message}");
                }
            }
        }
"@

        # Reemplazar el método original con el nuevo
        $newestContent = $updatedContent.Substring(0, $methodStartIndex) + $newDependenciesMethod + $updatedContent.Substring($methodEndIndex)
        
        # Guardar el archivo modificado
        $newestContent | Set-Content -Path $filePath -NoNewline
        
        Write-Host "El método InstallDependencies también ha sido actualizado correctamente."
    }
}
else {
    Write-Host "No se pudo encontrar el método InstallModFromGithub en el archivo."
}
