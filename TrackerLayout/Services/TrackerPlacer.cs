using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using TrackerLayout.Helpers;
using TrackerLayout.Models;

namespace TrackerLayout.Services;

public class TrackerPlacer(Database db, Transaction tr, Editor ed)
{
    private readonly Database    _db = db;
    private readonly Transaction _tr = tr;
    private readonly Editor      _ed = ed;

    public int PlaceTrackers(PerimeterData perimeter, TrackerParameters p)
    {
        BlockHelper.EnsureLayer(_db, _tr);
        BlockHelper.PurgeTrackerBlock(_db, _tr);       // elimina vecchi tracker e blocco precedente
        BlockHelper.EnsureBlockDefinition(_db, _tr, p); // ricrea con geometria aggiornata

        var terrain = new TerrainInterpolator();
        terrain.LoadFromDatabase(_db, _tr);
        if (terrain.PointCount == 0)
            _ed.WriteMessage("\nATTENZIONE: nessun punto 3D trovato — quota Z = 0 per tutti i tracker." +
                             "\n            Aggiungere DBPoint o Polilinee 3D con quote reali.");
        else
            _ed.WriteMessage($"\n[DBG] Punti terreno caricati: {terrain.PointCount}  " +
                             $"(Z range: {terrain.ZMin:F2} – {terrain.ZMax:F2} m)");

        _ed.WriteMessage($"\n[DBG] Lunghezza tracker={p.TrackerLength:F2} m  " +
                         $"Larghezza tracker={p.TrackerWidth:F2} m  " +
                         $"Pitch={p.Pitch} m  Hub={p.TrackerHubHeight} m");

        // ── Vettori di direzione ─────────────────────────────────────────────
        var (axX, axY) = p.AxisDirection;   // lungo l'asse tracker
        var (rwX, rwY) = p.RowDirection;    // perpendicolare (direzione delle file)

        _ed.WriteMessage($"\n[DBG] Azimut={p.AzimuthDegrees}°  " +
                         $"AxisDir=({axX:F4},{axY:F4})  RowDir=({rwX:F4},{rwY:F4})");

        // ── Proiezione vertici nel sistema locale ────────────────────────────
        var localVerts = perimeter.Vertices
            .Select(pt => (U: rwX * pt.X + rwY * pt.Y,
                           V: axX * pt.X + axY * pt.Y))
            .ToList();

        double uMin = localVerts.Min(v => v.U);
        double uMax = localVerts.Max(v => v.U);
        double vMin = localVerts.Min(v => v.V);
        double vMax = localVerts.Max(v => v.V);

        _ed.WriteMessage($"\n[DBG] Bounding box locale:" +
                         $"  U=[{uMin:F2}, {uMax:F2}]  ampiezza={uMax - uMin:F2} m" +
                         $"  V=[{vMin:F2}, {vMax:F2}]  ampiezza={vMax - vMin:F2} m");

        // ── Applicazione fasce ───────────────────────────────────────────────
        double uStart = uMin + p.LateralMarginEW;
        double uEnd   = uMax - p.LateralMarginEW;
        double vStart = vMin + p.ManeuverMarginNS;
        double vEnd   = vMax - p.ManeuverMarginNS;

        _ed.WriteMessage($"\n[DBG] Area netta: U=[{uStart:F2},{uEnd:F2}]  V=[{vStart:F2},{vEnd:F2}]");

        if (uStart >= uEnd)
        {
            _ed.WriteMessage($"\nERRORE: larghezza netta {uEnd - uStart:F2} m insufficiente per le carraie E/O.");
            return 0;
        }
        if (vStart >= vEnd)
        {
            _ed.WriteMessage($"\nERRORE: profondità netta {vEnd - vStart:F2} m insufficiente per le fasce N/S.");
            return 0;
        }

        double halfLen = p.TrackerLength / 2.0;
        if (vEnd - vStart < p.TrackerLength)
        {
            _ed.WriteMessage($"\nERRORE: area netta in direzione asse ({vEnd - vStart:F2} m) " +
                             $"< lunghezza tracker ({p.TrackerLength:F2} m).");
            return 0;
        }

        // Pendenza massima in radianti (0 = nessun filtro)
        double maxSlopeRad = p.MaxSlopeDegrees > 0.0
            ? p.MaxSlopeDegrees * Math.PI / 180.0
            : double.MaxValue;

        // ── Diagnostica attesa ───────────────────────────────────────────────
        int expectedRows = (int)Math.Floor((uEnd - uStart) / p.Pitch) + 1;
        int expectedCols = (int)Math.Floor((vEnd - vStart) / p.TrackerLength);
        _ed.WriteMessage($"\n[DBG] File attese ≈ {expectedRows}  Tracker per fila ≈ {expectedCols}  " +
                         $"Totale teorico ≈ {expectedRows * expectedCols}");
        if (p.MaxSlopeDegrees > 0)
            _ed.WriteMessage($"\n[DBG] Filtro pendenza attivo: max {p.MaxSlopeDegrees:F1}°");

        // ── Scansione ────────────────────────────────────────────────────────
        int count      = 0;
        int skippedSlope = 0;
        int rowId      = 0;
        // Rotazione blocco: azimut da Nord (CW) → angolo AutoCAD da X+ (CCW)
        // Derivazione: il blocco è disegnato con asse lungo Y locale.
        // Dopo rotazione θ, Y locale → (−sin θ, cos θ) nel mondo.
        // Vogliamo che corrisponda a AxisDirection = (sin az, cos az).
        // Quindi: −sin θ = sin az e cos θ = cos az → θ = −az.
        double rotRad  = -p.AzimuthRadians;

        for (double u = uStart; u <= uEnd + 1e-9; u += p.Pitch)
        {
            rowId++;
            int colId = 0;
            double v  = vStart + halfLen;

            while (v + halfLen <= vEnd + 1e-9)
            {
                double cx = rwX * u + axX * v;
                double cy = rwY * u + axY * v;

                var ctr  = new Point2d(cx, cy);
                var end1 = new Point2d(cx + axX * halfLen, cy + axY * halfLen);
                var end2 = new Point2d(cx - axX * halfLen, cy - axY * halfLen);

                if (IsInsidePolygon(ctr,  perimeter.Vertices) &&
                    IsInsidePolygon(end1, perimeter.Vertices) &&
                    IsInsidePolygon(end2, perimeter.Vertices))
                {
                    // ── Filtro pendenza ──────────────────────────────────────
                    double roll = terrain.ComputeTrackerRoll(cx, cy, halfLen, p.AxisDirection);
                    if (Math.Abs(roll) > maxSlopeRad)
                    {
                        skippedSlope++;
                        v += p.TrackerLength;
                        continue;
                    }

                    colId++;
                    double z = terrain.InterpolateZ(cx, cy);

                    BlockHelper.InsertTracker(_db, _tr,
                        new Point3d(cx, cy, z + p.TrackerHubHeight), rotRad, roll, rowId, colId);
                    count++;
                }

                v += p.TrackerLength;
            }
        }

        // ── Diagnostica finale ───────────────────────────────────────────────
        if (skippedSlope > 0)
            _ed.WriteMessage($"\n[DBG] Tracker scartati per pendenza > {p.MaxSlopeDegrees:F1}°: {skippedSlope}");

        if (count == 0)
        {
            _ed.WriteMessage("\n[DBG] Nessun tracker inserito. Possibili cause:");
            _ed.WriteMessage("\n      1) I punti centro/estremi cadono fuori dal poligono.");
            _ed.WriteMessage("\n      2) Le unità del disegno non sono metri (verifica con UNITS).");
            _ed.WriteMessage("\n      3) La pendenza supera il limite impostato ovunque.");

            double uTest = uStart;
            double vTest = vStart + halfLen;
            double cxT   = rwX * uTest + axX * vTest;
            double cyT   = rwY * uTest + axY * vTest;
            bool   inT   = IsInsidePolygon(new Point2d(cxT, cyT), perimeter.Vertices);
            _ed.WriteMessage($"\n[DBG] Primo centro testato: ({cxT:F3}, {cyT:F3})  dentro={inT}");
            _ed.WriteMessage($"\n[DBG] Primo vertice perimetro: ({perimeter.Vertices[0].X:F3}, {perimeter.Vertices[0].Y:F3})");
        }

        return count;
    }

    // ── Ray casting ──────────────────────────────────────────────────────────

    private static bool IsInsidePolygon(Point2d pt, List<Point2d> polygon)
    {
        bool inside = false;
        int  n      = polygon.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = polygon[i].X, yi = polygon[i].Y;
            double xj = polygon[j].X, yj = polygon[j].Y;

            bool crosses = (yi > pt.Y) != (yj > pt.Y) &&
                           pt.X < (xj - xi) * (pt.Y - yi) / (yj - yi) + xi;

            if (crosses) inside = !inside;
        }

        return inside;
    }
}
