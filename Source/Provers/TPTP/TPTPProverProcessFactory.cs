namespace Microsoft.Boogie.TPTP;

/// <summary>
/// An interface for a TPTP prover. If you with to implement a new TPTP prover, you should implement this class
/// and then see <see cref="Factory">the Factory class</see> on constructing the actual prover.
/// </summary>
public interface TPTPProverProcessFactory
{
    /// <summary>
    /// Start a new process.
    /// </summary>
    public TPTPProverProcess Start();
}

