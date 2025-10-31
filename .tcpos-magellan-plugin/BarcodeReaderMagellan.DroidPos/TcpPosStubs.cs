using System;

namespace TcpPos
{
    /// <summary>
    /// Represents a minimal contract for a TCPOS barcode reader plugin.
    /// </summary>
    public interface IBarcodeReaderPlugin
    {
        /// <summary>
        /// Gets the human friendly name for the plugin.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Initializes the plugin.
        /// </summary>
        /// <param name="logger">Logger provided by TCPOS.</param>
        /// <param name="sink">Sink used to forward decoded barcodes.</param>
        void Initialize(ITcpPosLogger logger, IBarcodeSink sink);

        /// <summary>
        /// Starts the plugin.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the plugin.
        /// </summary>
        void Stop();
    }

    /// <summary>
    /// Receives decoded barcodes and forwards them to TCPOS.
    /// </summary>
    public interface IBarcodeSink
    {
        /// <summary>
        /// Publishes a decoded barcode to the TCPOS runtime.
        /// </summary>
        void Publish(BarcodeEventArgs args);
    }

    /// <summary>
    /// Logger abstraction exposed by TCPOS.
    /// </summary>
    public interface ITcpPosLogger
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message, Exception? exception = null);
    }

    /// <summary>
    /// Event raised when a barcode has been decoded.
    /// </summary>
    public sealed class BarcodeEventArgs : EventArgs
    {
        public BarcodeEventArgs(string data, string symbology, DateTime timestamp)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Symbology = symbology ?? throw new ArgumentNullException(nameof(symbology));
            Timestamp = timestamp;
        }

        /// <summary>
        /// Gets the decoded barcode payload.
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// Gets the symbology of the barcode (EAN-13, Code128, etc.).
        /// </summary>
        public string Symbology { get; }

        /// <summary>
        /// Gets the timestamp (UTC) of when the barcode was decoded.
        /// </summary>
        public DateTime Timestamp { get; }
    }

    /// <summary>
    /// Helper extensions to protect against null loggers.
    /// </summary>
    public static class TcpPosLoggerExtensions
    {
        public static void SafeInfo(this ITcpPosLogger? logger, string message)
        {
            logger?.Info(message);
        }

        public static void SafeWarn(this ITcpPosLogger? logger, string message)
        {
            logger?.Warn(message);
        }

        public static void SafeError(this ITcpPosLogger? logger, string message, Exception? exception = null)
        {
            logger?.Error(message, exception);
        }
    }
}
