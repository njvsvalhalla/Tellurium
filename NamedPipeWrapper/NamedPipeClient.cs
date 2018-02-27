namespace NamedPipeWrapper
{
    using System;
    using System.IO;
    using System.IO.Pipes;
    using System.Runtime.InteropServices;
    using System.Threading;

    using IO;
    using JetBrains.Annotations;
    using Serialization;
    using Threading;

    /// <summary>
    /// Wraps a <see cref="NamedPipeClientStream"/>.
    /// </summary>
    /// <typeparam name="TReadWrite">Reference type to read from and write to the named pipe</typeparam>
    [PublicAPI]
    public class NamedPipeClient<TReadWrite> : NamedPipeClient<TReadWrite, TReadWrite> where TReadWrite : class
    {
        /// <summary>
        /// Constructs a new <c>NamedPipeClient</c> to connect to the <see cref="NamedPipeServer{TReadWrite}"/> specified by <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the server's pipe</param>
        /// <param name="serializer">Serializer to use. Can be null to use the default serializer</param>
        public NamedPipeClient(string pipeName, ICustomSerializer<TReadWrite> serializer = null) : base(pipeName, ".", serializer, serializer)
        {
        }

        /// <summary>
        /// Constructs a new <c>NamedPipeClient</c> to connect to the <see cref="NamedPipeServer{TReadWrite}"/> specified by <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the server's pipe</param>
        /// <param name="serverName">Server name. By default, "." (local).</param>
        /// <param name="serializer">Serializer to use. Can be null to use the default serializer</param>
        public NamedPipeClient(string pipeName, string serverName, ICustomSerializer<TReadWrite> serializer = null) : base(pipeName, serverName, serializer, serializer)
        {
        }
    }

    /// <summary>
    /// Wraps a <see cref="NamedPipeClientStream"/>.
    /// </summary>
    /// <typeparam name="TRead">Reference type to read from the named pipe</typeparam>
    /// <typeparam name="TWrite">Reference type to write to the named pipe</typeparam>
    [PublicAPI]
    public class NamedPipeClient<TRead, TWrite>
        where TRead : class
        where TWrite : class
    {
        /// <summary>
        /// Invoked whenever a message is received from the server.
        /// </summary>
        public event ConnectionMessageEventHandler<TRead, TWrite> ServerMessage;

        /// <summary>
        /// Invoked when the client disconnects from the server (e.g., the pipe is closed or broken).
        /// </summary>
        public event ConnectionDisconnectedEventHandler<TRead, TWrite> Disconnected;

        /// <summary>
        /// Invoked whenever an exception is thrown during a read or write operation on the named pipe.
        /// </summary>
        public event PipeExceptionEventHandler Error;

        private readonly string _pipeName;
        private NamedPipeConnection<TRead, TWrite> _connection;

        private readonly AutoResetEvent _connected = new AutoResetEvent(false);
        private readonly AutoResetEvent _disconnected = new AutoResetEvent(false);

        private volatile bool _closedExplicitly;
        private CancellationTokenSource _ctsCancelConnect;
        private readonly string _serverName;

        private readonly ICustomSerializer<TRead> _serializerRead;
        private readonly ICustomSerializer<TWrite> _serializerWrite;

        /// <summary>
        /// Constructs a new <c>NamedPipeClient</c> to connect to the <see cref="NamedPipeServer{TRead, TWrite}"/> specified by <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the server's pipe</param>
        /// <param name="serverName">Server name. Use "." for the local machine.</param>
        /// <param name="serializerRead">Serializer to use when reading. Can be null to use the default serializer</param>
        /// <param name="serializerWrite">Serializer to use when writing. Can be null to use the default serializer</param>
        public NamedPipeClient(string pipeName, string serverName, ICustomSerializer<TRead> serializerRead = null, ICustomSerializer<TWrite> serializerWrite = null)
        {
            _pipeName = pipeName;
            _serverName = serverName;
            _serializerRead = serializerRead;
            _serializerWrite = serializerWrite;
        }

        /// <summary>
        /// Connects to the named pipe server asynchronously.
        /// This method returns immediately, possibly before the connection has been established.
        /// </summary>
        public void Start()
        {
            _closedExplicitly = false;
            _ctsCancelConnect = new CancellationTokenSource();
            var worker = new Worker();
            worker.Error += OnError;
            worker.DoWork(ListenSync);
        }

        /// <summary>
        ///     Sends a message to the server over a named pipe.
        /// </summary>
        /// <param name="message">Message to send to the server.</param>
        public void PushMessage(TWrite message) => _connection?.PushMessage(message);
        /// <summary>
        /// Closes the named pipe.
        /// </summary>
        public void Stop()
        {
            _ctsCancelConnect.Cancel();
            _closedExplicitly = true;
            _connection?.Close();
        }

        #region Wait for connection/disconnection

        /// <summary>
        /// Waits until the connection has been established, and returns true.
        /// Returns false if the connection has not and will not happen.
        /// </summary>
        public bool WaitForConnection() => WaitHandle.WaitAny(new[] { _connected, _ctsCancelConnect.Token.WaitHandle }) == 0;
        /// <summary>
        /// Waits until the connection has been established or until the timeout, and returns true if the connection has been established.
        /// Returns false if the connection is not established after the timeout.
        /// </summary>
        public bool WaitForConnection(int millisecondsTimeout) => WaitHandle.WaitAny(new[] { _connected, _ctsCancelConnect.Token.WaitHandle }, millisecondsTimeout) == 0;
        /// <summary>
        /// Waits until the connection has been established or until the timeout, and returns true if the connection has been established.
        /// Returns false if the connection is not established after the timeout.
        /// </summary>
        public bool WaitForConnection(TimeSpan timeout) => WaitHandle.WaitAny(new[] { _connected, _ctsCancelConnect.Token.WaitHandle }, timeout) == 0;
        /// <summary>
        /// Waits until the disconnection has been completed, and returns true.
        /// Returns false if the connection was not established, or if the disconnection has not and will not happen.
        /// </summary>
        public bool WaitForDisconnection()
        {
            if (_connection == null) return true;
            return WaitHandle.WaitAny(new[] { _disconnected, _ctsCancelConnect.Token.WaitHandle }) == 0;
        }
        /// <summary>
        /// Waits until the disconnection has been completed or until the timeout, and returns true if the disconnection has been completed.
        /// Returns false if the connection was not established, or if the disconnection has not and will not happen.
        /// </summary>
        public bool WaitForDisconnection(int millisecondsTimeout) => WaitHandle.WaitAny(new[] { _disconnected, _ctsCancelConnect.Token.WaitHandle }, millisecondsTimeout) == 0;
        /// <summary>
        /// Waits until the disconnection has been completed or until the timeout, and returns true if the disconnection has been completed.
        /// Returns false if the connection was not established, or if the disconnection has not and will not happen.
        /// </summary>
        public bool WaitForDisconnection(TimeSpan timeout) => WaitHandle.WaitAny(new[] { _disconnected, _ctsCancelConnect.Token.WaitHandle }, timeout) == 0;

        #endregion

        #region Private methods

        private void ListenSync()
        {
            // Get the name of the data pipe that should be used from now on by this NamedPipeClient
            var handshake = PipeClientFactory.Connect<string, string>(_pipeName, _serverName, StringUtf8Serializer.Instance, StringUtf8Serializer.Instance, _ctsCancelConnect.Token);
            if (handshake == null) { return; }
            var dataPipeName = handshake.ReadObject();
            handshake.Close();

            // Connect to the actual data pipe
            var dataPipe = PipeClientFactory.CreateAndConnectPipe(dataPipeName, _serverName, _ctsCancelConnect.Token);
            if (dataPipe == null) { return; }

            // Create a Connection object for the data pipe
            _connection = ConnectionFactory.CreateConnection<TRead, TWrite>(dataPipe, _serializerRead, _serializerWrite);
            _connection.Disconnected += OnDisconnected;
            _connection.ReceiveMessage += OnReceiveMessage;
            _connection.Error += ConnectionOnError;
            _connection.Open();

            _connected.Set();
        }

        private void OnDisconnected(NamedPipeConnection<TRead, TWrite> connection)
        {
            // set before, so that if the eventhandler wants to connect again, the order of the events is still correct
            _disconnected.Set();
            Disconnected?.Invoke(connection, _closedExplicitly);
        }
        private void OnReceiveMessage(NamedPipeConnection<TRead, TWrite> connection, TRead message) => ServerMessage?.Invoke(connection, message);
        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        private void ConnectionOnError(NamedPipeConnection<TRead, TWrite> connection, Exception exception) => OnError(exception);
        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        /// <param name="exception"></param>
        private void OnError(Exception exception) => Error?.Invoke(exception);

        #endregion
    }
    [UsedImplicitly]
    internal static class PipeClientFactory
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool WaitNamedPipe(string name, int timeout);

        private static bool NamedPipeExists(string pipeName)
        {
            try
            {
                var exists = WaitNamedPipe(pipeName, 0);
                if (exists) return true;
                var error = Marshal.GetLastWin32Error();
                return error != 0 && error != 2;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static PipeStreamWrapper<TRead, TWrite> Connect<TRead, TWrite>(string pipeName, string serverName, ICustomSerializer<TRead> serializerRead, ICustomSerializer<TWrite> serializerWrite, CancellationToken cancelToken)
            where TRead : class
            where TWrite : class
        {
            var inner = CreateAndConnectPipe(pipeName, serverName, cancelToken);
            return inner == null ? null : new PipeStreamWrapper<TRead, TWrite>(inner, serializerRead, serializerWrite);
        }
        public static NamedPipeClientStream CreateAndConnectPipe(string pipeName, string serverName, CancellationToken cancelToken, int timeout = 10)
        {
            var normalizedPath = Path.GetFullPath(string.Format(@"\\{1}\pipe\{0}", pipeName, serverName));
            while (!cancelToken.IsCancellationRequested && !NamedPipeExists(normalizedPath))
                Thread.Sleep(timeout);
            if (cancelToken.IsCancellationRequested) { return null; }
            var pipe = CreatePipe(pipeName, serverName);
            pipe.Connect(1000);
            return pipe;
        }
        private static NamedPipeClientStream CreatePipe(string pipeName, string serverName) => new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);
    }
}
