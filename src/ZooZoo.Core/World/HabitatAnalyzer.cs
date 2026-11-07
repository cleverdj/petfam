using ZooZoo.Core.Geometry;

namespace ZooZoo.Core.World;

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

    /// <param name="cells">Row-major colours, cols × rows, covering <paramref name="bounds"/>.</param>
    public static Result Analyze(Lab[] cells, int cols, int rows, RectI bounds, RectI workingArea)
    {
        if (cells.Length != cols * rows) throw new ArgumentException("cells length must be cols*rows");
        var cellH = bounds.Height / (float)rows;

        // Rows whose centre lies in the working area (above the taskbar).
        var firstRow = Math.Clamp((int)MathF.Ceiling((workingArea.Top - bounds.Y) / cellH - 0.5f), 0, rows - 1);
        var lastRow = Math.Clamp((int)MathF.Floor((workingArea.Bottom - bounds.Y) / cellH - 0.5f), 0, rows - 1);
        var usableRows = lastRow - firstRow + 1;
        if (usableRows < 6) return new Result(null, "too few rows");

        Lab At(int c, int r) => cells[r * cols + c];

        // 1. Ground colour model from the bottom band (robust: median and MAD).
        var seedRows = Math.Max(2, usableRows / 12);
        var seed = new List<Lab>();
        for (var r = lastRow - seedRows + 1; r <= lastRow; r++)
        for (var c = 0; c < cols; c++)
            seed.Add(At(c, r));

        var mL = Median(seed.Select(s => s.L)); var mA = Median(seed.Select(s => s.A)); var mB = Median(seed.Select(s => s.B));
        var sL = Math.Max(7f, 1.4826f * Median(seed.Select(s => MathF.Abs(s.L - mL))));
        var sA = Math.Max(4f, 1.4826f * Median(seed.Select(s => MathF.Abs(s.A - mA))));
        var sB = Math.Max(4f, 1.4826f * Median(seed.Select(s => MathF.Abs(s.B - mB))));

        // No consistent floor colour at the bottom (busy photo, pattern): don't guess.
        if (sL > 28f || sA > 22f || sB > 22f)
            return new Result(null, $"bottom band too varied (σ {sL:F0}/{sA:F0}/{sB:F0}), using flat floor");

        float ModelDist(Lab p)
        {
            var dl = (p.L - mL) / sL; var da = (p.A - mA) / sA; var db = (p.B - mB) / sB;
            return MathF.Sqrt(dl * dl + da * da + db * db);
        }

        // Sky: bluish and bright in the upper part of the screen, or near-white.
        bool SkyLike(int r, Lab p) =>
            (r - firstRow) < usableRows * 0.65f && ((p.B < -6f && p.L > 45f) || (p.L > 92f && MathF.Abs(p.A) < 4f && MathF.Abs(p.B) < 6f));

        const float ModelLimit = 3.2f;
        const float EdgeLimit = 14f;

        // 2. Region growing from the bottom band.
        var ground = new bool[cols * rows];
        var queue = new Queue<(int C, int R)>();
        for (var r = lastRow - seedRows + 1; r <= lastRow; r++)
        for (var c = 0; c < cols; c++)
        {
            var p = At(c, r);
            if (ModelDist(p) > ModelLimit || SkyLike(r, p)) continue;
            ground[r * cols + c] = true;
            queue.Enqueue((c, r));
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
                if (ground[i]) continue;
                var p = At(nc, nr);
                if (SkyLike(nr, p)) continue;
                if (ModelDist(p) > ModelLimit) continue;
                if (Lab.Distance(p, here) > EdgeLimit) continue;
                ground[i] = true;
                queue.Enqueue((nc, nr));
            }
        }

        // 3. Support from below: floor seen in perspective is continuous from the bottom of the screen upward.
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
                supported = r + g > lastRow || ground[(r + g) * cols + c];   // the screen's bottom edge counts as floor
            if (!supported) ground[i] = false;
        }

        // 4. Clean up: close small gaps, drop thin bridges, keep what touches the bottom, fill small holes.
        //    Closing may only fill floor-coloured gaps; a differently coloured patch (mud, a puddle) stays out.
        var beforeClose = ground;
        ground = Erode(Dilate(ground, cols, rows), cols, rows);           // close
        for (var i = 0; i < ground.Length; i++)
            if (ground[i] && !beforeClose[i] && ModelDist(cells[i]) > ModelLimit) ground[i] = false;
        ground = Dilate(Erode(ground, cols, rows), cols, rows);           // open
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
            if (r < firstRow || r > lastRow) ground[r * cols + c] = false;
        ground = KeepTouchingBottom(ground, cols, rows, lastRow);
        var beforeFill = ground;
        ground = FillSmallHoles(ground, cols, rows, maxHole: Math.Max(4, cols * rows / 400));
        for (var i = 0; i < ground.Length; i++)
            if (ground[i] && !beforeFill[i] && ModelDist(cells[i]) > ModelLimit * 1.5f) ground[i] = false;

        var count = ground.Count(x => x);
        var usable = usableRows * cols;
        var fraction = count / (float)usable;
        var report = $"ground model L={mL:F0} a={mA:F0} b={mB:F0} (σ {sL:F0}/{sA:F0}/{sB:F0}), ground {fraction:P0} of screen";
        if (fraction < MinGroundFraction) return new Result(null, report + " → too small, using flat floor");

        var kinds = new ZoneType[cols * rows];
        for (var i = 0; i < kinds.Length; i++) kinds[i] = ground[i] ? ZoneType.Ground : ZoneType.Blocked;
        var places = ClassifyPlaces(cells, kinds, cols, rows, firstRow, lastRow, new Lab(mL, mA, mB), new Lab(sL, sA, sB));
        return new Result(new WalkMask(cols, rows, bounds, kinds), report + "; " + places);
    }

    /// <summary>
    /// Finds places on and next to the floor: water (pools, lakes, sea below the horizon), mud (flat dark-brown
    /// patches on the ground), sun (brightly lit ground), sand (light ground at the water's edge) and shelter
    /// (ground right under a big object). Mutates <paramref name="k"/> and returns a short summary.
    /// </summary>
    private static string ClassifyPlaces(Lab[] cells, ZoneType[] k, int cols, int rows, int firstRow, int lastRow, Lab model, Lab sigma)
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
        foreach (var comp in Components(k, cols, rows, firstRow, lastRow, i => k[i] == ZoneType.Blocked && WaterLike(At(i))))
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
        bool SunLike(int i)
        {
            var p = At(i);
            if (p.L < bright) return false;
            if (k[i] == ZoneType.Ground) return true;
            return k[i] == ZoneType.Blocked
                   && MathF.Abs(p.A - model.A) <= 3f * sigma.A + 6f
                   && MathF.Abs(p.B - model.B) <= 3f * sigma.B + 10f
                   && !(p.B < -6f && p.L > 45f);
        }
        foreach (var comp in Components(k, cols, rows, firstRow, lastRow, SunLike))
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
