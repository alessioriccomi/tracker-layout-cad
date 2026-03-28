namespace TrackerLayout.Models;

public enum PanelType { OneP, TwoP }
public enum PanelOrientation { Longitudinal, Transverse }

/// <summary>
/// Parametri di input raccolti dall'utente per il posizionamento dei tracker.
/// </summary>
public class TrackerParameters
{
    /// <summary>Distanza tra gli assi dei tracker (m).</summary>
    public double Pitch { get; set; } = 7.0;

    /// <summary>
    /// Orientamento azimutale dell'asse dei tracker in gradi da Nord geografico.
    /// 0° = Nord, 90° = Est, 180° = Sud, 270° = Ovest.
    /// </summary>
    public double AzimuthDegrees { get; set; } = 180.0;

    // ── Parametri pannelli ────────────────────────────────────────────────────

    /// <summary>Numero di pannelli lungo l'asse del tracker.</summary>
    public int NumberOfPanels { get; set; } = 28;

    /// <summary>Lato corto del pannello (m).</summary>
    public double PanelWidth { get; set; } = 1.134;

    /// <summary>Lato lungo del pannello (m).</summary>
    public double PanelHeight { get; set; } = 2.256;

    /// <summary>Tipologia: 1P = singolo ritratto, 2P = doppio ritratto affiancato.</summary>
    public PanelType PanelType { get; set; } = PanelType.OneP;

    /// <summary>
    /// Disposizione dei pannelli rispetto all'asse tracker.
    /// Longitudinal = lato lungo del pannello parallelo all'asse tracker.
    /// Transverse    = lato lungo del pannello perpendicolare all'asse tracker.
    /// </summary>
    public PanelOrientation PanelOrientation { get; set; } = PanelOrientation.Longitudinal;

    /// <summary>Altezza hub del tracker da terra (m).</summary>
    public double TrackerHubHeight { get; set; } = 1.5;

    /// <summary>Larghezza della fascia di manovra sui lati Nord e Sud (m).</summary>
    public double ManeuverMarginNS { get; set; } = 4.0;

    /// <summary>Larghezza delle carraie laterali sui lati Est e Ovest (m).</summary>
    public double LateralMarginEW { get; set; } = 4.0;

    /// <summary>
    /// Pendenza massima del terreno tollerata (°).
    /// I tracker il cui sito supera questa soglia vengono scartati.
    /// 0 = nessun limite (accetta qualsiasi pendenza).
    /// </summary>
    public double MaxSlopeDegrees { get; set; } = 0.0;

    // ── Calcolati a runtime ───────────────────────────────────────────────────

    /// <summary>
    /// Lunghezza del tracker lungo il proprio asse (m), calcolata dai parametri pannello.
    /// </summary>
    public double TrackerLength =>
        PanelOrientation == PanelOrientation.Longitudinal
            ? NumberOfPanels * PanelHeight
            : NumberOfPanels * PanelWidth;

    /// <summary>
    /// Larghezza del tracker perpendicolare al proprio asse (m), calcolata dai parametri pannello.
    /// </summary>
    public double TrackerWidth =>
        PanelOrientation == PanelOrientation.Longitudinal
            ? (PanelType == PanelType.OneP ? PanelWidth : 2.0 * PanelWidth)
            : (PanelType == PanelType.OneP ? PanelHeight : 2.0 * PanelHeight);

    /// <summary>Azimut in radianti.</summary>
    public double AzimuthRadians => AzimuthDegrees * Math.PI / 180.0;

    /// <summary>Vettore unitario nella direzione dell'asse tracker (piano XY).</summary>
    public (double X, double Y) AxisDirection =>
        (Math.Sin(AzimuthRadians), Math.Cos(AzimuthRadians));

    /// <summary>Vettore unitario perpendicolare all'asse tracker (direzione delle file).</summary>
    public (double X, double Y) RowDirection =>
        (-AxisDirection.Y, AxisDirection.X);
}
