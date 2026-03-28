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
///   - SubDMesh         → vertici aggiunti all'IDW cloud
///
/// Algoritmo:
///   1. Se il punto cade dentro un triangolo della mesh → interpolazione baricentrica (precisa)
///      Accelerata da griglia spaziale 2D — O(1) medio invece di O(n)
///   2. Altrimenti → IDW (Inverse Distance Weighting) dai k punti più vicini
/// </summary>
public class TerrainInterpolator
{
    // Punti per IDW (cloud di punti)
    private readonly List<Point3d> _points = [];

    // Triangoli della mesh per interpolazione baricentrica
    private readonly List<(Point3d A, Point3d B, Point3d C)> _triangles = [];

    // Griglia spaziale per accelerare la ricerca triangoli
    private double   _gridMinX, _gridMinY, _gridCellSize;
    private int      _gridCols, _gridRows;
    private List<int>[]? _triGrid;  // per ogni cella: indici in _triangles

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
        _triGrid = null;

        var modelSpace = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            Entity entity;
            try { entity = (Entity)tr.GetObject(id, OpenMode.ForRead); }
            catch { continue; }   // entità non apribile (layer bloccato, proxy, ecc.)

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

                // ── SubDMesh — mesh moderna AutoCAD ──────────────────────────
                case SubDMesh sdm:
                    try
                    {
                        var verts = sdm.Vertices;
                        foreach (Point3d vtx in verts)
                            _points.Add(vtx);
                    }
                    catch { /* mesh non accessibile — skip */ }
                    break;

                // ── 3DFACE (mesh del terreno) ─────────────────────────────────
                // API AutoCAD: GetVertexAt usa indici 0-based (0, 1, 2, 3).
                // Ogni Face ha 4 vertici; se è un triangolo v3 == v2.
                case Face face:
                    try
                    {
                        Point3d v0 = face.GetVertexAt(0);
                        Point3d v1 = face.GetVertexAt(1);
                        Point3d v2 = face.GetVertexAt(2);
                        Point3d v3 = face.GetVertexAt(3);

                        _points.Add(v0);
                        _points.Add(v1);
                        _points.Add(v2);

                        _triangles.Add((A: v0, B: v1, C: v2));

                        if (v3.DistanceTo(v2) > 1e-6)
                        {
                            _points.Add(v3);
                            _triangles.Add((A: v2, B: v3, C: v0));
                        }
                    }
                    catch { /* topologia invalida — skip */ }
                    break;
            }
        }

        // Costruisce la griglia spaziale dopo aver caricato tutti i triangoli
        if (_triangles.Count > 0)
            BuildTriangleGrid();
    }

    // ── Griglia spaziale per triangoli ──────────────────────────────────────

    private void BuildTriangleGrid()
    {
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        foreach (var tri in _triangles)
        {
            xMin = Math.Min(xMin, Math.Min(tri.A.X, Math.Min(tri.B.X, tri.C.X)));
            xMax = Math.Max(xMax, Math.Max(tri.A.X, Math.Max(tri.B.X, tri.C.X)));
            yMin = Math.Min(yMin, Math.Min(tri.A.Y, Math.Min(tri.B.Y, tri.C.Y)));
            yMax = Math.Max(yMax, Math.Max(tri.A.Y, Math.Max(tri.B.Y, tri.C.Y)));
        }

        double dx = xMax - xMin;
        double dy = yMax - yMin;
        if (dx < 1e-6 || dy < 1e-6) return;

        // ~50 triangoli per cella in media
        int targetCells = Math.Max(4, _triangles.Count / 50);
        double aspect   = dx / dy;
        _gridCols = Math.Max(1, (int)Math.Sqrt(targetCells * aspect));
        _gridRows = Math.Max(1, (int)Math.Sqrt(targetCells / aspect));

        // Cellsize leggermente più grande per evitare triangoli su bordi di cella
        _gridCellSize = Math.Max(dx / _gridCols, dy / _gridRows) * 1.01;
        _gridMinX = xMin;
        _gridMinY = yMin;

        _triGrid = new List<int>[_gridCols * _gridRows];
        for (int i = 0; i < _triGrid.Length; i++)
            _triGrid[i] = [];

        for (int ti = 0; ti < _triangles.Count; ti++)
        {
            var tri   = _triangles[ti];
            double txMin = Math.Min(tri.A.X, Math.Min(tri.B.X, tri.C.X));
            double txMax = Math.Max(tri.A.X, Math.Max(tri.B.X, tri.C.X));
            double tyMin = Math.Min(tri.A.Y, Math.Min(tri.B.Y, tri.C.Y));
            double tyMax = Math.Max(tri.A.Y, Math.Max(tri.B.Y, tri.C.Y));

            int cLo = GridCol(txMin), cHi = GridCol(txMax);
            int rLo = GridRow(tyMin), rHi = GridRow(tyMax);

            for (int r = rLo; r <= rHi; r++)
                for (int c = cLo; c <= cHi; c++)
                    _triGrid[r * _gridCols + c].Add(ti);
        }
    }

    private int GridCol(double x) =>
        Math.Clamp((int)((x - _gridMinX) / _gridCellSize), 0, _gridCols - 1);

    private int GridRow(double y) =>
        Math.Clamp((int)((y - _gridMinY) / _gridCellSize), 0, _gridRows - 1);

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

        // 1. Ricerca baricentrica accelerata dalla griglia
        if (_triGrid != null)
        {
            int col = GridCol(x);
            int row = GridRow(y);

            if (col >= 0 && col < _gridCols && row >= 0 && row < _gridRows)
            {
                foreach (int ti in _triGrid[row * _gridCols + col])
                {
                    var tri = _triangles[ti];
                    if (PointInTriangle2D(x, y, tri.A, tri.B, tri.C))
                        return BarycentricZ(x, y, tri.A, tri.B, tri.C);
                }
            }
        }
        else if (_triangles.Count > 0)
        {
            // Griglia non disponibile (< soglia): scan lineare
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
