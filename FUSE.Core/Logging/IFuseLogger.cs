namespace Fuse.Core.Logging
{
    /// <summary>
    /// Unity-free logging seam. The shipping FUSE code logs through the
    /// game-coupled static <c>FuseLog</c>; the ported (game-free) validator
    /// and migration route their diagnostics through this interface instead so
    /// a standalone editor can inject its own sink.
    /// </summary>
    public interface IFuseLogger
    {
        void Info(string message);

        void Warning(string message);

        void Error(string message);
    }

    /// <summary>
    /// No-op logger used as the default when a caller does not supply one.
    /// Keeps the ported migration/validation behaviour identical to the
    /// shipping code (which always logged somewhere) without forcing every
    /// caller to provide a sink.
    /// </summary>
    public sealed class NullFuseLogger : IFuseLogger
    {
        public static readonly NullFuseLogger Instance = new NullFuseLogger();

        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message)
        {
        }
    }
}
