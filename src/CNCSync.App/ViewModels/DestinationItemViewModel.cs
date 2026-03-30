using CNCSync.Core.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CNCSync.App.ViewModels;

public partial class DestinationItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private DestinationType type = DestinationType.Ftp;

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
    private string localRootPath = string.Empty;

    [ObservableProperty]
    private NetworkShareProtocol networkProtocol = NetworkShareProtocol.Smb;

    [ObservableProperty]
    private string networkHost = string.Empty;

    [ObservableProperty]
    private string networkShareName = string.Empty;

    [ObservableProperty]
    private string networkDomain = string.Empty;

    [ObservableProperty]
    private bool useCurrentUserCredentials = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed destination" : Name;
    public string EndpointSummary => Type switch
    {
        DestinationType.LocalFolder => LocalRootPath,
        DestinationType.NetworkShare => string.IsNullOrWhiteSpace(NetworkHost) || string.IsNullOrWhiteSpace(NetworkShareName)
            ? NetworkHost
            : $"{NetworkHost}/{NetworkShareName}",
        _ => Host
    };
    public bool UsesFtp => Type == DestinationType.Ftp;
    public bool UsesSftp => Type == DestinationType.Sftp;
    public bool UsesScp => Type == DestinationType.Scp;
    public bool UsesLocalFolder => Type == DestinationType.LocalFolder;
    public bool UsesNetworkShare => Type == DestinationType.NetworkShare;
    public bool UsesRemoteHost => Type is DestinationType.Ftp or DestinationType.Sftp or DestinationType.Scp;
    public bool UsesCredentials => Type is DestinationType.Ftp or DestinationType.Sftp or DestinationType.Scp or DestinationType.NetworkShare;
    public bool SupportsAnonymousFtp => Type == DestinationType.Ftp;
    public bool CredentialsAreEditable => Type switch
    {
        DestinationType.NetworkShare => !UseCurrentUserCredentials,
        DestinationType.Sftp or DestinationType.Scp => true,
        _ => !UseAnonymousFtp
    };
    public double CredentialsOpacity => CredentialsAreEditable ? 1.0 : 0.55;
    public IReadOnlyList<NetworkShareProtocol> AvailableNetworkProtocols { get; } = Enum.GetValues<NetworkShareProtocol>();

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnTypeChanged(DestinationType value)
    {
        OnPropertyChanged(nameof(UsesFtp));
        OnPropertyChanged(nameof(UsesSftp));
        OnPropertyChanged(nameof(UsesScp));
        OnPropertyChanged(nameof(UsesLocalFolder));
        OnPropertyChanged(nameof(UsesNetworkShare));
        OnPropertyChanged(nameof(UsesRemoteHost));
        OnPropertyChanged(nameof(UsesCredentials));
        OnPropertyChanged(nameof(SupportsAnonymousFtp));
        OnPropertyChanged(nameof(CredentialsAreEditable));
        OnPropertyChanged(nameof(CredentialsOpacity));
        OnPropertyChanged(nameof(EndpointSummary));
        OnPropertyChanged(nameof(DisplayName));

        if (value is DestinationType.Sftp or DestinationType.Scp)
        {
            UseAnonymousFtp = false;
            if (Port == 21)
            {
                Port = 22;
            }
        }
        else if (value == DestinationType.NetworkShare)
        {
            UseAnonymousFtp = false;
        }
        else if (value == DestinationType.Ftp && Port == 22)
        {
            Port = 21;
        }
    }
    partial void OnUseAnonymousFtpChanged(bool value)
    {
        OnPropertyChanged(nameof(CredentialsAreEditable));
        OnPropertyChanged(nameof(CredentialsOpacity));
    }
    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnLocalRootPathChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnNetworkHostChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnNetworkShareNameChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnUseCurrentUserCredentialsChanged(bool value)
    {
        OnPropertyChanged(nameof(CredentialsAreEditable));
        OnPropertyChanged(nameof(CredentialsOpacity));
    }

    public static DestinationItemViewModel FromSettings(DestinationSettings settings) =>
        new()
        {
            Id = settings.Id,
            Name = settings.Name,
            Type = settings.Type,
            Host = settings.Host,
            Port = settings.Port,
            RemoteBasePath = settings.RemoteBasePath,
            UseAnonymousFtp = settings.UseAnonymousFtp,
            Username = settings.Username,
            Password = settings.Password,
            LocalRootPath = settings.LocalRootPath,
            NetworkProtocol = settings.NetworkProtocol,
            NetworkHost = settings.NetworkHost,
            NetworkShareName = settings.NetworkShareName,
            NetworkDomain = settings.NetworkDomain,
            UseCurrentUserCredentials = settings.UseCurrentUserCredentials
        };

    public DestinationSettings ToSettings() =>
        new()
        {
            Id = Id,
            Name = Name,
            Type = Type,
            Host = Host,
            Port = Port,
            RemoteBasePath = RemoteBasePath,
            UseAnonymousFtp = UseAnonymousFtp,
            Username = Username,
            Password = Password,
            LocalRootPath = LocalRootPath,
            NetworkProtocol = NetworkProtocol,
            NetworkHost = NetworkHost,
            NetworkShareName = NetworkShareName,
            NetworkDomain = NetworkDomain,
            UseCurrentUserCredentials = UseCurrentUserCredentials
        };
}
