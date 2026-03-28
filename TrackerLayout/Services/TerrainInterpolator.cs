using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TrackerLayout.Services;

/// <summary>
/// Interpola la quota Z in un punto XY a partire dai dati terreno presenti nel disegno.
///
/// Entità lette dal model space:
///   - DBPoint          → punto quota diretto
///   - Polyline3d       → ogni vertice della polilinea 3D
///   - Polyline (heavy) → ogni vertice con Z ≠ 0
///   - Face (3DFACE)    → triangoli/quad della mesh; usa interpolazione baricentrica (esatta)
///
/// Algoritmo:
///   1. Se il punto cade dentro un triangolo della mesh → interpolazione baricentrica (precisa)
///   2. Altrimenti → IDW (Inverse Distance Weighting) dai k punti più vicini
/// </summary>
public class TerrainInterpolator
{
    // Punti per IDW (cloud di punti)
    private readonly List<Point3d> _points = [];

    // Triangoli della mesh per interpolazione baricentrica
    private readonly List<(Point3d A, Point3d B, Point3d C)> _triangles = [];

    private const int    KNeighbours = 6;
    private const double PowerParam  = 2.0;

    public int    PointCount    => _points.Count;
    public int    TriangleCount => _triangles.Count;
    public double ZMin => _points.Count > 0 ? _points.Min(p => p.Z) : 0;
    public double ZMax => _points.Count > 0 ? _points.Max(p => p.Z) : 0;

    // ── Caricamento dati terreno ─────────────────────────────────────────────

    public void LoadFromDatabase(Database db, Transaction tr)
    {
        _points.Clear();
        _triangles.Clear();

        var modelSpace = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            var entity = tr.GetObject(id, OpenMode.ForRead);

            switch (entity)
            {
                // ── Punto quota ──────────────────────────────────────────────
                case DBPoint pt:
                    _points.Add(pt.Position);
                    break;

                // ── Polilinea 3D (es. curve di livello 3D) ───────────────────
                case Polyline3d p3d:
                    foreach (ObjectId vtxId in p3d)
                    {
                        if (tr.GetObject(vtxId, OpenMode.ForRead) is PolylineVertex3d vtx)
                            _points.Add(vtx.Position);
                    }
                    break;

                // ── Polilinea pesante con vertici 3D ─────────────────────────
                case Polyline pl:
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var p3 = pl.GetPoint3dAt(i);
                        if (Math.Abs(p3.Z) > 1e-6)
                            _points.Add(p3);
                    }
                    break;

                // ── 3DFACE (mesh del terreno) ─────────────────────────────────
                // Ogni Face AutoCAD ha 4 vertici (o 3 se è un triangolo, dove il 4°
                // coincide con il 3°). Si aggiungono sia i vertici all'IDW cloud sia
                // i triangoli per l'interpolazione baricentrica precisa.
                // SubDMesh — mesh moderna AutoCAD (MESH command, CADmapper DXF, ecc.)
                // I vertici sono punti 3D; usiamo IDW (non baricentrico perché
                // la topologia delle facce richiede accesso agli indici).
                case SubDMesh sdm:
                    foreach (Point3d vtx in sdm.Vertices)
                        if (Math.Abs(vtx.Z) > 1e-6 || sdm.Vertices.Count < 4)
                            _points.Add(vtx);
                    break;

                // 3DFACE — indici 1..4 (API AutoCAD)
                case Face face:
                    Point3d v1 = face.GetVertexAt(1);
                    Point3d v2 = face.GetVertexAt(2);
                    Point3d v3 = face.GetVertexAt(3);
                    Point3d v4 = face.GetVertexAt(4);

                    _points.Add(v1);
                    _points.Add(v2);
                    _points.Add(v3);

                    // Triangolo principale
                    _triangles.Add((A: v1, B: v2, C: v3));

                    // Se quad (v4 ≠ v3) → aggiungi secondo triangolo
                    if (v4.DistanceTo(v3) > 1e-6)
                    {
                        _points.Add(v4);
                        _triangles.Add((A: v3, B: v4, C: v1));
                    }
                    break;
            }
        }
    }

    // ── Interpolazione quota Z ───────────────────────────────────────────────

    /// <summary>
    /// Restituisce la quota Z al punto (x, y):
    ///   - se il punto cade in un triangolo della mesh → interpolazione baricentrica
    ///   - altrimenti → IDW sui k punti più vicini
    ///   - se non ci sono dati → 0
    /// </summary>
    public double InterpolateZ(double x, double y)
    {
        if (_points.Count == 0 && _triangles.Count == 0)
            return 0.0;

        // 1. Prova l'interpolazione baricentrica sulla mesh
        if (_triangles.Count > 0)
        {
            foreach (var tri in _triangles)
            {
                if (PointInTriangle2D(x, y, tri.A, tri.B, tri.C))
                    return BarycentricZ(x, y, tri.A, tri.B, tri.C);
            }
        }

        // 2. Fallback: IDW dal cloud di punti
        if (_points.Count == 0) return 0.0;

        var nearest = _points
            .Select(p => (Point: p, D: Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y))))
            .OrderBy(d => d.D)
            .Take(KNeighbours)
            .ToList();

        if (nearest[0].D < 1e-6)
            return nearest[0].Point.Z;

        double wSum = 0, wTot = 0;
        foreach (var (p, d) in nearest)
        {
            double w = 1.0 / Math.Pow(d, PowerParam);
            wSum += w * p.Z;
            wTot += w;
        }
        return wSum / wTot;
    }

    /// <summary>
    /// Angolo di inclinazione longitudinale (roll) del tracker lungo il proprio asse.
    /// Positivo = l'estremo in direzione +AxisDir è più alto.
    /// </summary>
    public double ComputeTrackerRoll(
        double cx, double cy, double halfLength,
        (double X, double Y) axisDir)
    {
        double z1 = InterpolateZ(cx - axisDir.X * halfLength, cy - axisDir.Y * halfLength);
        double z2 = InterpolateZ(cx + axisDir.X * halfLength, cy + axisDir.Y * halfLength);
        return Math.Atan2(z2 - z1, 2.0 * halfLength);
    }

    // ── Geometria triangolo ──────────────────────────────────────────────────

    /// <summary>Test punto-in-triangolo nel piano XY (ignora Z).</summary>
    private static bool PointInTriangle2D(double px, double py,
        Point3d a, Point3d b, Point3d c)
    {
        double d1 = Cross2D(px, py, a.X, a.Y, b.X, b.Y);
        double d2 = Cross2D(px, py, b.X, b.Y, c.X, c.Y);
        double d3 = Cross2D(px, py, c.X, c.Y, a.X, a.Y);

        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static double Cross2D(double px, double py,
        double x1, double y1, double x2, double y2)
        => (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);

    /// <summary>
    /// Interpolazione baricentrica di Z dentro il triangolo ABC,
    /// per il punto (px, py) nel piano XY.
    /// </summary>
    private static double BarycentricZ(double px, double py,
        Point3d a, Point3d b, Point3d c)
    {
        double denom = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(denom) < 1e-12) return (a.Z + b.Z + c.Z) / 3.0;

        double w1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / denom;
        double w2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / denom;
        double w3 = 1.0 - w1 - w2;
        return w1 * a.Z + w2 * b.Z + w3 * c.Z;
    }
}
