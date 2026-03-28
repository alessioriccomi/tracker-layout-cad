using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TrackerLayout.Models;

namespace TrackerLayout.Helpers;

/// <summary>
/// Utilità per creare e gestire il blocco TRACKER nel database AutoCAD.
/// Il blocco mostra il perimetro del tracker e i rettangoli di ogni singolo pannello.
/// </summary>
public static class BlockHelper
{
    public const string BlockName = "TRACKER";
    public const string LayerName = "TRACKER";

    // ── Layer TRACKER ────────────────────────────────────────────────────────

    public static void EnsureLayer(Database db, Transaction tr)
    {
        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (layerTable.Has(LayerName)) return;

        var layer = new LayerTableRecord
        {
            Name  = LayerName,
            Color = Autodesk.AutoCAD.Colors.Color
                       .FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3)
        };
        layerTable.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, add: true);
    }

    // ── Pulizia blocchi esistenti ────────────────────────────────────────────

    /// <summary>
    /// Elimina dal model space tutte le BlockReference TRACKER già inserite,
    /// poi elimina la definizione del blocco.
    /// Chiamare PRIMA di EnsureBlockDefinition per ripartire da zero ad ogni run.
    /// </summary>
    public static void PurgeTrackerBlock(Database db, Transaction tr)
    {
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!blockTable.Has(BlockName)) return;

        // 1. Elimina tutte le istanze nel model space
        var modelSpace = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        foreach (ObjectId id in modelSpace.Cast<ObjectId>().ToList())
        {
            var obj = tr.GetObject(id, OpenMode.ForRead);
            if (obj is BlockReference br && br.Name == BlockName)
            {
                br.UpgradeOpen();
                // Colleziona prima, poi cancella — evita eInvalidIndex durante l'iterazione
                var attIds = br.AttributeCollection.Cast<ObjectId>().ToList();
                foreach (ObjectId attId in attIds)
                {
                    var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForWrite);
                    att.Erase();
                }
                br.Erase();
            }
        }

        // 2. Elimina le entità della definizione
        var btr = (BlockTableRecord)tr.GetObject(blockTable[BlockName], OpenMode.ForWrite);
        foreach (ObjectId id in btr.Cast<ObjectId>().ToList())
        {
            var obj = tr.GetObject(id, OpenMode.ForWrite);
            obj.Erase();
        }

        // 3. Elimina la definizione stessa
        btr.Erase();
    }

    // ── Definizione blocco ───────────────────────────────────────────────────

    /// <summary>
    /// Crea la definizione del blocco TRACKER con:
    ///   - contorno esterno del tracker
    ///   - asse centrale
    ///   - perimetro di ogni singolo pannello
    ///   - attributi (ROW_ID, COL_ID, ELEVATION, AZIMUTH)
    ///
    /// Il blocco è centrato nell'origine, asse lungo Y (lunghezza tracker),
    /// larghezza lungo X (larghezza tracker).
    /// </summary>
    public static ObjectId EnsureBlockDefinition(Database db, Transaction tr, TrackerParameters p)
    {
        double length = p.TrackerLength;
        double width  = p.TrackerWidth;
        double hw     = width  / 2.0;
        double hl     = length / 2.0;

        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        blockTable.UpgradeOpen();

        var btr   = new BlockTableRecord { Name = BlockName, Origin = Point3d.Origin };
        var btrId = blockTable.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, add: true);

        // ── Contorno esterno ─────────────────────────────────────────────────
        var outline = MakeRect(-hw, -hl, hw, hl);
        outline.Layer = LayerName;
        btr.AppendEntity(outline);
        tr.AddNewlyCreatedDBObject(outline, add: true);

        // ── Asse centrale ────────────────────────────────────────────────────
        var axis = new Line(new Point3d(0, -hl, 0), new Point3d(0, hl, 0));
        axis.Layer = LayerName;
        btr.AppendEntity(axis);
        tr.AddNewlyCreatedDBObject(axis, add: true);

        // ── Perimetri pannelli ───────────────────────────────────────────────
        DrawPanels(btr, tr, p, hw, hl);

        // ── Attributi ────────────────────────────────────────────────────────
        foreach (var att in new[]
        {
            MakeAttribute("ROW_ID",    "Riga",    new Point2d(-hw - 0.5,  0.0)),
            MakeAttribute("COL_ID",    "Colonna", new Point2d(-hw - 0.5, -0.8)),
            MakeAttribute("ELEVATION", "Quota Z", new Point2d(-hw - 0.5, -1.6)),
            MakeAttribute("AZIMUTH",   "Azimut",  new Point2d(-hw - 0.5, -2.4)),
        })
        {
            btr.AppendEntity(att);
            tr.AddNewlyCreatedDBObject(att, add: true);
        }

        return btrId;
    }

    // ── Disegno pannelli ─────────────────────────────────────────────────────

    private static void DrawPanels(
        BlockTableRecord btr, Transaction tr, TrackerParameters p,
        double hw, double hl)
    {
        int    n  = p.NumberOfPanels;
        double pw = p.PanelWidth;
        double ph = p.PanelHeight;
        bool   twoP = p.PanelType == PanelType.TwoP;

        if (p.PanelOrientation == PanelOrientation.Longitudinal)
        {
            // Pannelli impilati lungo Y, lato lungo (ph) parallelo all'asse Y.
            // Colonne: 1P → 1 colonna centrata; 2P → 2 colonne affiancate.
            int cols = twoP ? 2 : 1;
            double colWidth = pw;           // larghezza di ogni colonna = PanelWidth
            double halfTotalW = cols * colWidth / 2.0;

            for (int c = 0; c < cols; c++)
            {
                double x0 = -halfTotalW + c * colWidth;
                double x1 = x0 + colWidth;

                for (int i = 0; i < n; i++)
                {
                    double y0 = -hl + i * ph;
                    double y1 = y0 + ph;
                    AddPanelRect(btr, tr, x0, y0, x1, y1);
                }
            }
        }
        else // Transverse
        {
            // Pannelli impilati lungo Y, lato lungo (ph) perpendicolare all'asse (va in X).
            // Passo lungo Y = PanelWidth; larghezza in X = PanelHeight.
            // 1P → 1 colonna; 2P → 2 colonne.
            int    cols       = twoP ? 2 : 1;
            double colHeight  = ph;         // estensione in X di ogni colonna
            double halfTotalW = cols * colHeight / 2.0;

            for (int c = 0; c < cols; c++)
            {
                double x0 = -halfTotalW + c * colHeight;
                double x1 = x0 + colHeight;

                for (int i = 0; i < n; i++)
                {
                    double y0 = -hl + i * pw;
                    double y1 = y0 + pw;
                    AddPanelRect(btr, tr, x0, y0, x1, y1);
                }
            }
        }
    }

    private static void AddPanelRect(
        BlockTableRecord btr, Transaction tr,
        double x0, double y0, double x1, double y1)
    {
        var poly = MakeRect(x0, y0, x1, y1);
        poly.Layer = LayerName;
        btr.AppendEntity(poly);
        tr.AddNewlyCreatedDBObject(poly, add: true);
    }

    private static Polyline MakeRect(double x0, double y0, double x1, double y1)
    {
        var p = new Polyline();
        p.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
        p.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
        p.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
        p.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
        p.Closed = true;
        return p;
    }

    // ── Inserimento istanza ───────────────────────────────────────────────────

    public static void InsertTracker(
        Database    db,
        Transaction tr,
        Point3d     insertPt,
        double      rotationRad,
        double      rollRad,
        int         rowId,
        int         colId)
    {
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!blockTable.Has(BlockName)) return;

        var modelSpace = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        var br = new BlockReference(insertPt, blockTable[BlockName])
        {
            Layer    = LayerName,
            Rotation = rotationRad
        };
        modelSpace.AppendEntity(br);
        tr.AddNewlyCreatedDBObject(br, add: true);

        var btrDef = (BlockTableRecord)tr.GetObject(blockTable[BlockName], OpenMode.ForRead);

        foreach (ObjectId attId in btrDef)
        {
            var obj = tr.GetObject(attId, OpenMode.ForRead);
            if (obj is not AttributeDefinition attDef || attDef.Constant) continue;

            var attRef = new AttributeReference();
            attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
            attRef.Layer = LayerName;

            attRef.TextString = attDef.Tag switch
            {
                "ROW_ID"    => rowId.ToString(),
                "COL_ID"    => colId.ToString(),
                "ELEVATION" => $"{insertPt.Z:F3}",
                "AZIMUTH"   => $"{rotationRad * 180.0 / Math.PI:F2}",
                _           => attDef.TextString
            };

            br.AttributeCollection.AppendAttribute(attRef);
            tr.AddNewlyCreatedDBObject(attRef, add: true);
        }

        // ── Inclinazione 3D lungo l'asse tracker (segue il profilo del terreno) ──
        // Il blocco è disegnato orizzontale; applica la rotazione di roll attorno
        // all'asse trasversale del tracker (perpendicolare all'asse nel piano XY).
        //
        // Asse di tilt derivato:
        //   il blocco ha Y locale → AxisDirection in world dopo rotRad.
        //   Per inclinare Y verso +Z (up) con roll > 0, l'asse di rotazione
        //   è il vettore X del blocco in world = (cos(rotRad), sin(rotRad), 0).
        //   (dimostrazione: cross(AxisDir, Z_up) = (axY, -axX) = (cos(rotRad), sin(rotRad)) ✓)
        if (Math.Abs(rollRad) > 1e-10)
        {
            var tiltAxis   = new Vector3d(Math.Cos(rotationRad), Math.Sin(rotationRad), 0);
            var rollMatrix = Matrix3d.Rotation(rollRad, tiltAxis, insertPt);

            br.TransformBy(rollMatrix);

            // Aggiorna posizione degli AttributeReference (sono entità separate nel DB)
            var attIds = br.AttributeCollection.Cast<ObjectId>().ToList();
            foreach (ObjectId attId in attIds)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForWrite);
                att.TransformBy(rollMatrix);
            }
        }
    }

    // ── Helper privati ────────────────────────────────────────────────────────

    private static AttributeDefinition MakeAttribute(
        string tag, string prompt, Point2d position) =>
        new()
        {
            Tag        = tag,
            Prompt     = prompt,
            TextString = "",
            Position   = new Point3d(position.X, position.Y, 0),
            Height     = 0.4,
            Invisible  = true,
            Layer      = LayerName
        };
}
