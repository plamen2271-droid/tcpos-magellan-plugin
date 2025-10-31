using System;
using System.Threading;
using System.Threading.Tasks;
using TcpPos;

namespace MagellanBarcodeReader
{
    /// <summary>
    /// Xamarin.Android implementation of a TCPOS barcode reader plugin for Datalogic Magellan devices.
    /// </summary>
    public sealed class MagellanBarcodeReader : IBarcodeReaderPlugin, IDisposable
    {
        private readonly IScannerDevice _scanner;
        private readonly object _gate = new object();
        private CancellationTokenSource? _runLoop;
        private ITcpPosLogger? _logger;
        private IBarcodeSink? _sink;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MagellanBarcodeReader"/> class using the default platform scanner.
        /// </summary>
        public MagellanBarcodeReader()
            : this(PlatformScannerDevice.CreateDefault())
        {
        }

        internal MagellanBarcodeReader(IScannerDevice scanner)
        {
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _scanner.BarcodeScanned += OnBarcodeScanned;
        }

        /// <inheritdoc />
        public string Name => "Magellan (Datalogic) Barcode Reader";

        /// <inheritdoc />
        public void Initialize(ITcpPosLogger logger, IBarcodeSink sink)
        {
            ThrowIfDisposed();

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));

            _logger.SafeInfo("Initializing Magellan barcode reader plugin.");
            _scanner.Initialize(_logger);
        }

        /// <inheritdoc />
        public void Start()
        {
            ThrowIfDisposed();

            lock (_gate)
            {
                EnsureInitialized();

                if (_runLoop != null)
                {
                    _logger!.SafeWarn("Magellan barcode reader is already running.");
                    return;
                }

                _logger!.SafeInfo("Starting Magellan barcode reader loop.");
                _runLoop = new CancellationTokenSource();
                var token = _runLoop.Token;

                Task.Run(async () =>
                {
                    try
                    {
                        await _scanner.StartAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown.
                    }
                    catch (Exception ex)
                    {
                        _logger.SafeError("Magellan scanner loop terminated unexpectedly.", ex);
                    }
                }, token);
            }
        }

        /// <inheritdoc />
        public void Stop()
        {
            lock (_gate)
            {
                if (_runLoop == null)
                {
                    return;
                }

                _logger.SafeInfo("Stopping Magellan barcode reader loop.");
                _runLoop.Cancel();
                _runLoop.Dispose();
                _runLoop = null;
                _scanner.Stop();
            }
        }

        /// <summary>
        /// Injects a synthetic barcode into the plugin. Useful for local testing without hardware.
        /// </summary>
        /// <param name="data">The barcode payload.</param>
        /// <param name="symbology">The barcode symbology (optional).</param>
        public void SimulateScan(string data, string symbology = "UNKNOWN")
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (_scanner is ISimulationAwareScannerDevice simulator)
            {
                simulator.SimulateScan(data, symbology);
            }
            else
            {
                throw new NotSupportedException("The underlying scanner does not support simulation.");
            }
        }

        private void OnBarcodeScanned(object? sender, ScannerBarcodeEventArgs e)
        {
            lock (_gate)
            {
                if (_runLoop == null)
                {
                    return;
                }
            }

            try
            {
                _sink?.Publish(new BarcodeEventArgs(e.Data, e.Symbology, e.TimestampUtc));
                _logger.SafeInfo($"Barcode received: {e.Data} ({e.Symbology}).");
            }
            catch (Exception ex)
            {
                _logger.SafeError("Failed to forward barcode to TCPOS.", ex);
            }
        }

        private void EnsureInitialized()
        {
            if (_logger == null || _sink == null)
            {
                throw new InvalidOperationException("Plugin must be initialized before it can be started.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MagellanBarcodeReader));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _scanner.BarcodeScanned -= OnBarcodeScanned;
            _scanner.Dispose();
            _disposed = true;
        }
    }

    internal interface IScannerDevice : IDisposable
    {
        event EventHandler<ScannerBarcodeEventArgs> BarcodeScanned;

        void Initialize(ITcpPosLogger logger);

        Task StartAsync(CancellationToken cancellationToken);

        void Stop();
    }

    internal interface ISimulationAwareScannerDevice
    {
        void SimulateScan(string data, string symbology);
    }

    internal sealed class ScannerBarcodeEventArgs : EventArgs
    {
        public ScannerBarcodeEventArgs(string data, string symbology, DateTime timestampUtc)
        {
            Data = data;
            Symbology = symbology;
            TimestampUtc = timestampUtc;
        }

        public string Data { get; }

        public string Symbology { get; }

        public DateTime TimestampUtc { get; }
    }

    internal static class PlatformScannerDevice
    {
        public static IScannerDevice CreateDefault()
        {
            // In the absence of the physical SDK we fall back to a simulated device.
            // Projects consuming this library can replace the implementation via DI/tests.
            return new SimulatedScannerDevice();
        }
    }

    internal sealed class SimulatedScannerDevice : IScannerDevice, ISimulationAwareScannerDevice
    {
        private readonly object _gate = new object();
        private ITcpPosLogger? _logger;
        private bool _running;

        public event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned;

        public void Initialize(ITcpPosLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.SafeInfo("Simulated scanner initialized.");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _running = true;
            }

            _logger.SafeInfo("Simulated scanner ready (waiting for injected scans).");

            var tcs = new TaskCompletionSource<object?>();
            cancellationToken.Register(() => tcs.TrySetResult(null));
            return tcs.Task;
        }

        public void Stop()
        {
            lock (_gate)
            {
                _running = false;
            }

            _logger.SafeInfo("Simulated scanner stopped.");
        }

        public void SimulateScan(string data, string symbology)
        {
            if (string.IsNullOrEmpty(data))
            {
                throw new ArgumentException("Data must be provided.", nameof(data));
            }

            ScannerBarcodeEventArgs args;
            lock (_gate)
            {
                if (!_running)
                {
                    _logger.SafeWarn("Ignoring simulated scan because the scanner is not running.");
                    return;
                }

                args = new ScannerBarcodeEventArgs(data, symbology, DateTime.UtcNow);
            }

            _logger.SafeInfo($"Simulated scan: {data} ({symbology}).");
            BarcodeScanned?.Invoke(this, args);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
