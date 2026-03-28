using System.Diagnostics;
using System.Windows.Interop;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;
using TrackerLayout.Models;
using TrackerLayout.Services;
using TrackerLayout.UI;

// Registra la classe comando presso il motore AutoCAD al momento del NETLOAD
[assembly: CommandClass(typeof(TrackerLayout.Commands.TrackerLayoutCommand))]

namespace TrackerLayout.Commands;

/// <summary>
/// Punto di ingresso del plugin TrackerLayout.
/// Implementa IExtensionApplication per eseguire codice a caricamento/scaricamento.
/// </summary>
public class TrackerLayoutCommand : IExtensionApplication
{
    // ── Parametri dell'ultima sessione (riempiono i campi alla riapertura) ───
    private static TrackerParameters? _lastParameters;

    // ── IExtensionApplication ────────────────────────────────────────────────

    public void Initialize()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        doc?.Editor.WriteMessage(
            "\n[TrackerLayout] Plugin caricato. Usa il comando TRACKER_LAYOUT per avviare.");
    }

    public void Terminate() { }

    // ── Comando principale ───────────────────────────────────────────────────

    [CommandMethod("TRACKER_LAYOUT", CommandFlags.Modal)]
    public void RunTrackerLayout()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;
        var db = doc.Database;

        ed.WriteMessage("\n[TrackerLayout] Agrivoltaico Monoassiale");

        // ── Step 1: dialogo WPF parametri ────────────────────────────────────
        // WindowInteropHelper aggancia la finestra WPF alla main window di AutoCAD
        // (gestione corretta di Z-order e modalità) senza dipendere da AcMgd.dll.
        var dialog = new TrackerLayoutDialog(_lastParameters);
        var helper = new WindowInteropHelper(dialog)
        {
            Owner = Process.GetCurrentProcess().MainWindowHandle
        };
        bool? confirmed = dialog.ShowDialog();

        if (confirmed != true || dialog.Result is null)
        {
            ed.WriteMessage("\nOperazione annullata.");
            return;
        }

        var parameters = dialog.Result;
        _lastParameters = parameters;

        ed.WriteMessage($"\n  Pannelli={parameters.NumberOfPanels}  " +
                        $"Dim={parameters.PanelWidth:F3}x{parameters.PanelHeight:F3} m  " +
                        $"Tipo={parameters.PanelType}  Disp={parameters.PanelOrientation}");
        ed.WriteMessage($"\n  Tracker: L={parameters.TrackerLength:F2} m  W={parameters.TrackerWidth:F2} m  " +
                        $"Hub={parameters.TrackerHubHeight} m");
        ed.WriteMessage($"\n  Pitch={parameters.Pitch} m  |  Azimut={parameters.AzimuthDegrees}°  |  " +
                        $"N/S={parameters.ManeuverMarginNS} m  |  E/O={parameters.LateralMarginEW} m  |  " +
                        $"PendenzaMax={parameters.MaxSlopeDegrees}°");

        // ── Step 2: selezione perimetro ──────────────────────────────────────
        var selector = new PerimeterSelector(ed);
        var perimeter = selector.SelectPerimeter();

        if (perimeter is null)
        {
            ed.WriteMessage("\nOperazione annullata.");
            return;
        }

        // ── Step 3: posizionamento tracker ───────────────────────────────────
        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            var placer = new TrackerPlacer(db, tr, ed);
            int count = placer.PlaceTrackers(perimeter, parameters);

            tr.Commit();
            ed.WriteMessage($"\n[TrackerLayout] Completato: {count} tracker inseriti nel layer TRACKER.");
        }
        catch (System.Exception ex)
        {
            tr.Abort();
            ed.WriteMessage($"\n[TrackerLayout] ERRORE: {ex.Message}");
        }
    }
}
