using NUnit.Framework;

// Allow tests to run in parallel at the fixture level. The codec is pure-functional
// and has no shared state; the vector loader only reads files. Per-test parallelism
// would require ref-struct-free tests, so we stop at fixture granularity.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
