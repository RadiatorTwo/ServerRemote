using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerRemote.Contracts;

namespace ServerRemote.App.ViewModels;

/// <summary>A row in the services list with status-dependent start/stop/restart commands.</summary>
public sealed partial class ServiceItemViewModel : ObservableObject
{
    private readonly Func<string, ServiceControlAction, Task> _control;

    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private ServiceState _state;
    [ObservableProperty] private bool _canControl;
    [ObservableProperty] private bool _isBusy;

    public ServiceItemViewModel(ServiceStatusDto dto, Func<string, ServiceControlAction, Task> control)
    {
        _control = control;
        Update(dto);
    }

    public void Update(ServiceStatusDto dto)
    {
        Key = dto.Key;
        DisplayName = dto.DisplayName;
        State = dto.State;
        CanControl = dto.CanControl;
    }

    public bool IsRunning => State == ServiceState.Running;
    public bool IsStopped => State == ServiceState.Stopped;
    public bool IsNotInstalled => State == ServiceState.NotInstalled;

    /// <summary>Start only makes sense for a stopped, controllable service — and not while an action is running.</summary>
    public bool CanStart => CanControl && IsStopped && !IsBusy;

    /// <summary>Stop/restart only for a running, controllable service — and not while an action is running.</summary>
    public bool CanStopOrRestart => CanControl && IsRunning && !IsBusy;

    /// <summary>Localized, human-readable status text.</summary>
    public string StateText => State switch
    {
        ServiceState.Running => "Running",
        ServiceState.Stopped => "Stopped",
        ServiceState.StartPending => "Starting…",
        ServiceState.StopPending => "Stopping…",
        ServiceState.Paused => "Paused",
        ServiceState.PausePending => "Pausing…",
        ServiceState.ContinuePending => "Resuming…",
        ServiceState.NotInstalled => "Not installed",
        _ => "Unknown"
    };

    /// <summary>Status color from the central palette for the badge.</summary>
    public Color StatusColor => State switch
    {
        ServiceState.Running => Resolve("Success", Color.FromArgb("#22C55E")),
        ServiceState.Stopped => Resolve("Danger", Color.FromArgb("#E11D48")),
        ServiceState.NotInstalled => Resolve("TextMuted", Color.FromArgb("#71717A")),
        ServiceState.Unknown => Resolve("Warning", Color.FromArgb("#F59E0B")),
        _ => Resolve("Warning", Color.FromArgb("#F59E0B"))
    };

    private static Color Resolve(string keyName, Color fallback) =>
        Application.Current?.Resources.TryGetValue(keyName, out var v) == true && v is Color c ? c : fallback;

    partial void OnStateChanged(ServiceState value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsNotInstalled));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStopOrRestart));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StatusColor));
    }

    partial void OnCanControlChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStopOrRestart));
    }

    /// <summary>Hide the buttons during a running action and show the progress.</summary>
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStopOrRestart));
        OnPropertyChanged(nameof(BusyText));
    }

    /// <summary>Label next to the spinner, depending on the currently running action.</summary>
    public string BusyText => _runningAction switch
    {
        ServiceControlAction.Start => "Starting…",
        ServiceControlAction.Stop => "Stopping…",
        ServiceControlAction.Restart => "Restarting…",
        _ => "Please wait…"
    };

    private ServiceControlAction? _runningAction;

    [RelayCommand]
    private Task Start() => Run(ServiceControlAction.Start);

    [RelayCommand]
    private Task Stop() => Run(ServiceControlAction.Stop);

    [RelayCommand]
    private Task Restart() => Run(ServiceControlAction.Restart);

    private async Task Run(ServiceControlAction action)
    {
        if (IsBusy) return;
        _runningAction = action;
        IsBusy = true;
        try
        {
            await _control(Key, action);
        }
        finally
        {
            IsBusy = false;
            _runningAction = null;
        }
    }
}
