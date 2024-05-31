using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

#if DEBUG
var benchmark = new Benchmark();
benchmark.Count = 1000;
benchmark.GlobalSetup();
var expected = benchmark.Basic();
Console.WriteLine($"Expected: {expected}");

List<(string, Func<Benchmark.Coordinate>)> methods = [
    (nameof(Benchmark.ShrimpleSIMD), benchmark.ShrimpleSIMD),
    (nameof(Benchmark.ProbablyUBSIMD), benchmark.ProbablyUBSIMD)
];

foreach (var (name, func) in methods)
{
    var r = func();
    if (!r.Equals(expected)) Console.WriteLine($"Wrong result for {name}: {r}");
}

#else
BenchmarkRunner.Run<Benchmark>();
#endif


public class Benchmark
{

    public readonly struct Coordinate(double x, double y) : IEquatable<Coordinate>
    {
        public double X { get; } = x;
        public double Y { get; } = y;
        public bool Equals(Coordinate other) => X == other.X && Y == other.Y;
        public override string ToString() => $"({X}, {Y})";
    }
    
    [Params(1000, 100_000)]
    public int Count { get; set; }

    private List<Coordinate> _coordinates = [];
    
    [GlobalSetup]
    public void GlobalSetup()
    {
        var random = new Random(15); // deterministic random
        _coordinates = new List<Coordinate>(Count);
        for (var i = 0; i < Count; i++)
        {
            _coordinates.Add(new Coordinate(Rand(), Rand()));
        }

        // lmao
        const double Start = 1;
        const double End = 1000;
        double Rand() => random.NextDouble() * Math.Abs(End - Start) + Start;
    }


    [Benchmark]
    public Coordinate Basic()
    {
        double xmax = 0, ymax = 0;
        
        foreach (var coord in _coordinates)
        {
            xmax = Math.Max(xmax, coord.X);
            ymax = Math.Max(ymax, coord.Y);
        }

        return new Coordinate(xmax, ymax);
    }


    [Benchmark]
    public Coordinate ShrimpleSIMD()
    {
        var span = CollectionsMarshal.AsSpan(_coordinates);

        var xmaxes = Vector256<double>.Zero;
        var ymaxes = Vector256<double>.Zero;
        
        int i;
        for (i = 0; i < span.Length; i += 4)
        {
            var xvec = Vector256.Create(span[i].X, span[i + 1].X, span[i + 2].X, span[i + 3].X);
            xmaxes = Vector256.Max(xmaxes, xvec);
            
            var yvec = Vector256.Create(span[i].Y, span[i + 1].Y, span[i + 2].Y, span[i + 3].Y);
            ymaxes = Vector256.Max(ymaxes, yvec);
        }

        // get current max values
        
        var xmax = xmaxes[0];
        var ymax = ymaxes[0];

        for (var j = 1; j < Vector256<double>.Count; j++)
        {
            xmax = Math.Max(xmax, xmaxes[j]);
            ymax = Math.Max(ymax, ymaxes[j]);
        }
        
        // iterate rest of input

        for (; i < span.Length; i++)
        {
            xmax = Math.Max(xmax, span[i].X);
            ymax = Math.Max(ymax, span[i].Y);
        }

        return new Coordinate(xmax, ymax);
    }
    
    [Benchmark]
    public Coordinate ProbablyUBSIMD()
    {
        // reinterpret Coordinate as double[] and run operations so that even index is x and odd is y
        var span = CollectionsMarshal.AsSpan(_coordinates);
        ref var start = ref Unsafe.As<Coordinate, double>(ref span[0]);

        var maxes = Vector256<double>.Zero;
        var length = span.Length * 2; // 2 doubles per span item
        int i;
        for (i = 0; i < length ; i += Vector256<double>.Count)
        {
            maxes = Vector256.Max(maxes, Vector256.LoadUnsafe(ref Unsafe.Add(ref start, i)));
        }

        // get current max value
        
        var xmax = maxes[0];
        var ymax = maxes[1];

        for (var j = 2; j < Vector256<double>.Count; j += 2)
        {
            xmax = Math.Max(xmax, maxes[j]);
            ymax = Math.Max(ymax, maxes[j + 1]);
        }
        
        // iterate rest of input

        for (; i < span.Length; i += 2)
        {
            xmax = Math.Max(xmax, Unsafe.Add(ref start, i));
            ymax = Math.Max(ymax, Unsafe.Add(ref start, i + 1));
        }

        return new Coordinate(xmax, ymax);
    }
}