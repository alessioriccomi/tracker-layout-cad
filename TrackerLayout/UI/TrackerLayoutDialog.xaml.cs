using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TrackerLayout.Models;
using TrackerLayout.UI.ViewModels;

namespace TrackerLayout.UI;

public partial class TrackerLayoutDialog : Window
{
    private readonly TrackerParametersViewModel _vm;

    public TrackerLayoutDialog(TrackerParameters? initialValues = null)
    {
        InitializeComponent();

        _vm = new TrackerParametersViewModel();

        if (initialValues is not null)
        {
            _vm.NumberOfPanels      = initialValues.NumberOfPanels.ToString();
            _vm.PanelWidth          = initialValues.PanelWidth.ToString("G", CultureInfo.InvariantCulture);
            _vm.PanelHeight         = initialValues.PanelHeight.ToString("G", CultureInfo.InvariantCulture);
            _vm.PanelTypeIndex      = initialValues.PanelType == PanelType.TwoP ? 1 : 0;
            _vm.PanelOrientationIndex = initialValues.PanelOrientation == PanelOrientation.Transverse ? 1 : 0;
            _vm.Pitch               = initialValues.Pitch.ToString("G", CultureInfo.InvariantCulture);
            _vm.TrackerHubHeight    = initialValues.TrackerHubHeight.ToString("G", CultureInfo.InvariantCulture);
            _vm.AzimuthDegrees      = initialValues.AzimuthDegrees.ToString("G", CultureInfo.InvariantCulture);
            _vm.ManeuverMarginNS    = initialValues.ManeuverMarginNS.ToString("G", CultureInfo.InvariantCulture);
            _vm.LateralMarginEW     = initialValues.LateralMarginEW.ToString("G", CultureInfo.InvariantCulture);
            _vm.MaxSlopeDegrees     = initialValues.MaxSlopeDegrees.ToString("G", CultureInfo.InvariantCulture);
        }

        DataContext = _vm;
    }

    public TrackerParameters? Result { get; private set; }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_vm.HasErrors) return;
        Result = _vm.ToModel();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }
}

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class BoolNegateConverter : IValueConverter
{
    public static readonly BoolNegateConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
