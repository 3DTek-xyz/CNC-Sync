using CNCSync.Core.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CNCSync.App.ViewModels;

public partial class FtpDestinationItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string host = string.Empty;

    [ObservableProperty]
    private int port = 21;

    [ObservableProperty]
    private string remoteBasePath = string.Empty;

    [ObservableProperty]
    private bool useAnonymousFtp = true;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool autoUpload = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed FTP destination" : Name;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    public static FtpDestinationItemViewModel FromSettings(FtpDestinationSettings settings) =>
        new()
        {
            Id = settings.Id,
            Name = settings.Name,
            Host = settings.Host,
            Port = settings.Port,
            RemoteBasePath = settings.RemoteBasePath,
            UseAnonymousFtp = settings.UseAnonymousFtp,
            Username = settings.Username,
            Password = settings.Password,
            AutoUpload = settings.AutoUpload
        };

    public FtpDestinationSettings ToSettings() =>
        new()
        {
            Id = Id,
            Name = Name,
            Host = Host,
            Port = Port,
            RemoteBasePath = RemoteBasePath,
            UseAnonymousFtp = UseAnonymousFtp,
            Username = Username,
            Password = Password,
            AutoUpload = AutoUpload
        };
}
