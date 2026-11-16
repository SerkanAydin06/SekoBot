namespace SekoBot.Memory;

/// <summary>
/// Everything on the map the character can't walk through, read from the game's own collision:
/// every static mesh of the level (walls, houses, barrels, the market's crates) and every foliage
/// instance (rocks, tree trunks) whose collision blocks pawns. Each mesh's simple collision shapes
/// (convex pieces, boxes, capsules, spheres: UBodySetup.AggGeom) are put where the mesh stands and
/// seen from above; a mesh that collides with its exact triangles only gets its bounding box.
/// Grass, flowers and bushes don't collide and are left out. Only ever reads.
/// </summary>
internal sealed partial class GameWorldReader
{
    private const int TransformRotation = 0x1D0, TransformScale = 0x1F0;
    private const int SphereElemSize = 0x40, BoxElemSize = 0x58, SphylElemSize = 0x50, ConvexElemSize = 0xB0;
    private const int ElemCenter = 0x30, ElemRotation = 0x3C, SphereRadius = 0x3C, BoxSize = 0x48, SphylRadius = 0x48, SphylLength = 0x4C;
    private const int ConvexVertices = 0x30, ConvexTransform = 0x70, InstanceMatrixSize = 0x40;
    private const int MaxComponentsPerActor = 3000, MaxInstances = 200_000, MaxVertices = 4096;
    /// <summary>A bounding box this wide (world units, 40 m) is a piece of ground or a whole town, not an obstacle.</summary>
    internal const double RoughTooBig = 4000;
    /// <summary>Lower than this (world units): stepped over.</summary>
    internal const double StepHeight = 35;
    /// <summary>Lower than this and wider than <see cref="FloorWidth"/> (world units): a floor, walked on.</summary>
    internal const double FloorHeight = 120, FloorWidth = 500;

    private readonly Dictionary<long, int> _componentKinds = [];
    private readonly Dictionary<long, (List<List<Vec3>> Pieces, bool Rough)> _meshShapes = [];

    /// <summary>The obstacles of the map the character is on (empty when it can't be read).</summary>
    public List<MapBlock> ReadBlocks()
    {
        var blocks = new List<MapBlock>();
        try
        {
            if (!_located)
                return blocks;
            long instance = Pointer(_moduleBase + _gameInstance);
            long instanceClass = Pointer(instance + ObjClass);
            if (ClassName(instanceClass) != "m2data_C")
                return blocks;
            long gm = Pointer(instance + Offset(instanceClass, "m2data_C", "gm"));
            long level = Pointer(gm + ObjOuter);
            long world = Pointer(level + ObjOuter);
            if (!MemoryReads.IsPointer(level))
                return blocks;
            var levels = new List<long> { level };
            if (MemoryReads.IsPointer(world))
            {
                long list = world + Offset(Pointer(world + ObjClass), "World", "Levels");
                long data = Pointer(list);
                int count = Int32(list + 8) ?? 0;
                for (int i = 0; i < count && i < 200 && MemoryReads.IsPointer(data); i++)
                    if (Pointer(data + 8L * i) is long l && MemoryReads.IsPointer(l) && !levels.Contains(l))
                        levels.Add(l);
            }
            foreach (long l in levels)
                if (TryActors(l, out long[] actors))
                    foreach (long actor in actors)
                        ReadActorBlocks(actor, blocks);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException) { }
        return blocks;
    }

    private void ReadActorBlocks(long actor, List<MapBlock> blocks)
    {
        if (!MemoryReads.IsPointer(actor))
            return;
        string actorClass = ClassName(Pointer(actor + ObjClass));
        // Moving things (mobs, players, loot) and the ground itself aren't obstacles of the map.
        if (actorClass is "NPC" or "playerMove" or "droppedItem" or "LODActor" || actorClass.StartsWith("Landscape", StringComparison.Ordinal))
            return;
        long root = Pointer(actor + ActorRoot);
        if (!MemoryReads.IsPointer(root))
            return;
        var stack = new Stack<long>();
        var seen = new HashSet<long>();
        stack.Push(root);
        while (stack.Count > 0 && seen.Count < MaxComponentsPerActor)
        {
            long component = stack.Pop();
            if (!seen.Add(component))
                continue;
            long cls = Pointer(component + ObjClass);
            if (!MemoryReads.IsPointer(cls))
                continue;
            long children = component + Offset(cls, "SceneComponent", "AttachChildren");
            long childData = Pointer(children);
            int childCount = Int32(children + 8) ?? 0;
            for (int i = 0; i < childCount && i < MaxComponentsPerActor && MemoryReads.IsPointer(childData); i++)
                if (Pointer(childData + 8L * i) is long child && MemoryReads.IsPointer(child))
                    stack.Push(child);
            int kind = ComponentKind(cls);
            if (kind == 0)
                continue;
            byte enabled = Byte(component + Offset(cls, "StaticMeshComponent", "BodyInstance.CollisionEnabled")) ?? 0;
            byte pawn = Byte(component + Offset(cls, "StaticMeshComponent", "BodyInstance.CollisionResponses.ResponseToChannels.Pawn")) ?? 0;
            if (enabled is not (1 or 3) || pawn != 2)
                continue;                                                    // no collision, or pawns pass through it
            long mesh = Pointer(component + Offset(cls, "StaticMeshComponent", "StaticMesh"));
            if (!MemoryReads.IsPointer(mesh) || MeshShapes(mesh) is not { Pieces.Count: > 0 } shape)
                continue;
            var bytes = new byte[0x30];
            if (!_memory.Read(component + TransformRotation, bytes, bytes.Length))
                continue;
            var toWorld = Transform.From(bytes, 0);
            string name = ClassName(mesh);
            if (kind == 1)
            {
                AddBlocks(blocks, shape, name, p => toWorld.Apply(p));
                continue;
            }
            // Foliage and other instanced meshes: one transform (a matrix) per rock / tree.
            long list = component + Offset(cls, "InstancedStaticMeshComponent", "PerInstanceSMData");
            long data = Pointer(list);
            int count = Int32(list + 8) ?? 0;
            if (!MemoryReads.IsPointer(data) || count <= 0 || count > MaxInstances)
                continue;
            var matrices = new byte[count * InstanceMatrixSize];
            if (!_memory.Read(data, matrices, matrices.Length))
                continue;
            for (int i = 0; i < count; i++)
            {
                int at = i * InstanceMatrixSize;
                float M(int k) => BitConverter.ToSingle(matrices, at + 4 * k);
                AddBlocks(blocks, shape, name, p => toWorld.Apply(new Vec3(
                    p.X * M(0) + p.Y * M(4) + p.Z * M(8) + M(12),
                    p.X * M(1) + p.Y * M(5) + p.Z * M(9) + M(13),
                    p.X * M(2) + p.Y * M(6) + p.Z * M(10) + M(14))));
            }
        }
    }

    /// <summary>One block per collision piece, in the world; flat and odd ones are left out.</summary>
    private static void AddBlocks(List<MapBlock> blocks, (List<List<Vec3>> Pieces, bool Rough) shape, string name, Func<Vec3, Vec3> place)
    {
        foreach (var piece in shape.Pieces)
        {
            var points = new List<(double X, double Y)>(piece.Count);
            double bottom = double.MaxValue, top = double.MinValue;
            foreach (var local in piece)
            {
                var p = place(local);
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || Math.Abs(p.X) > 1e7 || Math.Abs(p.Y) > 1e7)
                    return;
                points.Add((p.X, p.Y));
                bottom = Math.Min(bottom, p.Z);
                top = Math.Max(top, p.Z);
            }
            if (top - bottom < StepHeight)
                continue;
            var block = new MapBlock { Name = name, Points = MapBlock.Hull(points), Bottom = (float)Math.Round(bottom), Top = (float)Math.Round(top), Rough = shape.Rough };
            if (block.Corners < 3 || block.Area < 100)
                continue;
            // Low and wide: a floor or a platform (the market's floor) the character walks on.
            if (top - bottom < FloorHeight && block.Width > FloorWidth)
                continue;
            var (minX, minY, maxX, maxY) = block.Box;
            if (shape.Rough && (maxX - minX > RoughTooBig || maxY - minY > RoughTooBig))
                continue;
            blocks.Add(block);
        }
    }

    /// <summary>0: not a mesh, 1: a static mesh, 2: an instanced one (foliage).</summary>
    private int ComponentKind(long cls)
    {
        if (_componentKinds.TryGetValue(cls, out int kind))
            return kind;
        kind = 0;
        for (long c = cls; MemoryReads.IsPointer(c) && kind == 0; c = Pointer(c + StructSuper))
        {
            string n = ClassName(c);
            if (n == "InstancedStaticMeshComponent")
                kind = 2;
            else if (n == "StaticMeshComponent")
                kind = 1;
            else if (n is "PrimitiveComponent" or "SceneComponent" or "ActorComponent" or "Object")
                break;
        }
        return _componentKinds[cls] = kind;
    }

    /// <summary>A mesh's collision pieces in its own space (each a point cloud), read once.</summary>
    private (List<List<Vec3>> Pieces, bool Rough) MeshShapes(long mesh)
    {
        if (_meshShapes.TryGetValue(mesh, out var known))
            return known;
        var pieces = new List<List<Vec3>>();
        bool rough = false;
        long meshClass = Pointer(mesh + ObjClass);
        long setup = Pointer(mesh + Offset(meshClass, "StaticMesh", "BodySetup"));
        if (MemoryReads.IsPointer(setup))
        {
            long setupClass = Pointer(setup + ObjClass);
            byte trace = Byte(setup + Offset(setupClass, "BodySetup", "CollisionTraceFlag")) ?? 0;
            long geometry = setup + Offset(setupClass, "BodySetup", "AggGeom");
            if (trace != 3)                                     // 3: collides with its exact triangles (unreadable)
            {
                Elements(geometry + 0x00, SphereElemSize, e =>
                {
                    var c = Vec(e, ElemCenter);
                    double r = BitConverter.ToSingle(e, SphereRadius);
                    pieces.Add(Circle(c, r, r));
                });
                Elements(geometry + 0x10, BoxElemSize, e =>
                {
                    var c = Vec(e, ElemCenter);
                    var rot = Vec(e, ElemRotation);
                    var size = Vec(e, BoxSize);
                    pieces.Add(BoxCorners(size.X / 2, size.Y / 2, size.Z / 2).Select(p => Rotate(rot, p) + c).ToList());
                });
                Elements(geometry + 0x20, SphylElemSize, e =>
                {
                    var c = Vec(e, ElemCenter);
                    var rot = Vec(e, ElemRotation);
                    double r = BitConverter.ToSingle(e, SphylRadius), half = BitConverter.ToSingle(e, SphylLength) / 2 + r;
                    pieces.Add(BoxCorners(r, r, half).Select(p => Rotate(rot, p) + c).ToList());
                });
                Elements(geometry + 0x30, ConvexElemSize, e =>
                {
                    long vertices = BitConverter.ToInt64(e, ConvexVertices);
                    int count = BitConverter.ToInt32(e, ConvexVertices + 8);
                    if (!MemoryReads.IsPointer(vertices) || count < 4 || count > MaxVertices)
                        return;
                    var raw = new byte[count * 12];
                    if (!_memory.Read(vertices, raw, raw.Length))
                        return;
                    var t = Transform.From(e, ConvexTransform);
                    var cloud = new List<Vec3>(count);
                    for (int i = 0; i < count; i++)
                        cloud.Add(t.Apply(new Vec3(BitConverter.ToSingle(raw, 12 * i), BitConverter.ToSingle(raw, 12 * i + 4), BitConverter.ToSingle(raw, 12 * i + 8))));
                    pieces.Add(cloud);
                });
            }
        }
        if (pieces.Count == 0)
        {
            // No simple shapes: the mesh's bounding box.
            var bounds = new byte[0x1C];
            if (_memory.Read(mesh + Offset(meshClass, "StaticMesh", "ExtendedBounds"), bounds, bounds.Length))
            {
                var origin = Vec(bounds, 0);
                var extent = Vec(bounds, 12);
                if (extent.X > 0 && extent.Y > 0 && extent.Z > 0 && extent.X < 1e6 && extent.Y < 1e6)
                {
                    pieces.Add(BoxCorners(extent.X, extent.Y, extent.Z).Select(p => p + origin).ToList());
                    rough = true;
                }
            }
        }
        return _meshShapes[mesh] = (pieces, rough);
    }

    private void Elements(long array, int size, Action<byte[]> each)
    {
        long data = Pointer(array);
        int count = Int32(array + 8) ?? 0;
        if (!MemoryReads.IsPointer(data) || count <= 0 || count > 256)
            return;
        var all = new byte[count * size];
        if (!_memory.Read(data, all, all.Length))
            return;
        for (int i = 0; i < count; i++)
            each(all.AsSpan(i * size, size).ToArray());
    }

    private static Vec3 Vec(byte[] b, int at) =>
        new(BitConverter.ToSingle(b, at), BitConverter.ToSingle(b, at + 4), BitConverter.ToSingle(b, at + 8));

    private static List<Vec3> BoxCorners(double x, double y, double z) =>
        [new(-x, -y, -z), new(x, -y, -z), new(x, y, -z), new(-x, y, -z), new(-x, -y, z), new(x, -y, z), new(x, y, z), new(-x, y, z)];

    private static List<Vec3> Circle(Vec3 c, double r, double halfHeight)
    {
        var points = new List<Vec3>(16);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            points.Add(new Vec3(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), c.Z - halfHeight));
            points.Add(new Vec3(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), c.Z + halfHeight));
        }
        return points;
    }

    /// <summary>Turns a point by an FRotator (pitch, yaw, roll in degrees), as the engine does.</summary>
    internal static Vec3 Rotate(Vec3 rotator, Vec3 p)
    {
        double d = Math.PI / 180;
        double sp = Math.Sin(rotator.X * d), cp = Math.Cos(rotator.X * d);
        double sy = Math.Sin(rotator.Y * d), cy = Math.Cos(rotator.Y * d);
        double sr = Math.Sin(rotator.Z * d), cr = Math.Cos(rotator.Z * d);
        // Rows of FRotationMatrix: X, Y and Z axes.
        return new Vec3(
            p.X * cp * cy + p.Y * (sr * sp * cy - cr * sy) + p.Z * -(cr * sp * cy + sr * sy),
            p.X * cp * sy + p.Y * (sr * sp * sy + cr * cy) + p.Z * (cy * sr - cr * sp * sy),
            p.X * sp + p.Y * -sr * cp + p.Z * cr * cp);
    }

    /// <summary>An FTransform: rotation (quaternion), translation, scale.</summary>
    internal readonly record struct Transform(double Qx, double Qy, double Qz, double Qw, Vec3 Move, Vec3 Scale)
    {
        public static Transform From(byte[] b, int at) => new(
            BitConverter.ToSingle(b, at), BitConverter.ToSingle(b, at + 4), BitConverter.ToSingle(b, at + 8), BitConverter.ToSingle(b, at + 12),
            Vec(b, at + 16), Vec(b, at + 32));

        public Vec3 Apply(Vec3 p)
        {
            double x = p.X * Scale.X, y = p.Y * Scale.Y, z = p.Z * Scale.Z;
            // v + 2w(q × v) + 2 q × (q × v)
            double tx = 2 * (Qy * z - Qz * y), ty = 2 * (Qz * x - Qx * z), tz = 2 * (Qx * y - Qy * x);
            return new Vec3(
                x + Qw * tx + (Qy * tz - Qz * ty) + Move.X,
                y + Qw * ty + (Qz * tx - Qx * tz) + Move.Y,
                z + Qw * tz + (Qx * ty - Qy * tx) + Move.Z);
        }
    }
}
