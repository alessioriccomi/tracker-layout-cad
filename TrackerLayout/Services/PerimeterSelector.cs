using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using TrackerLayout.Models;

namespace TrackerLayout.Services;

/// <summary>
/// Gestisce la selezione interattiva del perimetro di campo da parte dell'utente.
/// Accetta Polyline (lwpolyline) e Polyline2d chiuse.
/// </summary>
public class PerimeterSelector(Editor ed)
{
    private readonly Editor _ed = ed;

    /// <summary>
    /// Chiede all'utente di selezionare un'entità e la valida come poligono chiuso.
    /// Restituisce null se l'utente annulla o l'entità non è valida.
    /// </summary>
    public PerimeterData? SelectPerimeter()
    {
        var opts = new PromptEntityOptions(
            "\nSeleziona il perimetro del campo (polilinea chiusa): ")
        {
            AllowNone = false
        };
        opts.SetRejectMessage("\nSelezionare una Polyline o Polyline2d chiusa.");
        opts.AddAllowedClass(typeof(Polyline),   exactMatch: false);
        opts.AddAllowedClass(typeof(Polyline2d), exactMatch: false);

        var result = _ed.GetEntity(opts);
        if (result.Status != PromptStatus.OK)
            return null;

        var db = _ed.Document.Database;
        using var tr = db.TransactionManager.StartOpenCloseTransaction();

        var entity = (Entity)tr.GetObject(result.ObjectId, OpenMode.ForRead);

        return entity switch
        {
            Polyline lwp   => ExtractFromLwPolyline(lwp,   result.ObjectId),
            Polyline2d p2d => ExtractFromPolyline2d(p2d, tr, result.ObjectId),
            _              => null
        };
    }

    // ── Estrazione vertici da LWPolyline ─────────────────────────────────────

    private PerimeterData? ExtractFromLwPolyline(Polyline lwp, ObjectId id)
    {
        if (!lwp.Closed)
        {
            _ed.WriteMessage("\nLa polilinea selezionata non è chiusa. Operazione annullata.");
            return null;
        }

        var vertices = Enumerable
            .Range(0, lwp.NumberOfVertices)
            .Select(i => new Point2d(lwp.GetPoint2dAt(i).X, lwp.GetPoint2dAt(i).Y))
            .ToList();

        _ed.WriteMessage($"\nPerimetro selezionato: {vertices.Count} vertici.");
        return new PerimeterData(id, vertices);
    }

    // ── Estrazione vertici da Polyline2d (legacy) ─────────────────────────────

    private PerimeterData? ExtractFromPolyline2d(
        Polyline2d p2d, Transaction tr, ObjectId id)
    {
        if (!p2d.Closed)
        {
            _ed.WriteMessage("\nLa polilinea selezionata non è chiusa. Operazione annullata.");
            return null;
        }

        var vertices = new List<Point2d>();
        foreach (ObjectId vId in p2d)
        {
            var v = (Vertex2d)tr.GetObject(vId, OpenMode.ForRead);
            vertices.Add(new Point2d(v.Position.X, v.Position.Y));
        }

        _ed.WriteMessage($"\nPerimetro selezionato: {vertices.Count} vertici.");
        return new PerimeterData(id, vertices);
    }
}
