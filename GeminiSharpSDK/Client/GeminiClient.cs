using ManagedCode.GeminiSharpSDK.Configuration;
using ManagedCode.GeminiSharpSDK.Execution;
using ManagedCode.GeminiSharpSDK.Internal;
using ManagedCode.GeminiSharpSDK.Models;

namespace ManagedCode.GeminiSharpSDK.Client;

public sealed class GeminiClient : IDisposable
{
    private readonly GeminiOptions _options;
    private readonly bool _autoStart;
    private readonly ConnectionState _connectionState;

    public GeminiClient(GeminiClientOptions? options = null)
        : this(options, null)
    {
    }

    public GeminiClient(GeminiOptions options)
        : this(CreateClientOptions(options), null)
    {
    }

    internal GeminiClient(GeminiClientOptions? options, GeminiExec? exec)
    {
        var resolvedOptions = options ?? new GeminiClientOptions();
        _options = resolvedOptions.GeminiOptions ?? new GeminiOptions();
        _autoStart = resolvedOptions.AutoStart;
        _connectionState = new ConnectionState(exec);
    }

    public GeminiClientState State => _connectionState.GetSnapshot();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connectionState.Start(CreateExec);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connectionState.Stop();
        return Task.CompletedTask;
    }

    public GeminiThread StartThread(ThreadOptions? options = null)
    {
        var exec = GetOrCreateExec();
        return new GeminiThread(exec, _options, options ?? new ThreadOptions());
    }

    public GeminiThread ResumeThread(string id, ThreadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var exec = GetOrCreateExec();
        return new GeminiThread(exec, _options, options ?? new ThreadOptions(), id);
    }

    public GeminiCliMetadata GetCliMetadata()
    {
        var executablePath = GeminiCliLocator.FindGeminiPath(_options.GeminiExecutablePath);
        return GeminiCliMetadataReader.Read(executablePath);
    }

    public GeminiCliUpdateStatus GetCliUpdateStatus()
    {
        var executablePath = GeminiCliLocator.FindGeminiPath(_options.GeminiExecutablePath);
        return GeminiCliMetadataReader.ReadUpdateStatus(executablePath);
    }

    public void Dispose() => _connectionState.Dispose();

    private GeminiExec GetOrCreateExec() => _connectionState.GetOrCreate(_autoStart, CreateExec);

    private GeminiExec CreateExec()
    {
        return new GeminiExec(
            _options.GeminiExecutablePath,
            _options.EnvironmentVariables,
            _options.Config,
            _options.Logger);
    }

    private static GeminiClientOptions CreateClientOptions(GeminiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new GeminiClientOptions
        {
            GeminiOptions = options,
            AutoStart = true,
        };
    }

    private sealed class ConnectionState
    {
        private readonly Lock _gate = new();
        private GeminiExec? _exec;
        private bool _disposed;

        internal ConnectionState(GeminiExec? exec)
        {
            _exec = exec;
        }

        internal GeminiClientState GetSnapshot()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return GeminiClientState.Disposed;
                }

                return _exec is null
                    ? GeminiClientState.Disconnected
                    : GeminiClientState.Connected;
            }
        }

        internal void Start(Func<GeminiExec> execFactory)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _exec ??= execFactory();
            }
        }

        internal void Stop()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _exec = null;
            }
        }

        internal GeminiExec GetOrCreate(bool autoStart, Func<GeminiExec> execFactory)
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (_exec is not null)
                {
                    return _exec;
                }

                if (!autoStart)
                {
                    throw new InvalidOperationException($"Client not connected. Call {nameof(StartAsync)} first.");
                }

                _exec = execFactory();
                return _exec;
            }
        }

        internal void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _exec = null;
                _disposed = true;
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(GeminiClient));
    }
}
