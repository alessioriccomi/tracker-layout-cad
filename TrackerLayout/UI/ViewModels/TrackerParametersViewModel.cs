using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using TrackerLayout.Models;

namespace TrackerLayout.UI.ViewModels;

/// <summary>
/// ViewModel per la finestra di dialogo parametri.
/// Usa IDataErrorInfo per la validazione in-binding (nessun framework esterno).
/// </summary>
public class TrackerParametersViewModel : INotifyPropertyChanged, IDataErrorInfo
{
    public TrackerParametersViewModel()
    {
        ConfirmCommand = new RelayCommand(
            execute:    () => { },
            canExecute: () => !HasErrors);
    }

    // ── Pannelli ─────────────────────────────────────────────────────────────

    private string _numberOfPanels = "28";
    public string NumberOfPanels
    {
        get => _numberOfPanels;
        set { _numberOfPanels = value; NotifyAll(); }
    }

    private string _panelWidth = "1.134";
    public string PanelWidth
    {
        get => _panelWidth;
        set { _panelWidth = value; NotifyAll(); }
    }

    private string _panelHeight = "2.256";
    public string PanelHeight
    {
        get => _panelHeight;
        set { _panelHeight = value; NotifyAll(); }
    }

    // 0 = 1P, 1 = 2P
    private int _panelTypeIndex = 0;
    public int PanelTypeIndex
    {
        get => _panelTypeIndex;
        set { _panelTypeIndex = value; NotifyAll(); }
    }

    // 0 = Longitudinale, 1 = Trasversale
    private int _panelOrientationIndex = 0;
    public int PanelOrientationIndex
    {
        get => _panelOrientationIndex;
        set { _panelOrientationIndex = value; NotifyAll(); }
    }

    // ── Geometria impianto ───────────────────────────────────────────────────

    private string _pitch = "7.0";
    public string Pitch
    {
        get => _pitch;
        set { _pitch = value; NotifyAll(); }
    }

    private string _trackerHubHeight = "1.5";
    public string TrackerHubHeight
    {
        get => _trackerHubHeight;
        set { _trackerHubHeight = value; NotifyAll(); }
    }

    private string _azimuthDegrees = "180";
    public string AzimuthDegrees
    {
        get => _azimuthDegrees;
        set { _azimuthDegrees = value; NotifyAll(); }
    }

    // ── Fasce ────────────────────────────────────────────────────────────────

    private string _maneuverMarginNS = "4.0";
    public string ManeuverMarginNS
    {
        get => _maneuverMarginNS;
        set { _maneuverMarginNS = value; NotifyAll(); }
    }

    private string _lateralMarginEW = "4.0";
    public string LateralMarginEW
    {
        get => _lateralMarginEW;
        set { _lateralMarginEW = value; NotifyAll(); }
    }

    // ── Pendenza massima ─────────────────────────────────────────────────────

    private string _maxSlopeDegrees = "0";
    public string MaxSlopeDegrees
    {
        get => _maxSlopeDegrees;
        set { _maxSlopeDegrees = value; NotifyAll(); }
    }

    // ── Proprietà calcolate (visualizzazione) ────────────────────────────────

    public double AzimuthDouble =>
        TryParsePositive(_azimuthDegrees, requireNonZero: false, out var d) ? d % 360.0 : 180.0;

    public string TrackerLengthInfo
    {
        get
        {
            if (!TryParseInt(_numberOfPanels, out int n) || n <= 0) return "—";
            bool longitudinal = _panelOrientationIndex == 0;
            if (longitudinal)
            {
                if (!TryParsePositive(_panelHeight, requireNonZero: true, out double h)) return "—";
                return $"{n * h:F2} m";
            }
            else
            {
                if (!TryParsePositive(_panelWidth, requireNonZero: true, out double w)) return "—";
                return $"{n * w:F2} m";
            }
        }
    }

    public string TrackerWidthInfo
    {
        get
        {
            bool longitudinal = _panelOrientationIndex == 0;
            bool twoP         = _panelTypeIndex == 1;

            if (longitudinal)
            {
                if (!TryParsePositive(_panelWidth, requireNonZero: true, out double w)) return "—";
                return twoP ? $"{2 * w:F2} m" : $"{w:F2} m";
            }
            else
            {
                if (!TryParsePositive(_panelHeight, requireNonZero: true, out double h)) return "—";
                return twoP ? $"{2 * h:F2} m" : $"{h:F2} m";
            }
        }
    }

    // ── Comando conferma ─────────────────────────────────────────────────────

    public ICommand ConfirmCommand { get; }

    // ── Validazione ──────────────────────────────────────────────────────────

    public bool HasErrors =>
        !string.IsNullOrEmpty(this[nameof(NumberOfPanels)])   ||
        !string.IsNullOrEmpty(this[nameof(PanelWidth)])       ||
        !string.IsNullOrEmpty(this[nameof(PanelHeight)])      ||
        !string.IsNullOrEmpty(this[nameof(Pitch)])            ||
        !string.IsNullOrEmpty(this[nameof(TrackerHubHeight)]) ||
        !string.IsNullOrEmpty(this[nameof(AzimuthDegrees)])   ||
        !string.IsNullOrEmpty(this[nameof(ManeuverMarginNS)]) ||
        !string.IsNullOrEmpty(this[nameof(LateralMarginEW)])  ||
        !string.IsNullOrEmpty(this[nameof(MaxSlopeDegrees)]);

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(NumberOfPanels)   => ValidateInt(NumberOfPanels,       "N° pannelli"),
        nameof(PanelWidth)       => ValidatePositive(PanelWidth,       "Larghezza pannello"),
        nameof(PanelHeight)      => ValidatePositive(PanelHeight,      "Altezza pannello"),
        nameof(Pitch)            => ValidatePositive(Pitch,            "Pitch"),
        nameof(TrackerHubHeight) => ValidatePositive(TrackerHubHeight, "Altezza hub"),
        nameof(AzimuthDegrees)   => ValidateAzimuth(AzimuthDegrees),
        nameof(ManeuverMarginNS) => ValidatePositive(ManeuverMarginNS, "Margine N/S"),
        nameof(LateralMarginEW)  => ValidatePositive(LateralMarginEW,  "Carraie E/O"),
        nameof(MaxSlopeDegrees)  => ValidateNonNegative(MaxSlopeDegrees, "Pendenza max"),
        _                        => string.Empty
    };

    // ── Conversione in modello ───────────────────────────────────────────────

    public TrackerParameters ToModel() => new()
    {
        NumberOfPanels      = int.Parse(_numberOfPanels.Trim()),
        PanelWidth          = ParseInvariant(_panelWidth),
        PanelHeight         = ParseInvariant(_panelHeight),
        PanelType           = _panelTypeIndex == 1 ? PanelType.TwoP : PanelType.OneP,
        PanelOrientation    = _panelOrientationIndex == 1 ? PanelOrientation.Transverse : PanelOrientation.Longitudinal,
        Pitch               = ParseInvariant(_pitch),
        TrackerHubHeight    = ParseInvariant(_trackerHubHeight),
        AzimuthDegrees      = ParseInvariant(_azimuthDegrees) % 360.0,
        ManeuverMarginNS    = ParseInvariant(_maneuverMarginNS),
        LateralMarginEW     = ParseInvariant(_lateralMarginEW),
        MaxSlopeDegrees     = ParseInvariant(_maxSlopeDegrees),
    };

    // ── Validazione privata ───────────────────────────────────────────────────

    private static string ValidatePositive(string text, string fieldName)
    {
        if (!TryParsePositive(text, requireNonZero: true, out _))
            return $"{fieldName} deve essere un numero > 0.";
        return string.Empty;
    }

    private static string ValidateNonNegative(string text, string fieldName)
    {
        if (!TryParsePositive(text, requireNonZero: false, out _))
            return $"{fieldName} deve essere un numero ≥ 0.";
        return string.Empty;
    }

    private static string ValidateInt(string text, string fieldName)
    {
        if (!TryParseInt(text, out int v) || v <= 0)
            return $"{fieldName} deve essere un intero > 0.";
        return string.Empty;
    }

    private static string ValidateAzimuth(string text)
    {
        if (!TryParsePositive(text, requireNonZero: false, out var d))
            return "Azimut deve essere un numero ≥ 0.";
        if (d < 0 || d >= 360)
            return "Azimut deve essere compreso tra 0° e 359.9°.";
        return string.Empty;
    }

    private static bool TryParsePositive(string text, bool requireNonZero, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ok = double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
               || double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture,   out value);

        if (!ok) return false;
        if (requireNonZero && value <= 0) return false;
        if (!requireNonZero && value < 0) return false;
        return true;
    }

    private static bool TryParseInt(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return int.TryParse(text.Trim(), out value);
    }

    private static double ParseInvariant(string text)
    {
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture,   out v))     return v;
        return 0;
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void NotifyAll()
    {
        Notify(nameof(NumberOfPanels));
        Notify(nameof(PanelWidth));
        Notify(nameof(PanelHeight));
        Notify(nameof(PanelTypeIndex));
        Notify(nameof(PanelOrientationIndex));
        Notify(nameof(Pitch));
        Notify(nameof(TrackerHubHeight));
        Notify(nameof(AzimuthDegrees));
        Notify(nameof(ManeuverMarginNS));
        Notify(nameof(LateralMarginEW));
        Notify(nameof(MaxSlopeDegrees));
        Notify(nameof(HasErrors));
        Notify(nameof(AzimuthDouble));
        Notify(nameof(TrackerLengthInfo));
        Notify(nameof(TrackerWidthInfo));
    }
}

// ── RelayCommand ─────────────────────────────────────────────────────────────

internal sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? _) => canExecute?.Invoke() ?? true;
    public void Execute(object? _)    => execute();

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
