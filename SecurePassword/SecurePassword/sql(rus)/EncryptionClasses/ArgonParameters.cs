namespace SecurePassword;

public readonly struct ArgonParameters : IEquatable<ArgonParameters>
{
    public int MemorySize { get; }
    public int Iterations { get; }
    public int ParallelismDegree { get; }

    public ArgonParameters(int memorySize, int iterations, int parallelismDegree) 
    {
        MemorySize = memorySize;
        Iterations = iterations;
        ParallelismDegree = parallelismDegree;
    }

    public bool Equals(ArgonParameters other)
    {
        return MemorySize == other.MemorySize &&
               Iterations == other.Iterations &&
               ParallelismDegree == other.ParallelismDegree;
    }

    public override bool Equals(object? obj)
    {
        return obj is ArgonParameters other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(MemorySize, Iterations, ParallelismDegree);
    }

    public static bool operator ==(ArgonParameters left, ArgonParameters right) => left.Equals(right);
    public static bool operator !=(ArgonParameters left, ArgonParameters right) => !left.Equals(right);

    public override string ToString() => $"MemorySize={MemorySize}KB, Iterations={Iterations}, Parallelism={ParallelismDegree}";
}