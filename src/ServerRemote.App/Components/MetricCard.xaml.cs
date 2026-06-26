using ServerRemote.App.Models;

namespace ServerRemote.App.Components;

public partial class MetricCard : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(MetricCard), string.Empty);

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(MetricCard), string.Empty);

    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(MetricCard), string.Empty);

    public static readonly BindableProperty StatusProperty =
        BindableProperty.Create(nameof(Status), typeof(MetricStatus), typeof(MetricCard), MetricStatus.Normal,
            propertyChanged: (b, _, _) => ((MetricCard)b).OnPropertyChanged(nameof(StatusLabel)));

    public static readonly BindableProperty ShowStatusProperty =
        BindableProperty.Create(nameof(ShowStatus), typeof(bool), typeof(MetricCard), true);

    public static readonly BindableProperty ShowProgressProperty =
        BindableProperty.Create(nameof(ShowProgress), typeof(bool), typeof(MetricCard), false);

    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(MetricCard), 0.0,
            propertyChanged: (b, _, _) => ((MetricCard)b).OnPropertyChanged(nameof(ProgressColumns)));

    public MetricCard() => InitializeComponent();

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public MetricStatus Status { get => (MetricStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public bool ShowStatus { get => (bool)GetValue(ShowStatusProperty); set => SetValue(ShowStatusProperty, value); }
    public bool ShowProgress { get => (bool)GetValue(ShowProgressProperty); set => SetValue(ShowProgressProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public string StatusLabel => Status.ToLabel();

    /// <summary>Two star columns (filled / empty) that represent the progress as a percentage.</summary>
    public ColumnDefinitionCollection ProgressColumns
    {
        get
        {
            var p = Math.Clamp(Progress, 0, 1);
            return new ColumnDefinitionCollection(
                new ColumnDefinition(new GridLength(p, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1 - p, GridUnitType.Star)));
        }
    }
}
