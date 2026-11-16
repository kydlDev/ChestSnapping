using BenchmarkDotNet.Attributes;

namespace ChestSnapping.Benchmarks
{
    /// <summary>Snap rules: the yaw alignment runs every frame while a chest ghost is held near another chest; the name list when a chest is placed.</summary>
    [MemoryDiagnoser]
    public class SnappingBenchmarks
    {
        float _ghostYaw, _targetYaw;
        string _csv, _name, _clone;

        [GlobalSetup]
        public void Setup()
        {
            _ghostYaw = 237.4f;
            _targetYaw = -83.2f;
            _csv = "piece_chest, piece_chest_wood, piece_chest_private, piece_chest_blackmetal, Container";
            _name = "piece_chest_private";
            _clone = "piece_chest_wood(Clone)";
        }

        [Benchmark]
        public float AlignedYaw() => ChestAlignment.AlignedYaw(_ghostYaw, _targetYaw);

        [Benchmark]
        public float Delta() => ChestAlignment.Delta(_ghostYaw, _targetYaw);

        [Benchmark]
        public int ParseNames() => PrefabNames.Parse(_csv).Count;

        [Benchmark]
        public string StripClone() => PrefabNames.StripClone(_clone);

        [Benchmark]
        public string AppendName() => PrefabNames.Append(_csv, _name);

        [Benchmark]
        public string RemoveName() => PrefabNames.Remove(_csv, _name);

        [Benchmark]
        public bool NeedsSupportOverride() => StackingRules.NeedsSupportOverride(_ghostYaw > 0f, _targetYaw < 0f, _ghostYaw > 200f);
    }
}
