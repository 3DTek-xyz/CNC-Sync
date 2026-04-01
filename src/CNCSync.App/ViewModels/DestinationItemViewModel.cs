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
    private FtpDataMode ftpDataMode = FtpDataMode.AutoPassive;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private SshAuthenticationMode sshAuthenticationMode = SshAuthenticationMode.Password;

    [ObservableProperty]
    private string privateKeyPath = string.Empty;

    [ObservableProperty]
    private string privateKeyPassphrase = string.Empty;

    [ObservableProperty]
    private string localRootPath = string.Empty;

    [ObservableProperty]
    private string networkHost = string.Empty;

    [ObservableProperty]
    private string networkShareName = string.Empty;

    [ObservableProperty]
    private string networkDomain = string.Empty;

    [ObservableProperty]
    private bool useCurrentUserCredentials = true;

    [ObservableProperty]
    private string requiredVpnConnectionName = string.Empty;

    [ObservableProperty]
    private bool disconnectVpnWhenFinished;

    [ObservableProperty]
    private bool replaceRemoteFolderOnUpload;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed destination" : Name;
    public IReadOnlyList<SshAuthenticationMode> AvailableSshAuthenticationModes { get; } =
        [SshAuthenticationMode.Password, SshAuthenticationMode.PrivateKey];
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
    public bool UsesSshAuthentication => Type is DestinationType.Sftp or DestinationType.Scp;
    public bool UsesFtpDataMode => Type == DestinationType.Ftp;
    public bool UsesLocalFolder => Type == DestinationType.LocalFolder;
    public bool UsesNetworkShare => Type == DestinationType.NetworkShare;
    public bool UsesRemoteHost => Type is DestinationType.Ftp or DestinationType.Sftp or DestinationType.Scp;
    public bool UsesUsername => Type is DestinationType.Ftp or DestinationType.Sftp or DestinationType.Scp or DestinationType.NetworkShare;
    public bool UsesPassword => Type switch
    {
        DestinationType.Sftp or DestinationType.Scp => SshAuthenticationMode == SshAuthenticationMode.Password,
        DestinationType.NetworkShare => !UseCurrentUserCredentials,
        _ => Type == DestinationType.Ftp && !UseAnonymousFtp
    };
    public bool UsesPrivateKeyPath => UsesSshAuthentication && SshAuthenticationMode == SshAuthenticationMode.PrivateKey;
    public bool UsesPrivateKeyPassphrase => UsesPrivateKeyPath;
    public bool SupportsAnonymousFtp => Type == DestinationType.Ftp;
    public bool UsernameIsEditable => Type switch
    {
        DestinationType.NetworkShare => !UseCurrentUserCredentials,
        DestinationType.Sftp or DestinationType.Scp => true,
        _ => !UseAnonymousFtp
    };
    public bool PasswordIsEditable => UsesPassword;
    public double UsernameOpacity => UsernameIsEditable ? 1.0 : 0.55;
    public double PasswordOpacity => PasswordIsEditable ? 1.0 : 0.55;
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnTypeChanged(DestinationType value)
    {
        OnPropertyChanged(nameof(UsesFtp));
        OnPropertyChanged(nameof(UsesSftp));
        OnPropertyChanged(nameof(UsesScp));
        OnPropertyChanged(nameof(UsesSshAuthentication));
        OnPropertyChanged(nameof(UsesFtpDataMode));
        OnPropertyChanged(nameof(UsesLocalFolder));
        OnPropertyChanged(nameof(UsesNetworkShare));
        OnPropertyChanged(nameof(UsesRemoteHost));
        OnPropertyChanged(nameof(UsesUsername));
        OnPropertyChanged(nameof(UsesPassword));
        OnPropertyChanged(nameof(UsesPrivateKeyPath));
        OnPropertyChanged(nameof(UsesPrivateKeyPassphrase));
        OnPropertyChanged(nameof(SupportsAnonymousFtp));
        OnPropertyChanged(nameof(UsernameIsEditable));
        OnPropertyChanged(nameof(PasswordIsEditable));
        OnPropertyChanged(nameof(UsernameOpacity));
        OnPropertyChanged(nameof(PasswordOpacity));
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
        OnPropertyChanged(nameof(UsesUsername));
        OnPropertyChanged(nameof(UsesPassword));
        OnPropertyChanged(nameof(UsernameIsEditable));
        OnPropertyChanged(nameof(PasswordIsEditable));
        OnPropertyChanged(nameof(UsernameOpacity));
        OnPropertyChanged(nameof(PasswordOpacity));
    }
    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnLocalRootPathChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnNetworkHostChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnNetworkShareNameChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnUseCurrentUserCredentialsChanged(bool value)
    {
        OnPropertyChanged(nameof(UsesUsername));
        OnPropertyChanged(nameof(UsesPassword));
        OnPropertyChanged(nameof(UsernameIsEditable));
        OnPropertyChanged(nameof(PasswordIsEditable));
        OnPropertyChanged(nameof(UsernameOpacity));
        OnPropertyChanged(nameof(PasswordOpacity));
    }
    partial void OnSshAuthenticationModeChanged(SshAuthenticationMode value)
    {
        OnPropertyChanged(nameof(UsesPassword));
        OnPropertyChanged(nameof(UsesPrivateKeyPath));
        OnPropertyChanged(nameof(UsesPrivateKeyPassphrase));
        OnPropertyChanged(nameof(UsernameIsEditable));
        OnPropertyChanged(nameof(PasswordIsEditable));
        OnPropertyChanged(nameof(UsernameOpacity));
        OnPropertyChanged(nameof(PasswordOpacity));
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
            FtpDataMode = settings.FtpDataMode,
            Username = settings.Username,
            Password = settings.Password,
            SshAuthenticationMode = settings.SshAuthenticationMode,
            PrivateKeyPath = settings.PrivateKeyPath,
            PrivateKeyPassphrase = settings.PrivateKeyPassphrase,
            LocalRootPath = settings.LocalRootPath,
            NetworkHost = settings.NetworkHost,
            NetworkShareName = settings.NetworkShareName,
            NetworkDomain = settings.NetworkDomain,
            UseCurrentUserCredentials = settings.UseCurrentUserCredentials,
            RequiredVpnConnectionName = settings.RequiredVpnConnectionName,
            DisconnectVpnWhenFinished = settings.DisconnectVpnWhenFinished,
            ReplaceRemoteFolderOnUpload = settings.ReplaceRemoteFolderOnUpload
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
            FtpDataMode = FtpDataMode,
            Username = Username,
            Password = Password,
            SshAuthenticationMode = SshAuthenticationMode,
            PrivateKeyPath = PrivateKeyPath,
            PrivateKeyPassphrase = PrivateKeyPassphrase,
            LocalRootPath = LocalRootPath,
            NetworkHost = NetworkHost,
            NetworkShareName = NetworkShareName,
            NetworkDomain = NetworkDomain,
            UseCurrentUserCredentials = UseCurrentUserCredentials,
            RequiredVpnConnectionName = RequiredVpnConnectionName,
            DisconnectVpnWhenFinished = DisconnectVpnWhenFinished,
            ReplaceRemoteFolderOnUpload = ReplaceRemoteFolderOnUpload
        };

    public IReadOnlyList<FtpDataMode> AvailableFtpDataModes { get; } = Enum.GetValues<FtpDataMode>();
}
