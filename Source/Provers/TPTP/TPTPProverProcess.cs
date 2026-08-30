namespace Microsoft.Boogie.TPTP;

/// <summary>
/// An interface for a TPTP prover instance.
/// </summary>
public interface TPTPProverProcess
{
    /// <summary>
    /// Get a writer. The entire TPTP file to check should be written to this writer.
    /// </summary>
    /// <returns>The text writer</returns>
    public TextWriter TPTPWriter { get; }

    /// <summary>
    /// Close the standard input and get the result from the solver.
    /// </summary>
    /// <returns>The solver outcome</returns>
    public Task<SolverOutcome> GetResult(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kill the process. Does nothing if the process is already killed.
    /// </summary>
    public void Kill();

    /// <summary>
    /// Get the resource count, i.e. the steps it took to compute the result.
    /// <para>
    /// </summary>
    /// <returns>the resource count</returns>
    /// <exception cref="InvalidOperationException">
    ///     if called before GetResult was called
    /// </exception> 
    /// <exception cref="NotSupportedException">
    ///     if the prover does not support getting the resource count
    /// </exception> 
    public int GetRCount();
}