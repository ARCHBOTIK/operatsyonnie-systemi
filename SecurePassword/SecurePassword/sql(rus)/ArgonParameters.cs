namespace SecurePassword;
public struct ArgonParameters // ласс дл€ удобства установки параметров при использовании Argon
{
    public int MemorySize { get; } //–азмер занимаемой пам€ти
    public int Iterations { get; } // оличество итераций
    public int ParallelismDegree { get; } //—тепень параллелизма, используютс€ только геттеры потому что это структура дл€ использовани€ вместо кортежей

    public ArgonParameters(int memorySize, int iterations, int parallelismDegree) 
    {
        MemorySize = memorySize;
        Iterations = iterations;
        ParallelismDegree = parallelismDegree;
    }
}