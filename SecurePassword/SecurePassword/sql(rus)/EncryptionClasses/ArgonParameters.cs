namespace SecurePassword;
public struct ArgonParameters
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
}