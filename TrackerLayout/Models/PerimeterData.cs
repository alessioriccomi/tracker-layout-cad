using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TrackerLayout.Models;

/// <summary>
/// Dati del perimetro selezionato dall'utente.
/// Contiene sia l'ObjectId dell'entità AutoCAD sia la polilinea 2D
/// ricavata per i calcoli geometrici di contenimento.
/// </summary>
public class PerimeterData
{
    /// <summary>ObjectId dell'entità poligono nel database AutoCAD.</summary>
    public ObjectId EntityId { get; init; }

    /// <summary>
    /// Vertici del perimetro in coordinate mondo (UCS corrente applicato).
    /// La lista è chiusa: l'ultimo punto è distinto dal primo.
    /// </summary>
    public List<Point2d> Vertices { get; init; } = [];

    /// <summary>
    /// Bounding box axis-aligned del perimetro,
    /// calcolato dai vertici al momento della costruzione.
    /// </summary>
    public Extents2d BoundingBox { get; init; }

    public PerimeterData(ObjectId id, IEnumerable<Point2d> vertices)
    {
        EntityId = id;
        Vertices = [.. vertices];

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var v in Vertices)
        {
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
        }

        BoundingBox = new Extents2d(minX, minY, maxX, maxY);
    }
}
