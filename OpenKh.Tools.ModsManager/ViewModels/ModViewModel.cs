using OpenKh.Common;
using OpenKh.Tools.ModsManager.Models;
using OpenKh.Tools.ModsManager.Services;
using OpenKh.Tools.ModsManager.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xe.Tools;
using Xe.Tools.Wpf.Commands;
using System.Drawing;
using System.Drawing.Imaging;
using static OpenKh.Tools.ModsManager.Helpers;

// Definir alias para evitar ambigüedades
using WpfBrush = System.Windows.Media.Brush;
using DrawingBrush = System.Drawing.Brush;

namespace OpenKh.Tools.ModsManager.ViewModels
{
    public class ModViewModel : BaseNotifyPropertyChanged
    {
        public ColorThemeService ColorTheme => ColorThemeService.Instance;
        private static readonly string FallbackImage = null;
        private readonly ModModel _model;
        private readonly IChangeModEnableState _changeModEnableState;
        private int _updateCount;

        public ModViewModel(ModModel model, IChangeModEnableState changeModEnableState)
        {
            _model = model;
            _changeModEnableState = changeModEnableState;

            var nameIndex = Source.IndexOf('/');
            if (nameIndex > 0)
            {
                Author = Source[0..nameIndex];
                Name = Source[(nameIndex + 1)..];
            }
            else
            {
                Author = _model.Metadata?.OriginalAuthor;
                Name = Source;
            }

            ReadMetadata();
            if (Title != null)
                Name = Title;

            UpdateCommand = new RelayCommand(async _ =>
            {
                InstallModProgressWindow progressWindow = null;
                try
                {
                    progressWindow = Application.Current.Dispatcher.Invoke(() =>
                    {
                        var progressWindow = new InstallModProgressWindow
                        {
                            OperationName = "Updating",
                            ModName = Source,
                            ProgressText = "Initializing",
                            ShowActivated = true
                        };
                        progressWindow.Show();
                        return progressWindow;
                    });

                    await ModsService.Update(Source, progress =>
                    {
                        Application.Current.Dispatcher.Invoke(() => progressWindow.ProgressText = progress);
                    }, nProgress =>
                    {
                        Application.Current.Dispatcher.Invoke(() => progressWindow.ProgressValue = nProgress);
                    });

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressWindow.ProgressText = "Reading latest changes";
                        progressWindow.ProgressValue = 1f;
                    });

                    var mod = ModsService.GetMods(new string[] { Source }).First();
                    ReadMetadata();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressWindow.Close();
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn("Unable to update the mod `{0}`: {1}\n"
                        , Source
                        , Log.FormatSecondaryLinesWithIndent(ex.ToString(), "  ")
                    );
                    Handle(ex);
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() => progressWindow?.Close());
                }
            });
        }

        public RelayCommand UpdateCommand { get; }

        public bool Enabled
        {
            get => _model.IsEnabled;
            set
            {
                _model.IsEnabled = value;
                _changeModEnableState.ModEnableStateChanged();
                OnPropertyChanged();
            }
        }

        public ImageSource IconImage { get; private set; }
        public ImageSource PreviewImage { get; private set; }
        public Visibility PreviewImageVisibility => PreviewImage != null ? Visibility.Visible : Visibility.Collapsed;

        public bool IsHosted => _model.Name.Contains('/');
        public string Path => _model.Path;
        public Visibility SourceVisibility => IsHosted ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LocalVisibility => !IsHosted ? Visibility.Visible : Visibility.Collapsed;

        public string Title => _model?.Metadata?.Title ?? Name;
        public string Name { get; }
        public string Author { get; }
        public string Source => _model.Name;
        public string AuthorUrl => $"https://github.com/{Author}";
        public string SourceUrl => $"https://github.com/{Source}";
        public string ReportBugUrl => $"https://github.com/{Source}/issues";
        public string FilesToPatch => string.Join('\n', GetFilesToPatch());

        public string Description => _model.Metadata?.Description;

        public List<string> Dependencies => _model.Metadata?.Dependencies?.Select(d => d.Name).ToList() ?? new List<string>();
        public bool HasDependencies => Dependencies.Count > 0;
        public string DependenciesList => string.Join(", ", Dependencies);
        public Visibility DependenciesVisibility => HasDependencies ? Visibility.Visible : Visibility.Collapsed;

        public string Priority => _model.Metadata?.Priority;
        public bool HasPriority => !string.IsNullOrEmpty(Priority);
        public Visibility PriorityVisibility => HasPriority ? Visibility.Visible : Visibility.Collapsed;
        public bool IsHighPriority => Priority?.ToUpperInvariant() == "ABOVE";
        public bool IsLowPriority => Priority?.ToUpperInvariant() == "BELOW";
        public string PriorityText => IsHighPriority ? "Install this ABOVE all other mods" : 
                                         IsLowPriority ? "Install this BELOW all other mods" : 
                                         $"Priority: {Priority}";

        public System.Windows.Media.Brush PriorityBackground => IsHighPriority ? System.Windows.Media.Brushes.DarkRed : 
                                           IsLowPriority ? System.Windows.Media.Brushes.DarkBlue : 
                                           System.Windows.Media.Brushes.DarkGray;

        public bool HasMissingDependencies { get; set; }
        public Visibility MissingDependenciesVisibility => HasMissingDependencies ? Visibility.Visible : Visibility.Collapsed;
        public string MissingDependenciesMessage => "Missing dependencies! Please install required mods first.";

        public string Homepage
        {
            get
            {
                if (Source == null)
                    return null;

                var author = System.IO.Path.GetDirectoryName(Source);
                var project = System.IO.Path.GetFileName(Source);
                return $"https://{author}.github.io/{project}";
            }
        }

        public int UpdateCount
        {
            get => _updateCount;
            set
            {
                _updateCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(UpdateVisibility));
            }
        }

        public bool IsUpdateAvailable => UpdateCount > 0;
        public Visibility UpdateVisibility => IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

        private IEnumerable<string> GetFilesToPatch()
        {
            foreach (var asset in _model.Metadata?.Assets ?? Enumerable.Empty<Patcher.AssetFile>())
            {
                yield return asset.Name;
                if (asset.Multi != null)
                {
                    foreach (var multiAsset in asset.Multi)
                        yield return multiAsset.Name;
                }
            }
        }

        private void ReadMetadata() => Task.Run(() =>
        {
            LoadImage(_model.IconImageSource, FallbackImage, image =>
            {
                IconImage = image;
                OnPropertyChanged(nameof(IconImage));
            });
            LoadImage(_model.PreviewImageSource, null, image =>
            {
                PreviewImage = image;
                OnPropertyChanged(nameof(PreviewImage));
                OnPropertyChanged(nameof(PreviewImageVisibility));
            });

            Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(Homepage));
                OnPropertyChanged(nameof(FilesToPatch));
                OnPropertyChanged(nameof(Dependencies));
                OnPropertyChanged(nameof(DependenciesList));
                OnPropertyChanged(nameof(DependenciesVisibility));
                OnPropertyChanged(nameof(Priority));
                OnPropertyChanged(nameof(PriorityVisibility));
                OnPropertyChanged(nameof(IsHighPriority));
                OnPropertyChanged(nameof(IsLowPriority));
                OnPropertyChanged(nameof(PriorityText));
                OnPropertyChanged(nameof(PriorityBackground));
                UpdateCount = 0;
            });
        });

        private static void LoadImage(string source, string fallback, Action<ImageSource> setter)
        {
            // Debug para ver qué rutas estamos intentando cargar
            System.Diagnostics.Debug.WriteLine($"Intentando cargar imagen desde: {source}");
            
            if (string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(fallback))
            {
                LoadImage(fallback, null, setter);
                return;
            }

            try
            {
                if (!File.Exists(source))
                {
                    System.Diagnostics.Debug.WriteLine($"Archivo no encontrado: {source}");
                    if (!string.IsNullOrEmpty(fallback))
                        LoadImage(fallback, null, setter);
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Archivo encontrado, cargando: {source}");
                
                // Cargar los bytes de la imagen
                byte[] imageData = File.ReadAllBytes(source);
                
                Application.Current.Dispatcher.Invoke(() => 
                {
                    try
                    {
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = new MemoryStream(imageData);
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();
                        
                        setter(bitmapImage);
                        System.Diagnostics.Debug.WriteLine($"Imagen cargada correctamente: {source}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error al inicializar BitmapImage: {ex.Message}");
                        
                        // Si hay una imagen de fallback, intentar cargarla
                        if (!string.IsNullOrEmpty(fallback))
                        {
                            try
                            {
                                LoadImage(fallback, null, setter);
                            }
                            catch (Exception fallbackEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error también al cargar fallback: {fallbackEx.Message}");
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                // Loguear el error
                System.Diagnostics.Debug.WriteLine($"Error al cargar imagen {source}: {ex.Message}");
                
                // Si hay una imagen de fallback, intentar cargarla
                if (!string.IsNullOrEmpty(fallback))
                {
                    try
                    {
                        LoadImage(fallback, null, setter);
                    }
                    catch (Exception fallbackEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error también al cargar fallback: {fallbackEx.Message}");
                    }
                }
            }
        }
    }
}
