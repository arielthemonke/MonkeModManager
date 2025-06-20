using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Newtonsoft.Json;

namespace MonkeModManager;

public partial class MainWindow : Window
{
    public ObservableCollection<Mod> Mods { get; } = new();
    private string gamePath;
    private string pluginsPath;
    private readonly HttpClient httpClient = new();
    private bool isDark = false;

    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();
        
        this.Opened += async (s, e) =>
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessage($"Initialization failed: {ex.Message}");
            }
        };
    }
    
    private static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "MonkeModManager");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "config.json");
    }

    private async Task InitializeAsync()
    {
        if (!File.Exists(GetConfigPath()))
        {
            var selectedPath = await ShowGamePathDialog();
            if (string.IsNullOrEmpty(selectedPath))
            {
                Close();
                return;
            }
            await SaveGamePath(selectedPath);
        }
        else
        {
            gamePath = GetGamePath();
            if (string.IsNullOrEmpty(gamePath))
            {
                await ShowErrorMessage("select a game path");
                var selectedPath = await ShowGamePathDialog();
                if (string.IsNullOrEmpty(selectedPath))
                {
                    Close();
                    return;
                }
                await SaveGamePath(selectedPath);
            }
        }

        pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
        
        await CheckOrInstallBepInEx();
        await LoadModsFromTheNewGitHubRepoAsync();
    }
    
    public async Task LoadModsFromTheNewGitHubRepoAsync()
    {
        using var client = new HttpClient();
        const string url = "https://raw.githubusercontent.com/The-Graze/MonkeModInfo/master/modinfo.json";

        try
        {
            var json = await client.GetStringAsync(url);
            var mods = JsonConvert.DeserializeObject<List<Mod>>(json);
            Mods.Clear();
            ItemControl0.Items.Clear();
            if (mods != null)
            {
                foreach (var mod in mods)
                {
                    Mods.Add(mod);
                    var modControl = MakeModControl(mod);
                    ItemControl0.Items.Add(modControl);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Loading mods failed: {ex.Message}");
            await ShowErrorMessage("Couldn't load the mod list from GitHub.");
        }
    }
    
    public async Task fixBepInExConfig()
    {
        string url = "https://raw.githubusercontent.com/arielthemonke/ModInfo/main/BepInEx.cfg";
        string configPath = Path.Combine(gamePath, "BepInEx", "config", "BepInEx.cfg");

        try
        {
            using var client = new HttpClient();
            var configContent = await client.GetStringAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            await File.WriteAllTextAsync(configPath, configContent);

            Console.WriteLine("yay i did a thing!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"never mind, it didnt work :sob: {ex.Message}");
            await ShowErrorMessage("it didnt work. error code: 50000000");
        }
    }


    private async Task SaveGamePath(string path)
    {
        gamePath = path;
        var config = new Config { GamePath = path };
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        await File.WriteAllTextAsync(GetConfigPath(), json);
        MessageBox0.Text = "Game path saved successfully!";
    }

    private Border MakeModControl(Mod mod)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = GetGroupColor(mod.Group),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(6),
            Padding = new Thickness(12),
            Background = Brushes.White
        };

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var contentStack = new StackPanel
        {
            Spacing = 4
        };

        var nameTextBlock = new TextBlock
        {
            Text = mod.DisplayName,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.DarkBlue
        };

        var infoPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };

        if (!string.IsNullOrEmpty(mod.Author))
        {
            var authorTextBlock = new TextBlock
            {
                Text = mod.AuthorInfo,
                FontSize = 12,
                Foreground = Brushes.DarkGreen,
                FontStyle = FontStyle.Italic
            };
            infoPanel.Children.Add(authorTextBlock);
        }

        if (!string.IsNullOrEmpty(mod.Group))
        {
            var groupBadge = new Border
            {
                Background = GetGroupColor(mod.Group),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2)
            };
            var groupText = new TextBlock
            {
                Text = mod.Group,
                FontSize = 10,
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold
            };
            groupBadge.Child = groupText;
            infoPanel.Children.Add(groupBadge);
        }

        TextBlock dependenciesText = null;
        if (mod.Dependencies?.Any() == true)
        {
            dependenciesText = new TextBlock
            {
                Text = $"Dependencies: {string.Join(", ", mod.Dependencies)}",
                FontSize = 10,
                Foreground = Brushes.Orange,
                TextWrapping = TextWrapping.Wrap
            };
        }

        var urlTextBlock = new TextBlock
        {
            Text = ShortenUrl(mod.DownloadUrl),
            FontSize = 10,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        };

        var isInstalled = IsModInstalled(mod);
        var statusText = new TextBlock
        {
            Text = isInstalled ? "\u2713 Installed" : "Not installed",
            FontSize = 12,
            Foreground = isInstalled ? Brushes.Green : Brushes.Orange
        };

        contentStack.Children.Add(nameTextBlock);
        contentStack.Children.Add(infoPanel);
        if (dependenciesText != null)
            contentStack.Children.Add(dependenciesText);
        contentStack.Children.Add(urlTextBlock);
        contentStack.Children.Add(statusText);

        var buttonStack = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var installButton = new Button
        {
            Content = isInstalled ? "Reinstall" : "Install",
            Background = isInstalled ? Brushes.Orange : Brushes.Green,
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8),
            CornerRadius = new CornerRadius(4)
        };

        installButton.Click += async (s, e) =>
        {
            await InstallMod(mod, installButton, statusText);
        };

        if (isInstalled)
        {
            var uninstallButton = new Button
            {
                Content = "Uninstall",
                Background = Brushes.Red,
                Foreground = Brushes.White,
                Padding = new Thickness(16, 8),
                CornerRadius = new CornerRadius(4)
            };

            uninstallButton.Click += async (s, e) =>
            {
                await UninstallMod(mod, installButton, uninstallButton, statusText);
            };

            buttonStack.Children.Add(uninstallButton);
        }

        buttonStack.Children.Add(installButton);

        Grid.SetColumn(contentStack, 0);
        Grid.SetColumn(buttonStack, 1);

        mainGrid.Children.Add(contentStack);
        mainGrid.Children.Add(buttonStack);

        border.Child = mainGrid;
        return border;
    }

    private IBrush GetGroupColor(string group)
    {
        return group?.ToLower() switch
        {
            "core" => Brushes.DarkBlue,
            "libraries" => Brushes.Purple,
            "gameplay" => Brushes.Green,
            "cosmetic" => Brushes.Pink,
            "utility" => Brushes.Orange,
            _ => Brushes.LightGray
        };
    }

    private string ShortenUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || url.Length <= 50)
            return url;
        
        return url.Substring(0, 47) + "...";
    }

    private bool IsModInstalled(Mod mod)
    {
        if (string.IsNullOrEmpty(pluginsPath) || !Directory.Exists(pluginsPath))
            return false;
        string fileName = Path.GetFileName(new Uri(mod.DownloadUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName))
            return false;
        try
        {
            var files = Directory.GetFiles(pluginsPath, "*.dll", SearchOption.AllDirectories);
            return files.Any(file => Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
    
    private async Task ExtractZipToAFolder(string zipPath, string targetDirectory)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destinationPath = Path.Combine(targetDirectory, entry.FullName);
            var directory = Path.GetDirectoryName(destinationPath);
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private async Task InstallMod(Mod mod, Button installButton, TextBlock statusText)
    {
        try
        {
            installButton.IsEnabled = false;
            installButton.Content = "Installing...";
            MessageBox0.Text = $"Installing {mod.Name}...";

            var installLocation = !string.IsNullOrEmpty(mod.InstallLocation) 
                ? mod.InstallLocation
                : "BepInEx/plugins";
            
            var targetDirectory = Path.Combine(gamePath, installLocation);
            Directory.CreateDirectory(targetDirectory);

            var downloadUrl = mod.DownloadUrl;
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"{mod.Name}.dll";
            }

            var downloadPath = await DownloadFile(downloadUrl, fileName);

            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractZipToAFolder(downloadPath, targetDirectory);
                File.Delete(downloadPath);
            }
            else
            {
                var targetPath = Path.Combine(targetDirectory, fileName);
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(downloadPath, targetPath);
            }

            statusText.Text = "\u2713 Installed";
            statusText.Foreground = Brushes.Green;
            installButton.Content = "Reinstall";
            installButton.Background = Brushes.Orange;

            MessageBox0.Text = $"Successfully installed {mod.Name} v{mod.Version}!";
        }
        catch (Exception ex)
        {
            await ShowErrorMessage($"Failed to install {mod.Name}: {ex.Message}");
        }
        finally
        {
            installButton.IsEnabled = true;
        }
    }

    private async Task UninstallMod(Mod mod, Button installButton, Button uninstallButton, TextBlock statusText)
    {
        try
        {
            var installLocation = !string.IsNullOrEmpty(mod.InstallLocation) 
                ? mod.InstallLocation 
                : "BepInEx/plugins";
            
            var targetDirectory = Path.Combine(gamePath, installLocation);
            
            var deleted = false;
            
            var dllFileName = $"{mod.Name}.dll";
            var dllPath = Path.Combine(targetDirectory, dllFileName);
            
            if (File.Exists(dllPath))
            {
                File.Delete(dllPath);
                deleted = true;
            }
            else
            {
                if (Directory.Exists(targetDirectory))
                {
                    var files = Directory.GetFiles(targetDirectory, "*.dll", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (Path.GetFileNameWithoutExtension(file)
                            .Equals(mod.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                            deleted = true;
                            break;
                        }
                    }
                }
            }

            if (deleted)
            {
                statusText.Text = "Not installed";
                statusText.Foreground = Brushes.Orange;
                installButton.Content = "Install";
                installButton.Background = Brushes.Green;

                if (uninstallButton.Parent is StackPanel parent)
                {
                    parent.Children.Remove(uninstallButton);
                }

                MessageBox0.Text = $"uninstalled {mod.Name}!";
            }
            else
            {
                MessageBox0.Text = $"couldnt find {mod.Name} to uninstall.";
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessage($"couldnt uninstall {mod.Name}: {ex.Message}");
        }
    }

    private async Task<string> DownloadFile(string url, string fileName)
    {
        try
        {
            var fileBytes = await httpClient.GetByteArrayAsync(url);
            var downloadDir = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
            Directory.CreateDirectory(downloadDir);
            
            var filePath = Path.Combine(downloadDir, fileName);
            await File.WriteAllBytesAsync(filePath, fileBytes);
            
            return filePath;
        }
        catch (Exception ex)
        {
            throw new Exception($"Download failed: {ex.Message}");
        }
    }

    private string GetGamePath()
    {
        try
        {
            if (!File.Exists(GetConfigPath()))
                return null;

            var json = File.ReadAllText(GetConfigPath());
            var config = JsonConvert.DeserializeObject<Config>(json);
            
            if (!string.IsNullOrWhiteSpace(config?.GamePath) && Directory.Exists(config.GamePath))
            {
                return config.GamePath;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading the config{ex.Message}");
        }
        
        return null;
    }

    private async Task<bool> CheckOrInstallBepInEx()
    {
        try
        {
            var bepInExPath = Path.Combine(gamePath, "BepInEx");
            
            if (!Directory.Exists(bepInExPath))
            {
                MessageBox0.Text = "BepInEx not found. Downloading...";
                
                var bytes = await httpClient.GetByteArrayAsync(
                    "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip");
                
                var tempPath = Path.Combine(Path.GetTempPath(), $"BepInEx_{Guid.NewGuid()}.zip");
                await File.WriteAllBytesAsync(tempPath, bytes);
                
                MessageBox0.Text = "Extracting BepInEx please wait!!!!!";
                await ExtractBepInEx(tempPath);
                
                File.Delete(tempPath);
                await fixBepInExConfig();
                MessageBox0.Text = "BepInEx installed successfully!";
                
                return true;
            }
            
            MessageBox0.Text = "BepInEx already installed.";
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorMessage($"couldnt install bepinex: {ex.Message}");
            return false;
        }
    }

    private async Task ExtractBepInEx(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var destinationPath = Path.Combine(gamePath, entry.FullName);
                var directory = Path.GetDirectoryName(destinationPath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"couldnr extract {ex.Message}");
        }
    }

    private async Task<string> ShowGamePathDialog()
    {
        var dialog = new Window
        {
            Title = "Select Game Path",
            Width = 500,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15
        };

        stackPanel.Children.Add(new TextBlock
        {
            Text = "Select your game path:",
            FontSize = 14,
            FontWeight = FontWeight.Medium
        });

        var pathTextBox = new TextBox
        {
            IsReadOnly = true,
            Height = 32,
            Background = Brushes.LightGray
        };

        var browseButton = new Button
        {
            Content = "Browse",
            Width = 100,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 32,
            IsEnabled = false
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 32
        };

        string selectedPath = null;

        browseButton.Click += async (sender, e) =>
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Select Game Folder"
            };

            var result = await folderDialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(result))
            {
                pathTextBox.Text = result;
                selectedPath = result;
                okButton.IsEnabled = true;
            }
        };

        okButton.Click += (sender, e) =>
        {
            dialog.Close();
        };

        cancelButton.Click += (sender, e) =>
        {
            selectedPath = null;
            dialog.Close();
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);

        stackPanel.Children.Add(pathTextBox);
        stackPanel.Children.Add(browseButton);
        stackPanel.Children.Add(buttonPanel);

        dialog.Content = stackPanel;

        await dialog.ShowDialog(this);
        return selectedPath;
    }

    private async Task ShowErrorMessage(string message)
    {
        MessageBox0.Text = $"Error: {message}";
        Console.WriteLine($"Error: {message}");
        
        var errorDialog = new Window
        {
            Title = "an ERROR has errored your app",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15
        };

        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        okButton.Click += (s, e) => errorDialog.Close();
        content.Children.Add(okButton);

        errorDialog.Content = content;
        await errorDialog.ShowDialog(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        httpClient?.Dispose();
        base.OnClosed(e);
    }
}

public class Mod
{
    public string Name { get; set; }
    public string Author { get; set; }
    public string Version { get; set; }
    public List<string> Dependencies { get; set; } = new List<string>();
    [JsonProperty("install_location")]
    public string InstallLocation { get; set; }
    [JsonProperty("git_path")]
    public string GitPath { get; set; }
    public string Group { get; set; }
    [JsonProperty("download_url")]
    public string DownloadUrl { get; set; }

    public string ModName => Name;
    public string URL => DownloadUrl;
    public string DisplayName => $"{Name} v{Version}";
    public string AuthorInfo => !string.IsNullOrEmpty(Author) ? $"by {Author}" : "";
}

public class Config
{
    public string GamePath { get; set; }
}