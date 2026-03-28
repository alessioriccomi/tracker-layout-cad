using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TrackerLayout.Services;

/// <summary>
/// Interpola la quota Z in un punto XY qualsiasi a partire dai punti 3D
/// presenti nel disegno.
///
/// Entità lette (dalla radice del model space):
///   - DBPoint          → posizione 3D diretta
///   - Polyline3d       → ogni vertice della polilinea 3D
///   - Polyline (heavy) → ogni vertice se ha Z ≠ 0
///
/// Algoritmo: IDW (Inverse Distance Weighting) sui k punti più vicini.
/// </summary>
public class TerrainInterpolator
{
    private readonly List<Point3d> _terrainPoints = [];
    private const int    KNeighbours = 6;
    private const double PowerParam  = 2.0;

    public int    PointCount => _terrainPoints.Count;
    public double ZMin => _terrainPoints.Count > 0 ? _terrainPoints.Min(p => p.Z) : 0;
    public double ZMax => _terrainPoints.Count > 0 ? _terrainPoints.Max(p => p.Z) : 0;

    // ── Caricamento punti 3D dal database ────────────────────────────────────

    public void LoadFromDatabase(Database db, Transaction tr)
    {
        _terrainPoints.Clear();

        var modelSpace = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            var entity = tr.GetObject(id, OpenMode.ForRead);

            switch (entity)
            {
                // ── Punto AutoCAD (DBPoint) ──────────────────────────────────
                case DBPoint pt:
                    _terrainPoints.Add(pt.Position);
                    break;

                // ── Polilinea 3D ─────────────────────────────────────────────
                case Polyline3d p3d:
                    foreach (ObjectId vtxId in p3d)
                    {
                        var vtxObj = tr.GetObject(vtxId, OpenMode.ForRead);
                        if (vtxObj is PolylineVertex3d vtx)
                            _terrainPoints.Add(vtx.Position);
                    }
                    break;

                // ── Polilinea pesante con vertici 3D ─────────────────────────
                case Polyline pl:
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var pt2 = pl.GetPoint3dAt(i);
                        if (Math.Abs(pt2.Z) > 1e-6)   // ignora vertici a quota 0
                            _terrainPoints.Add(pt2);
                    }
                    break;
            }
        }
    }

    // ── Interpolazione IDW ───────────────────────────────────────────────────

    /// <summary>
    /// Restituisce la quota Z interpolata nel punto (x, y) tramite IDW.
    /// Se non ci sono punti caricati, restituisce 0.
    /// </summary>
    public double InterpolateZ(double x, double y)
    {
        if (_terrainPoints.Count == 0)
            return 0.0;

        var distances = _terrainPoints
            .Select(p => (
                Point: p,
                Dist2D: Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y))
            ))
            .OrderBy(d => d.Dist2D)
            .Take(KNeighbours)
            .ToList();

        if (distances[0].Dist2D < 1e-6)
            return distances[0].Point.Z;

        double weightedSum = 0.0;
        double weightTotal = 0.0;

        foreach (var (point, dist2D) in distances)
        {
            double w = 1.0 / Math.Pow(dist2D, PowerParam);
            weightedSum += w * point.Z;
            weightTotal += w;
        }

        return weightedSum / weightTotal;
    }

    /// <summary>
    /// Calcola l'angolo di inclinazione longitudinale (roll) del tracker
    /// interpolando Z ai due estremi. Restituisce radianti.
    /// Positivo = l'estremo in direzione +AxisDir è più alto.
    /// </summary>
    public double ComputeTrackerRoll(
        double cx, double cy,
        double halfLength,
        (double X, double Y) axisDir)
    {
        double z1 = InterpolateZ(cx - axisDir.X * halfLength, cy - axisDir.Y * halfLength);
        double z2 = InterpolateZ(cx + axisDir.X * halfLength, cy + axisDir.Y * halfLength);

        return Math.Atan2(z2 - z1, 2.0 * halfLength);
    }
}
