using PetFam.Core.Geometry;

namespace PetFam.Core.World;

/// <summary>CIE L*a*b* colour of one grid cell.</summary>
public readonly record struct Lab(float L, float A, float B)
{
    public static Lab FromRgb(byte r, byte g, byte b)
    {
        static float Lin(byte v)
        {
            var c = v / 255f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        var R = Lin(r); var G = Lin(g); var B = Lin(b);
        var x = (0.4124f * R + 0.3576f * G + 0.1805f * B) / 0.95047f;
        var y = 0.2126f * R + 0.7152f * G + 0.0722f * B;
        var z = (0.0193f * R + 0.1192f * G + 0.9505f * B) / 1.08883f;

        static float F(float t) => t > 0.008856f ? MathF.Cbrt(t) : 7.787f * t + 16f / 116f;
        var fx = F(x); var fy = F(y); var fz = F(z);
        return new Lab(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    public static float Distance(Lab p, Lab q)
    {
        var dl = p.L - q.L; var da = p.A - q.A; var db = p.B - q.B;
        return MathF.Sqrt(dl * dl + da * da + db * db);
    }
}

/// <summary>
/// Finds the floor in a wallpaper (plan §6.2 ③, heuristic version, on-device).
/// The ground is assumed to touch the bottom of the screen: its colour is learned from the bottom rows, then the
/// region is grown upward through cells that match that colour and have no strong edge in between. Sky-like cells
/// are never ground. Big objects standing on the ground (an animal, a house) have other colours, so they stay out.
/// </summary>
public static class HabitatAnalyzer
{
    public const float MinGroundFraction = 0.04f;

    public sealed record Result(WalkMask? Mask, string Report);

    /// <summary>One floor material learned from the bottom of the screen (gravel, grass, a wooden floor ...).</summary>
    private sealed record Material(Lab Mean, Lab Sigma, float Tex, float TexSigma, int Count);

    /// <param name="cells">Row-major colours, cols × rows, covering <paramref name="bounds"/>.</param>
    /// <param name="texture">
    /// Optional roughness per cell (spread of lightness among its pixels). With it, smooth areas (sky, water,
    /// reflections) are told apart from rough ground of the same colour.
    /// </param>
    public static Result Analyze(Lab[] cells, int cols, int rows, RectI bounds, RectI workingArea, float[]? texture = null)
    {
        if (cells.Length != cols * rows) throw new ArgumentException("cells length must be cols*rows");
        if (texture is not null && texture.Length != cells.Length) throw new ArgumentException("texture length must be cols*rows");
        var cellH = bounds.Height / (float)rows;

        // Rows whose centre lies in the working area (above the taskbar).
        var firstRow = Math.Clamp((int)MathF.Ceiling((workingArea.Top - bounds.Y) / cellH - 0.5f), 0, rows - 1);
        var lastRow = Math.Clamp((int)MathF.Floor((workingArea.Bottom - bounds.Y) / cellH - 0.5f), 0, rows - 1);
        var usableRows = lastRow - firstRow + 1;
        if (usableRows < 6) return new Result(null, "too few rows");

        Lab At(int c, int r) => cells[r * cols + c];
        var seedRows = Math.Max(2, usableRows / 12);

        // 0. Texture, smoothed a little and measured against this picture's typical roughness.
        var hasTex = texture is not null;
        var tex = hasTex ? BoxMean(texture!, cols, rows) : new float[cells.Length];
        var tRef = 1f;
        if (hasTex)
        {
            var usableTex = new List<float>();
            for (var r = firstRow; r <= lastRow; r++)
            for (var c = 0; c < cols; c++)
                usableTex.Add(tex[r * cols + c]);
            tRef = MathF.Max(1.2f, Median(usableTex));
        }
        bool Smooth(int i, float k) => hasTex && tex[i] < tRef * k;

        // 1. Water and sky.
        //    A lake mirrors what stands behind it: find the waterline as the row the picture is mirrored about.
        //    Below it, smooth cells connected to the mirrored ones (or bluish and smooth) are water.
        //    Sky is smooth and grows down from the top edge, but never past the waterline.
        var water = new bool[cells.Length];
        var sky = new bool[cells.Length];
        var waterline = hasTex ? FindWaterline(cells, tex, tRef, cols, rows, firstRow, lastRow) : (Row: -1, Columns: new bool[cols]);

        bool BlueWater(Lab p) => (p.B < -6f && p.A < 6f && p.L > 15f && p.L < 85f) || (p.A < -8f && p.B < 2f && p.L > 15f && p.L < 85f);
        if (hasTex)
        {
            var q = new Queue<int>();
            var top = waterline.Row >= 0 ? waterline.Row + 1 : firstRow + (int)(usableRows * 0.45f);
            for (var r = top; r <= lastRow; r++)
            for (var c = 0; c < cols; c++)
            {
                var i = r * cols + c;
                if (cells[i].L < 12f) continue;   // black (space, deep shadow) is not water
                var mirrored = waterline.Row >= 0 && waterline.Columns[c] && r <= waterline.Row + Math.Max(3, usableRows / 10);
                // Below the waterline, a cell that looks like its mirror image above is a reflection: water.
                var m = waterline.Row >= 0 ? 2 * waterline.Row - r : -1;
                var reflects = waterline.Row >= 0 && waterline.Columns[c] && m >= firstRow && Lab.Distance(cells[i], cells[m * cols + c]) < 22f;
                if (!reflects)
                {
                    if (!Smooth(i, mirrored ? 0.9f : 0.6f)) continue;
                    if (!mirrored && !BlueWater(cells[i])) continue;
                }
                water[i] = true;
                q.Enqueue(i);
            }
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                foreach (var j in Neighbours(i, cols, rows))
                {
                    var jr = j / cols;
                    if (water[j] || jr < top || jr > lastRow || !Smooth(j, 0.85f) || cells[j].L < 12f) continue;
                    if (Lab.Distance(cells[i], cells[j]) > 22f) continue;
                    water[j] = true;
                    q.Enqueue(j);
                }
            }
            // Keep only big, wide bodies of water. With a mirror line: the lake that mirrors, and smooth patches
            // right next to it (bands of reflected sky), but not dark smooth grass elsewhere in a blue-tinted photo.
            var comps = Components(water, cols, rows).ToList();
            var nearLake = new bool[cells.Length];
            if (waterline.Row >= 0)
            {
                var lakeRows = waterline.Row + Math.Max(3, usableRows / 10);
                foreach (var comp in comps.Where(cm => cm.Any(i => waterline.Columns[i % cols] && i / cols <= lakeRows)))
                    foreach (var i in comp) nearLake[i] = true;
                for (var n = 0; n < 4; n++) nearLake = Dilate(nearLake, cols, rows);
            }
            var keepWater = new bool[cells.Length];
            foreach (var comp in comps)
            {
                // Under the lake's columns the water continues down to the bottom, even where a rough band of
                // reflections cuts it off from the waterline.
                if (waterline.Row >= 0 && !comp.Any(i => nearLake[i]) && comp.Count(i => waterline.Columns[i % cols]) < comp.Count * 0.3f) continue;
                var (minR, maxR, minC, maxC) = Extent(comp, cols);
                if (comp.Count < Math.Max(12, usableRows * cols / 250)) continue;
                // A lake has a far shore. Smooth blue that runs straight into the search limit is more of the
                // same backdrop (an abstract gradient), not water.
                if (waterline.Row < 0 && comp.Count(i => i / cols == top) > (maxC - minC + 1) * 0.3f) continue;
                if (maxC - minC + 1 < (maxR - minR + 1) * 0.8f) continue;
                foreach (var i in comp) keepWater[i] = true;
            }
            water = keepWater;

            // In a mirrored column, everything from the waterline down to the water is lake too: the band of
            // reflected trees and hills just under the waterline is as rough as the trees themselves.
            if (waterline.Row >= 0)
            {
                for (var c = 0; c < cols; c++)
                {
                    if (!waterline.Columns[c]) continue;
                    var r0 = waterline.Row + 1;
                    while (r0 <= lastRow && !water[r0 * cols + c]) r0++;
                    if (r0 > lastRow || r0 - waterline.Row > usableRows * 0.35f) continue;
                    for (var r = waterline.Row + 1; r < r0; r++)
                        if (cells[r * cols + c].L >= 12f) water[r * cols + c] = true;
                }
            }

            var skyLimit = waterline.Row >= 0 ? waterline.Row : firstRow + (int)(usableRows * 0.75f);
            for (var c = 0; c < cols; c++)
            {
                var i = firstRow * cols + c;
                if (cells[i].L > 35f && Smooth(i, 1.0f)) { sky[i] = true; q.Enqueue(i); }
            }
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                foreach (var j in Neighbours(i, cols, rows))
                {
                    var jr = j / cols;
                    if (sky[j] || water[j] || jr < firstRow || jr >= skyLimit) continue;
                    if (cells[j].L <= 35f || !Smooth(j, 1.0f) || Lab.Distance(cells[i], cells[j]) > 16f) continue;
                    sky[j] = true;
                    q.Enqueue(j);
                }
            }
            // Clouds and birds inside the sky are sky too.
            sky = FillSmallHoles(sky, cols, rows, cols * rows / 60);
            for (var i = 0; i < sky.Length; i++)
                if (water[i] || i / cols >= skyLimit) sky[i] = false;
        }

        // Sky: smooth from the top (above), or bluish/near-white in the upper part of the screen.
        bool SkyLike(int r, Lab p, int i) =>
            hasTex ? sky[i] : (r - firstRow) < usableRows * 0.65f && ((p.B < -6f && p.L > 45f) || (p.L > 92f && MathF.Abs(p.A) < 4f && MathF.Abs(p.B) < 6f));

        // 2. Floor materials from the bottom band: the floor may be more than one thing (gravel and grass).
        var seed = new List<int>();
        for (var r = lastRow - seedRows + 1; r <= lastRow; r++)
        for (var c = 0; c < cols; c++)
        {
            var i = r * cols + c;
            if (!water[i] && !SkyLike(r, cells[i], i)) seed.Add(i);
        }
        var bandSize = seedRows * cols;
        var materials = seed.Count < bandSize * 0.08f ? new List<Material>() : LearnMaterials(seed, cells, tex, hasTex);

        // The bottom of the screen is a lake: the floor is its far shore.
        var shore = false;
        if (materials.Count == 0 && hasTex)
        {
            var bandWater = 0;
            for (var r = lastRow - seedRows + 1; r <= lastRow; r++)
            for (var c = 0; c < cols; c++)
                if (water[r * cols + c]) bandWater++;
            if (bandWater > bandSize * 0.5f)
            {
                seed.Clear();
                for (var c = 0; c < cols; c++)
                {
                    var r = lastRow;
                    while (r >= firstRow && !water[r * cols + c]) r--;      // skip anything in front of the lake
                    while (r >= firstRow && water[r * cols + c]) r--;       // across the water
                    for (var d = 0; d < 2 && r - d >= firstRow; d++)
                    {
                        var i = (r - d) * cols + c;
                        if (!sky[i] && !water[i]) seed.Add(i);
                    }
                }
                if (seed.Count >= cols * 0.3f)
                {
                    materials = LearnMaterials(seed, cells, tex, hasTex);
                    shore = materials.Count > 0;
                }
            }
        }
        if (materials.Count == 0) return new Result(null, "bottom band too varied, using flat floor");

        // A plain picture (one flat colour or a soft gradient) has no horizon: what looked like sky is the same
        // stuff as the floor, so the whole screen is floor.
        if (hasTex && sky.Any(x => x))
        {
            var skyCells = Enumerable.Range(0, sky.Length).Where(i => sky[i]).ToList();
            var skyMean = new Lab(Median(skyCells.Select(i => cells[i].L)), Median(skyCells.Select(i => cells[i].A)), Median(skyCells.Select(i => cells[i].B)));
            if (materials.Any(m => m.Tex < tRef * 0.5f && Lab.Distance(m.Mean, skyMean) < 10f)) Array.Clear(sky);
        }

        float MaterialDist(Material m, int i)
        {
            var p = cells[i];
            var dl = (p.L - m.Mean.L) / m.Sigma.L; var da = (p.A - m.Mean.A) / m.Sigma.A; var db = (p.B - m.Mean.B) / m.Sigma.B;
            var d2 = dl * dl + da * da + db * db;
            if (hasTex)
            {
                var dt = (tex[i] - m.Tex) / m.TexSigma;
                d2 += dt * dt * 0.6f;
            }
            return MathF.Sqrt(d2);
        }
        float ModelDist(int i) => materials.Min(m => MaterialDist(m, i));

        const float ModelLimit = 3.2f;
        const float EdgeLimit = 14f;

        // 3. Region growing from the bottom band.
        var ground = new bool[cells.Length];
        var queue = new Queue<(int C, int R)>();
        foreach (var i in seed)
        {
            if (ModelDist(i) > ModelLimit) continue;
            ground[i] = true;
            queue.Enqueue((i % cols, i / cols));
        }

        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            var here = At(c, r);
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, -1), (0, 1) })
            {
                var nc = c + dc; var nr = r + dr;
                if (nc < 0 || nc >= cols || nr < firstRow || nr > lastRow) continue;
                var i = nr * cols + nc;
                if (ground[i] || water[i]) continue;
                var p = At(nc, nr);
                if (SkyLike(nr, p, i)) continue;
                if (ModelDist(i) > ModelLimit) continue;
                if (Lab.Distance(p, here) > EdgeLimit) continue;
                ground[i] = true;
                queue.Enqueue((nc, nr));
            }
        }

        {
            // 4. Support from below: floor seen in perspective is continuous from the bottom of the screen upward.
            //    A floor-coloured patch sitting on top of an object (light fur on an animal's back) has no floor
            //    under it, so it is dropped. Small things lying on the floor (a puddle, a bright patch, up to
            //    SupportGap rows tall) do not break the support; tall objects such as an animal's body do.
            const int SupportGap = 8;
            for (var r = lastRow - 1; r >= firstRow; r--)
            for (var c = 0; c < cols; c++)
            {
                var i = r * cols + c;
                if (!ground[i]) continue;
                var supported = false;
                for (var g = 1; g <= SupportGap + 1 && !supported; g++)
                    supported = r + g > lastRow || ground[(r + g) * cols + c] || (shore && water[(r + g) * cols + c]);
                if (!supported) ground[i] = false;
            }

            // 5. Clean up: close small gaps, drop thin bridges, keep what touches the bottom, fill small holes.
            //    Closing may only fill floor-coloured gaps; a differently coloured patch (mud, a puddle) stays out.
            var beforeClose = ground;
            ground = Erode(Dilate(ground, cols, rows), cols, rows);           // close
            for (var i = 0; i < ground.Length; i++)
                if (ground[i] && !beforeClose[i] && (water[i] || sky[i] || ModelDist(i) > ModelLimit)) ground[i] = false;
            ground = Dilate(Erode(ground, cols, rows), cols, rows);           // open
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                if (r < firstRow || r > lastRow) ground[r * cols + c] = false;
            for (var i = 0; i < ground.Length; i++)
                if (water[i] || sky[i]) ground[i] = false;
            ground = shore ? KeepConnected(ground, seed, cols, rows) : KeepTouchingBottom(ground, cols, rows, lastRow);
            var beforeFill = ground;
            ground = FillSmallHoles(ground, cols, rows, maxHole: Math.Max(4, cols * rows / 400));
            for (var i = 0; i < ground.Length; i++)
                if (ground[i] && !beforeFill[i] && (water[i] || ModelDist(i) > ModelLimit * 1.5f)) ground[i] = false;

            var count = ground.Count(x => x);
            var usable = usableRows * cols;
            var fraction = count / (float)usable;
            var main = materials.OrderByDescending(m => m.Count).FirstOrDefault();
            var report = (shore ? "far shore " : "") + $"floor {string.Join(" + ", materials.Select(m => $"L{m.Mean.L:F0}/a{m.Mean.A:F0}/b{m.Mean.B:F0}" + (hasTex ? $"/t{m.Tex:F1}" : "")))}";
            report += $", ground {fraction:P0} of screen";
            if (hasTex) report += $", sky {sky.Count(x => x) / (float)usable:P0}, water {water.Count(x => x) / (float)usable:P0}"
                                  + (waterline.Row >= 0 ? $" (mirror line at row {waterline.Row})" : "");
            if (fraction < MinGroundFraction) return new Result(null, report + " → too small, using flat floor");

            // Water pets can reach: next to the floor or at the bottom of the screen.
            var nearFloor = Dilate(Dilate(Dilate(ground, cols, rows), cols, rows), cols, rows);
            foreach (var comp in Components(water, cols, rows))
                if (!comp.Any(i => nearFloor[i] || i / cols >= lastRow - 1))
                    foreach (var i in comp) water[i] = false;

            var kinds = new ZoneType[cols * rows];
            for (var i = 0; i < kinds.Length; i++)
                kinds[i] = ground[i] ? ZoneType.Ground : water[i] ? ZoneType.Water : sky[i] ? ZoneType.Sky : ZoneType.Blocked;
            var model = main?.Mean ?? new Lab(50, 0, 0);
            var sigma = main?.Sigma ?? new Lab(7, 4, 4);
            var places = ClassifyPlaces(cells, kinds, cols, rows, firstRow, lastRow, model, sigma,
                waterOk: i => !hasTex || (Smooth(i, 0.6f) && (waterline.Row < 0 || i / cols > waterline.Row)),
                rough: i => !hasTex || !Smooth(i, 0.6f));
            return new Result(new WalkMask(cols, rows, bounds, kinds), report + "; " + places);
        }
    }

    /// <summary>
    /// Clusters the bottom-band cells into up to three floor materials (k-means on colour and roughness),
    /// dropping tiny clusters and clusters too varied to be one surface.
    /// </summary>
    private static List<Material> LearnMaterials(List<int> seed, Lab[] cells, float[] tex, bool hasTex)
    {
        float W(int i) => hasTex ? tex[i] * 3f : 0f;
        var sorted = seed.OrderBy(i => cells[i].L + W(i)).ToList();
        var k = Math.Min(3, Math.Max(1, seed.Count / 6));
        var centers = Enumerable.Range(0, k).Select(j => sorted[(int)((j + 0.5f) / k * (sorted.Count - 1))])
            .Select(i => (L: cells[i].L, A: cells[i].A, B: cells[i].B, T: W(i))).ToArray();
        var assign = new int[seed.Count];
        for (var iter = 0; iter < 10; iter++)
        {
            for (var s = 0; s < seed.Count; s++)
            {
                var p = cells[seed[s]];
                var t = W(seed[s]);
                var best = 0;
                var bestD = float.MaxValue;
                for (var j = 0; j < k; j++)
                {
                    var d = Sq(p.L - centers[j].L) + Sq(p.A - centers[j].A) * 2 + Sq(p.B - centers[j].B) * 2 + Sq(t - centers[j].T);
                    if (d < bestD) { bestD = d; best = j; }
                }
                assign[s] = best;
            }
            for (var j = 0; j < k; j++)
            {
                var members = Enumerable.Range(0, seed.Count).Where(s => assign[s] == j).Select(s => seed[s]).ToList();
                if (members.Count == 0) continue;
                centers[j] = (members.Average(i => cells[i].L), members.Average(i => cells[i].A), members.Average(i => cells[i].B), members.Average(W));
            }
        }

        var result = new List<Material>();
        for (var j = 0; j < k; j++)
        {
            var members = Enumerable.Range(0, seed.Count).Where(s => assign[s] == j).Select(s => seed[s]).ToList();
            if (members.Count < Math.Max(4, seed.Count * 0.1f)) continue;
            var mL = Median(members.Select(i => cells[i].L)); var mA = Median(members.Select(i => cells[i].A)); var mB = Median(members.Select(i => cells[i].B));
            var sL = Math.Max(7f, 1.4826f * Median(members.Select(i => MathF.Abs(cells[i].L - mL))));
            var sA = Math.Max(4f, 1.4826f * Median(members.Select(i => MathF.Abs(cells[i].A - mA))));
            var sB = Math.Max(4f, 1.4826f * Median(members.Select(i => MathF.Abs(cells[i].B - mB))));
            // No consistent surface (busy photo, pattern): don't guess.
            if (sL > 28f || sA > 22f || sB > 22f) continue;
            var mT = hasTex ? Median(members.Select(i => tex[i])) : 0f;
            var sT = hasTex ? Math.Max(1.2f, 1.4826f * Median(members.Select(i => MathF.Abs(tex[i] - mT)))) : 1f;
            result.Add(new Material(new Lab(mL, mA, mB), new Lab(sL, sA, sB), mT, sT, members.Count));
        }

        // Two clusters of the same surface (a lighter and a darker patch of one lawn) become one.
        for (var a = 0; a < result.Count; a++)
        for (var b = result.Count - 1; b > a; b--)
        {
            var x = result[a]; var y = result[b];
            if (Lab.Distance(x.Mean, y.Mean) < 8f && MathF.Abs(x.Tex - y.Tex) < 1.5f)
            {
                result[a] = x with
                {
                    Sigma = new Lab(MathF.Max(x.Sigma.L, y.Sigma.L), MathF.Max(x.Sigma.A, y.Sigma.A), MathF.Max(x.Sigma.B, y.Sigma.B)),
                    Count = x.Count + y.Count,
                };
                result.RemoveAt(b);
            }
        }
        return result;

        static float Sq(float v) => v * v;
    }

    /// <summary>
    /// The row a lake mirrors the picture about, and the columns where the mirror image holds.
    /// For every candidate row, patches just above and just below are compared flipped; the row where many
    /// columns match well wins. Returns row -1 when nothing is mirrored.
    /// </summary>
    private static (int Row, bool[] Columns) FindWaterline(Lab[] cells, float[] tex, float tRef, int cols, int rows, int firstRow, int lastRow)
    {
        var usable = lastRow - firstRow + 1;
        var depth = Math.Max(4, usable / 7);
        const int half = 3;
        var scores = new List<(int Row, int Count, bool[] Columns)>();
        for (var h = firstRow + (int)(usable * 0.3f); h + depth + 1 <= lastRow; h++)
        {
            if (h - depth < firstRow) continue;
            var ok = new bool[cols];
            var count = 0;
            for (var c = 0; c < cols; c++)
            {
                // Mirrored (above row h-d against below row h+d) versus merely shifted (h-d against h+depth+1-d):
                // vertical streaks match both ways, a real reflection only the mirrored way.
                var mirror = new List<(double U, double V)>();
                var shifted = new List<(double U, double V)>();
                double tBelow = 0, tAbove = 0;
                for (var dc = -half; dc <= half; dc++)
                {
                    var cc = c + dc;
                    if (cc < 0 || cc >= cols) continue;
                    for (var d = 1; d <= depth; d++)
                    {
                        var u = cells[(h - d) * cols + cc].L;
                        mirror.Add((u, cells[(h + d) * cols + cc].L));
                        shifted.Add((u, cells[(h + depth + 1 - d) * cols + cc].L));
                        tBelow += tex[(h + d) * cols + cc];
                        tAbove += tex[(h - d) * cols + cc];
                    }
                }
                // A reflection lies on water: smooth, or at least smoother than what it mirrors.
                if (tBelow / mirror.Count > tRef * 0.8 && tBelow > tAbove * 0.9) continue;
                var cm = Correlation(mirror, out var varU, out var varV);
                if (varU < 9 || varV < 4) continue;                   // flat on either side: nothing to compare
                var cs = Correlation(shifted, out _, out _);
                // ...and a reflection has about the brightness of what it mirrors (not black space under a planet).
                var dl = mirror.Average(x => Math.Abs(x.U - x.V));
                if (cm > 0.65 && cm - cs > 0.3 && dl < 18) { ok[c] = true; count++; }
            }
            scores.Add((h, count, ok));
        }
        // A reflection is itself symmetric in places, which shows up as weaker mirror lines further down.
        // The real waterline is the highest strong one.
        var top = scores.Count == 0 ? 0 : scores.Max(x => x.Count);
        if (top < cols * 0.12f) return (-1, new bool[cols]);
        var best = scores.Where(x => x.Count >= top * 0.75f).OrderBy(x => x.Row).First();

        // Columns between mirrored ones belong to the same lake (a smooth stretch has nothing to mirror).
        var cols2 = (bool[])best.Columns.Clone();
        var gap = Math.Max(4, cols / 12);
        for (int c = 0, prev = -1; c < cols; c++)
        {
            if (!best.Columns[c]) continue;
            if (prev >= 0 && c - prev <= gap)
                for (var x = prev + 1; x < c; x++) cols2[x] = true;
            prev = c;
        }
        return (best.Row, cols2);
    }

    private static double Correlation(List<(double U, double V)> pts, out double varU, out double varV)
    {
        double su = 0, sv = 0, suu = 0, svv = 0, suv = 0;
        foreach (var (u, v) in pts) { su += u; sv += v; suu += u * u; svv += v * v; suv += u * v; }
        var n = pts.Count;
        varU = (suu - su * su / n) / n;
        varV = (svv - sv * sv / n) / n;
        if (varU < 1e-6 || varV < 1e-6) return 0;
        return (suv - su * sv / n) / n / Math.Sqrt(varU * varV);
    }

    private static float[] BoxMean(float[] v, int cols, int rows)
    {
        var o = new float[v.Length];
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            float s = 0; var n = 0;
            for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
            {
                int rr = r + dr, cc = c + dc;
                if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                s += v[rr * cols + cc]; n++;
            }
            o[r * cols + c] = s / n;
        }
        return o;
    }

    private static IEnumerable<List<int>> Components(bool[] m, int cols, int rows)
    {
        var seen = new bool[m.Length];
        for (var start = 0; start < m.Length; start++)
        {
            if (seen[start] || !m[start]) continue;
            var comp = new List<int>();
            var q = new Queue<int>();
            q.Enqueue(start);
            seen[start] = true;
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                comp.Add(i);
                foreach (var j in Neighbours(i, cols, rows))
                {
                    if (seen[j] || !m[j]) continue;
                    seen[j] = true;
                    q.Enqueue(j);
                }
            }
            yield return comp;
        }
    }

    /// <summary>
    /// Finds places on and next to the floor: water (pools, lakes, sea below the horizon), mud (flat dark-brown
    /// patches on the ground), sun (brightly lit ground), sand (light ground at the water's edge) and shelter
    /// (ground right under a big object). Mutates <paramref name="k"/> and returns a short summary.
    /// </summary>
    private static string ClassifyPlaces(Lab[] cells, ZoneType[] k, int cols, int rows, int firstRow, int lastRow, Lab model, Lab sigma, Func<int, bool> waterOk, Func<int, bool> rough)
    {
        var usableRows = lastRow - firstRow + 1;
        var usable = usableRows * cols;
        Lab At(int i) => cells[i];

        var horizonRow = lastRow;
        for (var i = 0; i < k.Length; i++)
            if (k[i] == ZoneType.Ground) { horizonRow = Math.Min(horizonRow, i / cols); }

        bool WaterLike(Lab p) => (p.B < -6f && p.A < 6f && p.L > 15f && p.L < 85f) || (p.A < -8f && p.B < 2f && p.L > 15f && p.L < 85f);
        bool MudLike(Lab p) => p.L is >= 12f and <= 50f && p.A is >= 1f and <= 22f && p.B is >= 5f and <= 32f;
        bool SandLike(Lab p) => p.L is >= 62f and <= 94f && p.A is >= -3f and <= 14f && p.B is >= 8f and <= 40f;

        // Distance (in cells, up to 8) from every cell to the nearest floor cell: water and sand may be separated
        // from the grass by a shore.
        var floorDist = DistanceTo(k, cols, rows, j => WalkMask.Walkable(k[j]), 8);

        // Water: non-floor, watery colour, below the upper third, next to the floor (or its shore), wider than tall.
        var water = 0;
        foreach (var comp in Components(k, cols, rows, firstRow, lastRow, i => k[i] == ZoneType.Blocked && WaterLike(At(i)) && waterOk(i)))
        {
            var (minR, maxR, minC, maxC) = Extent(comp, cols);
            var touches = comp.Any(i => floorDist[i] <= 6) || maxR >= lastRow - 1;
            if (comp.Count < Math.Max(10, usable / 300) || minR <= firstRow + usableRows * 0.3f || !touches) continue;
            if (maxR - minR + 1 > (maxC - minC + 1) * 1.2f) continue;
            foreach (var i in comp) k[i] = ZoneType.Water;
            water += comp.Count;
        }

        // Mud: non-floor, dark brown, lying flat on the ground (not a tall brown object such as an animal).
        var mud = 0;
        foreach (var comp in Components(k, cols, rows, firstRow, lastRow, i => k[i] == ZoneType.Blocked && MudLike(At(i))))
        {
            var (minR, maxR, minC, maxC) = Extent(comp, cols);
            var h = maxR - minR + 1;
            var w = maxC - minC + 1;
            var touches = comp.Any(i => Neighbours(i, cols, rows).Any(j => k[j] == ZoneType.Ground));
            if (comp.Count < Math.Max(5, usable / 800) || !touches || minR <= horizonRow) continue;
            if (h > Math.Max(3, w * 0.5f) || h > usableRows * 0.25f) continue;
            // Mud lies in the floor: the cells right above it are floor too (a dark door in a wall is not mud).
            //    Look two cells up: the rim of a puddle is a blend of mud and grass and belongs to neither.
            var top = comp.Where(i => i / cols > firstRow + 1 && !comp.Contains(i - cols)).ToList();
            bool FloorAbove(int i) => WalkMask.Walkable(k[i - cols]) || WalkMask.Walkable(k[i - 2 * cols]);
            if (top.Count > 0 && top.Count(FloorAbove) < top.Count * 0.5f) continue;
            foreach (var i in comp) k[i] = ZoneType.Mud;
            mud += comp.Count;
        }

        // Sun: clearly brighter patches of the floor. A sunlit patch has the floor's hue but more light, so it may
        // have been left out of the floor itself; take those back in when they touch the floor.
        var sun = 0;
        var bright = model.L + Math.Max(8f, 1.3f * sigma.L);
        // A bright patch lying in the floor has floor both above and below it (not an object on the horizon).
        bool InFloor(int i)
        {
            int c = i % cols, r = i / cols;
            bool Floor(int rr) => rr >= firstRow && rr <= lastRow && WalkMask.Walkable(k[rr * cols + c]);
            var above = false; var below = false;
            for (var d = 1; d <= 4; d++) { above |= Floor(r - d); below |= Floor(r + d); }
            return above && below;
        }

        bool SunLike(int i)
        {
            var p = At(i);
            if (p.L < bright) return false;
            if (k[i] == ZoneType.Ground) return true;
            return k[i] == ZoneType.Blocked && rough(i) && InFloor(i)
                   && MathF.Abs(p.A - model.A) <= 3f * sigma.A + 6f
                   && MathF.Abs(p.B - model.B) <= 3f * sigma.B + 10f
                   && !(p.B < -6f && p.L > 45f);
        }
        // Bright spots at the water's edge are reflections and wet stones, not a warm place to lie.
        var waterDist = DistanceTo(k, cols, rows, j => k[j] == ZoneType.Water, 3);
        foreach (var comp in Components(k, cols, rows, firstRow, lastRow, i => SunLike(i) && waterDist[i] > 2))
        {
            if (comp.Count < 4) continue;
            if (!comp.Any(i => k[i] == ZoneType.Ground || Neighbours(i, cols, rows).Any(j => k[j] == ZoneType.Ground))) continue;
            foreach (var i in comp) k[i] = ZoneType.Sun;
            sun += comp.Count;
        }

        // Sand: light ground within a few cells of water (beach, shore). The shore usually differs from the grass,
        // so non-floor cells count too as long as they are sand-coloured and sit right by the water.
        var sand = 0;
        if (water > 0)
        {
            for (var i = 0; i < k.Length; i++)
            {
                var r0 = i / cols;
                if (r0 < firstRow || r0 > lastRow) continue;
                if (k[i] is not (ZoneType.Ground or ZoneType.Sun or ZoneType.Blocked) || !SandLike(At(i))) continue;
                int c = i % cols, r = i / cols;
                var nearWater = false;
                for (var dr = -6; dr <= 6 && !nearWater; dr++)
                for (var dc = -6; dc <= 6 && !nearWater; dc++)
                {
                    int cc = c + dc, rr = r + dr;
                    if (cc >= 0 && rr >= 0 && cc < cols && rr < rows && k[rr * cols + cc] == ZoneType.Water) nearWater = true;
                }
                if (!nearWater) continue;
                k[i] = ZoneType.Sand;
                sand++;
            }
        }

        // House: an object standing on the floor with a flat base and walls of an even colour that end in vertical
        // edges (a building). Its entrance, the floor in front of the middle of the base, becomes a House place.
        var house = 0;
        var houses = 0;
        var contact = new int[cols];
        for (var c = 0; c < cols; c++)
        {
            contact[c] = -1;
            var r = lastRow;
            while (r > lastRow - 3 && !WalkMask.Walkable(k[r * cols + c])) r--;   // skip specks at the very bottom
            if (!WalkMask.Walkable(k[r * cols + c])) continue;
            while (r >= firstRow && WalkMask.Walkable(k[r * cols + c])) r--;
            if (r < firstRow || k[r * cols + c] != ZoneType.Blocked || r <= horizonRow + 3) continue;
            contact[c] = r;   // the object's bottom row in this column
        }

        for (var c0 = 0; c0 < cols;)
        {
            if (contact[c0] < 0) { c0++; continue; }
            var c1 = c0;
            while (c1 + 1 < cols && contact[c1 + 1] >= 0 && Math.Abs(contact[c1 + 1] - contact[c0]) <= 1) c1++;
            var baseRow = contact[c0];
            if (c1 - c0 + 1 >= 4 && c1 - c0 + 1 <= cols * 0.4f && LooksLikeHouse(c0, c1, baseRow))
            {
                houses++;
                var mid = (c0 + c1) / 2;
                for (var r = baseRow + 1; r <= Math.Min(lastRow, baseRow + 2); r++)
                for (var c = mid - 1; c <= mid + 1; c++)
                {
                    var i = r * cols + c;
                    if (c < 0 || c >= cols || !WalkMask.Walkable(k[i])) continue;
                    k[i] = ZoneType.House;
                    house++;
                }
            }
            c0 = c1 + 1;
        }

        bool LooksLikeHouse(int c0, int c1, int baseRow)
        {
            // Wall colour: the median of a few rows above the base (a door or windows do not skew it).
            var sample = new List<Lab>();
            for (var r = baseRow; r >= Math.Max(firstRow, baseRow - 3); r--)
            for (var c = c0 + 1; c <= c1 - 1; c++)
                if (k[r * cols + c] == ZoneType.Blocked) sample.Add(At(r * cols + c));
            if (sample.Count == 0) return false;
            var wall = new Lab(Median(sample.Select(x => x.L)), Median(sample.Select(x => x.A)), Median(sample.Select(x => x.B)));
            var height = 0;
            for (var r = baseRow; r >= firstRow && height < 24; r--)
            {
                var allBlocked = true;
                var uniform = 0;
                for (var c = c0; c <= c1; c++)
                {
                    var i = r * cols + c;
                    if (k[i] != ZoneType.Blocked) { allBlocked = false; break; }
                    if (Lab.Distance(At(i), wall) <= 20f) uniform++;
                }
                if (!allBlocked || uniform < (c1 - c0 + 1) * 0.6f) break;
                height++;
            }
            // Buildings are wide: a tree trunk or a pole (narrow and tall) is not a house.
            if (height < 3 || c1 - c0 + 1 < 5 || c1 - c0 + 1 < height * 0.5f) return false;

            // Vertical edges: beside the wall, on both sides, something else (floor or another colour).
            int left = 0, right = 0;
            for (var r = baseRow - height + 1; r <= baseRow; r++)
            {
                bool Differs(int c) => c < 0 || c >= cols || WalkMask.Walkable(k[r * cols + c]) || Lab.Distance(At(r * cols + c), wall) > 15f;
                if (Differs(c0 - 1)) left++;
                if (Differs(c1 + 1)) right++;
            }
            return left >= height * 0.6f && right >= height * 0.6f;
        }

        // Shelter: floor right below a tall object that stands well in front of the horizon.
        var shelter = 0;
        for (var c = 0; c < cols; c++)
        for (var r = Math.Max(firstRow + 1, horizonRow + 4); r <= lastRow; r++)
        {
            var i = r * cols + c;
            if (!WalkMask.Walkable(k[i]) || k[(r - 1) * cols + c] != ZoneType.Blocked) continue;
            var above = At((r - 1) * cols + c);
            if (above.B < -6f && above.L > 45f) continue; // sky, not an object
            var height = 0;
            for (var rr = r - 1; rr >= firstRow && k[rr * cols + c] == ZoneType.Blocked; rr--) height++;
            if (height < 6) continue;
            for (var rr = r; rr <= Math.Min(lastRow, r + 2); rr++)
            {
                var j = rr * cols + c;
                if (k[j] is ZoneType.Ground or ZoneType.Sun) { k[j] = ZoneType.Hide; shelter++; }
            }
        }

        return $"places: water {water}, mud {mud}, sun {sun}, sand {sand}, shelter {shelter}, house {houses} ({house} entrance cells)";
    }

    /// <summary>City-block distance from each cell to the nearest cell matching target, capped at max.</summary>
    private static int[] DistanceTo(ZoneType[] k, int cols, int rows, Func<int, bool> target, int max)
    {
        var dist = Enumerable.Repeat(max + 1, k.Length).ToArray();
        var q = new Queue<int>();
        for (var i = 0; i < k.Length; i++)
            if (target(i)) { dist[i] = 0; q.Enqueue(i); }
        while (q.Count > 0)
        {
            var i = q.Dequeue();
            if (dist[i] >= max) continue;
            foreach (var j in Neighbours(i, cols, rows))
            {
                if (dist[j] <= dist[i] + 1) continue;
                dist[j] = dist[i] + 1;
                q.Enqueue(j);
            }
        }
        return dist;
    }

    private static IEnumerable<List<int>> Components(ZoneType[] k, int cols, int rows, int firstRow, int lastRow, Func<int, bool> member)
    {
        var seen = new bool[k.Length];
        for (var r = firstRow; r <= lastRow; r++)
        for (var c = 0; c < cols; c++)
        {
            var start = r * cols + c;
            if (seen[start] || !member(start)) continue;
            var comp = new List<int>();
            var q = new Queue<int>();
            q.Enqueue(start);
            seen[start] = true;
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                comp.Add(i);
                foreach (var j in Neighbours(i, cols, rows))
                {
                    var jr = j / cols;
                    if (seen[j] || jr < firstRow || jr > lastRow || !member(j)) continue;
                    seen[j] = true;
                    q.Enqueue(j);
                }
            }
            yield return comp;
        }
    }

    private static IEnumerable<int> Neighbours(int i, int cols, int rows)
    {
        int c = i % cols, r = i / cols;
        if (c > 0) yield return i - 1;
        if (c < cols - 1) yield return i + 1;
        if (r > 0) yield return i - cols;
        if (r < rows - 1) yield return i + cols;
    }

    private static (int MinR, int MaxR, int MinC, int MaxC) Extent(List<int> comp, int cols)
    {
        int minR = int.MaxValue, maxR = -1, minC = int.MaxValue, maxC = -1;
        foreach (var i in comp)
        {
            int c = i % cols, r = i / cols;
            minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
            minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
        }
        return (minR, maxR, minC, maxC);
    }

    private static float Median(IEnumerable<float> values)
    {
        var a = values.OrderBy(v => v).ToArray();
        return a.Length == 0 ? 0f : a[a.Length / 2];
    }

    private static bool[] Dilate(bool[] m, int cols, int rows)
    {
        var o = new bool[m.Length];
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
            o[r * cols + c] = Get(m, cols, rows, c, r) || Get(m, cols, rows, c + 1, r) || Get(m, cols, rows, c - 1, r)
                              || Get(m, cols, rows, c, r + 1) || Get(m, cols, rows, c, r - 1);
        return o;
    }

    private static bool[] Erode(bool[] m, int cols, int rows)
    {
        var o = new bool[m.Length];
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
            o[r * cols + c] = Get(m, cols, rows, c, r, edge: true) && Get(m, cols, rows, c + 1, r, edge: true) && Get(m, cols, rows, c - 1, r, edge: true)
                              && Get(m, cols, rows, c, r + 1, edge: true) && Get(m, cols, rows, c, r - 1, edge: true);
        return o;
    }

    private static bool Get(bool[] m, int cols, int rows, int c, int r, bool edge = false)
    {
        if (c < 0 || r < 0 || c >= cols || r >= rows) return edge;
        return m[r * cols + c];
    }

    private static bool[] KeepTouchingBottom(bool[] m, int cols, int rows, int lastRow)
    {
        var keep = new bool[m.Length];
        var q = new Queue<(int, int)>();
        for (var c = 0; c < cols; c++)
        for (var r = Math.Max(0, lastRow - 1); r <= lastRow; r++)
            if (m[r * cols + c] && !keep[r * cols + c]) { keep[r * cols + c] = true; q.Enqueue((c, r)); }

        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, -1), (0, 1) })
            {
                var nc = c + dc; var nr = r + dr;
                if (nc < 0 || nr < 0 || nc >= cols || nr >= rows) continue;
                var i = nr * cols + nc;
                if (!m[i] || keep[i]) continue;
                keep[i] = true;
                q.Enqueue((nc, nr));
            }
        }
        return keep;
    }

    /// <summary>Keeps the parts of the mask connected to any of the start cells.</summary>
    private static bool[] KeepConnected(bool[] m, IEnumerable<int> start, int cols, int rows)
    {
        var keep = new bool[m.Length];
        var q = new Queue<int>();
        foreach (var i in start)
            if (m[i] && !keep[i]) { keep[i] = true; q.Enqueue(i); }
        while (q.Count > 0)
        {
            var i = q.Dequeue();
            foreach (var j in Neighbours(i, cols, rows))
            {
                if (!m[j] || keep[j]) continue;
                keep[j] = true;
                q.Enqueue(j);
            }
        }
        return keep;
    }

    /// <summary>Fills enclosed non-ground pockets smaller than maxHole cells (noise inside the floor).</summary>
    private static bool[] FillSmallHoles(bool[] m, int cols, int rows, int maxHole)
    {
        var o = (bool[])m.Clone();
        var seen = new bool[m.Length];
        for (var start = 0; start < m.Length; start++)
        {
            if (m[start] || seen[start]) continue;
            var comp = new List<int>();
            var touchesBorder = false;
            var q = new Queue<int>();
            q.Enqueue(start);
            seen[start] = true;
            while (q.Count > 0)
            {
                var i = q.Dequeue();
                comp.Add(i);
                int c = i % cols, r = i / cols;
                if (c == 0 || r == 0 || c == cols - 1 || r == rows - 1) touchesBorder = true;
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, -1), (0, 1) })
                {
                    var nc = c + dc; var nr = r + dr;
                    if (nc < 0 || nr < 0 || nc >= cols || nr >= rows) continue;
                    var j = nr * cols + nc;
                    if (m[j] || seen[j]) continue;
                    seen[j] = true;
                    q.Enqueue(j);
                }
            }
            if (!touchesBorder && comp.Count <= maxHole)
                foreach (var i in comp) o[i] = true;
        }
        return o;
    }
}
