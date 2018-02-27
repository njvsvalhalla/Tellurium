namespace NamedPipeWrapper
{
    using System;
    using System.Collections.Generic;
    using System.IO.Pipes;
    using System.Linq;
    using IO;
    using JetBrains.Annotations;
    using Serialization;
    using Threading;

    /// <inheritdoc />
    /// <summary>
    /// Wraps a <see cref="T:System.IO.Pipes.NamedPipeServerStream" /> and provides multiple simultaneous client connection handling.
    /// </summary>
    /// <typeparam name="TReadWrite">Reference type to read from and write to the named pipe</typeparam>
    [PublicAPI]
    public class NamedPipeServer<TReadWrite> : NamedPipeServer<TReadWrite, TReadWrite> where TReadWrite : class
    {
        /// <summary>
        /// Constructs a new <c>NamedPipeServer</c> object that listens for client connections on the given <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the pipe to listen on</param>
        /// <param name="serializer">Serializer to use. Can be null to use the default serializer</param>
        public NamedPipeServer(string pipeName, ICustomSerializer<TReadWrite> serializer = null)
            : base(pipeName, serializer, serializer)
        {
        }

        /// <summary>
        /// Constructs a new <c>NamedPipeServer</c> object that listens for client connections on the given <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the pipe to listen on</param>
        /// <param name="bufferSize">Size of input and output buffer</param>
        /// <param name="security">And object that determine the access control and audit security for the pipe</param>
        /// <param name="serializer">Serializer to use. Can be null to use the default serializer</param>
        public NamedPipeServer(string pipeName, int bufferSize, PipeSecurity security, ICustomSerializer<TReadWrite> serializer = null)
            : base(pipeName, bufferSize, security, serializer, serializer)
        { }
    }

    /// <summary>
    /// Wraps a <see cref="NamedPipeServerStream"/> and provides multiple simultaneous client connection handling.
    /// </summary>
    /// <typeparam name="TRead">Reference type to read from the named pipe</typeparam>
    /// <typeparam name="TWrite">Reference type to write to the named pipe</typeparam>
    public class NamedPipeServer<TRead, TWrite>
        where TRead : class
        where TWrite : class
    {
        /// <summary>
        /// Invoked whenever a client connects to the server.
        /// </summary>
        public event ConnectionEventHandler<TRead, TWrite> ClientConnected;

        /// <summary>
        /// Invoked whenever a client disconnects from the server.
        /// </summary>
        public event ConnectionEventHandler<TRead, TWrite> ClientDisconnected;

        /// <summary>
        /// Invoked whenever a client sends a message to the server.
        /// </summary>
        public event ConnectionMessageEventHandler<TRead, TWrite> ClientMessage;

        /// <summary>
        /// Invoked whenever an exception is thrown during a read or write operation.
        /// </summary>
        public event PipeExceptionEventHandler Error;

        private readonly string _pipeName;
        private readonly int _bufferSize;
        private readonly PipeSecurity _security;
        private readonly List<NamedPipeConnection<TRead, TWrite>> _connections = new List<NamedPipeConnection<TRead, TWrite>>();

        private int _nextPipeId;

        private volatile bool _shouldKeepRunning;

        private readonly ICustomSerializer<TRead> _serializerRead;
        private readonly ICustomSerializer<TWrite> _serializerWrite;

        /// <summary>
        /// Constructs a new <c>NamedPipeServer</c> object that listens for client connections on the given <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the pipe to listen on</param>
        /// <param name="serializerRead">Serializer to use when reading. Can be null to use the default serializer</param>
        /// <param name="serializerWrite">Serializer to use when writing. Can be null to use the default serializer</param>
        protected NamedPipeServer(string pipeName, ICustomSerializer<TRead> serializerRead = null, ICustomSerializer<TWrite> serializerWrite = null)
        {
            _pipeName = pipeName;
            _serializerRead = serializerRead;
            _serializerWrite = serializerWrite;
        }

        /// <summary>
        /// Constructs a new <c>NamedPipeServer</c> object that listens for client connections on the given <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the pipe to listen on</param>
        /// <param name="bufferSize">Size of input and output buffer</param>
        /// <param name="security">And object that determine the access control and audit security for the pipe</param>
        /// <param name="serializerRead">Serializer to use when reading. Can be null to use the default serializer</param>
        /// <param name="serializerWrite">Serializer to use when writing. Can be null to use the default serializer</param>
        protected NamedPipeServer(string pipeName, int bufferSize, PipeSecurity security, ICustomSerializer<TRead> serializerRead = null, ICustomSerializer<TWrite> serializerWrite = null)
        {
            _pipeName = pipeName;
            _bufferSize = bufferSize;
            _security = security;
            _serializerRead = serializerRead;
            _serializerWrite = serializerWrite;
        }

        /// <summary>
        /// Begins listening for client connections in a separate background thread.
        /// This method returns immediately.
        /// </summary>
        public void Start()
        {
            _shouldKeepRunning = true;
            var worker = new Worker();
            worker.Error += OnError;
            worker.DoWork(ListenSync);
        }

        /// <summary>
        /// Sends a message to all connected clients asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        public void PushMessage(TWrite message)
        {
            lock (_connections)
            {
                foreach (var client in _connections)
                {
                    client.PushMessage(message);
                }
            }
        }

        /// <summary>
        /// Sends a message to a specific client asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetId">Specific client ID to send to.</param>
        public void PushMessage(TWrite message, int targetId)
        {
            lock (_connections)
            {
                // Can we speed this up with Linq or does that add overhead?
                foreach (var client in _connections)
                {
                    if (client.Id != targetId) continue;
                    client.PushMessage(message);
                    break;
                }
            }
        }

        /// <summary>
        /// Sends a message to a specific clients asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetIds">A list of client ID's to send to.</param>
        public void PushMessage(TWrite message, List<int> targetIds)
        {
            lock (_connections)
            {
                // Can we speed this up with Linq or does that add overhead?
                foreach (var client in _connections)
                if (targetIds.Contains(client.Id))
                    client.PushMessage(message);
            }
        }
        /// <summary>
        /// Sends a message to a specific clients asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetIds">An array of client ID's to send to.</param>
        public void PushMessage(TWrite message, IEnumerable<int> targetIds) => PushMessage(message, targetIds.ToList());
        /// <summary>
        /// Sends a message to a specific client asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetName">Specific client name to send to.</param>
        public void PushMessage(TWrite message, string targetName)
        {
            lock (_connections)
            {
                // Can we speed this up with Linq or does that add overhead?
                foreach (var client in _connections)
                {
                    if (!client.Name.Equals(targetName)) continue;
                    client.PushMessage(message);
                    break;
                }
            }
        }
        /// <summary>
        /// Sends a message to a specific client asynchronously.
        /// This method returns immediately, possibly before the message has been sent to all clients.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetNames">A list of client names to send to.</param>
        public void PushMessage(TWrite message, List<string> targetNames)
        {
            lock (_connections)
            {
                foreach (var client in _connections)
                {
                    if (targetNames.Contains(client.Name))
                    {
                        client.PushMessage(message);
                    }
                }
            }
        }


        /// <summary>
        /// Closes all open client connections and stops listening for new ones.
        /// </summary>
        public void Stop()
        {
            if (!_shouldKeepRunning)
            {
                return;
            }

            _shouldKeepRunning = false;

            // NOTE: clients might use autoretry. If that is the case, an existing client that gets closed right now will reconnect and take the 'slot' of the dummy client.
            // In order to minimize these chances, create the dummy client now, before closing the clients.
            // This prevents the delays of 2+2 seconds in most scenarios.

            // this dummy connection will use the local server name.
            var dummyClient = new NamedPipeClient<TRead, TWrite>(_pipeName, ".", _serializerRead, _serializerWrite);
            dummyClient.Start();
            dummyClient.WaitForConnection(TimeSpan.FromSeconds(2));

            // the dummy client, if successfully connected, should now be on the list of connections, so this loop will affect it too:
            lock (_connections)
            {
                foreach (var client in _connections.ToArray())
                {
                    client.Close();
                }
            }

            // and close the dummy client anyway
            dummyClient.Stop();
            dummyClient.WaitForDisconnection(TimeSpan.FromSeconds(2));
        }

        #region Private methods

        private void ListenSync()
        {
            while (_shouldKeepRunning) { WaitForConnection(); }
        }

        private void WaitForConnection()
        {
            NamedPipeServerStream handshakePipe = null;
            NamedPipeServerStream dataPipe = null;
            NamedPipeConnection<TRead, TWrite> connection = null;

            var connectionPipeName = GetNextConnectionPipeName();

            try
            {
                // Send the client the name of the data pipe to use
                handshakePipe = CreateAndConnectPipe();
                var handshakeWrapper = new PipeStreamWrapper<string, string>(handshakePipe, StringUtf8Serializer.Instance, StringUtf8Serializer.Instance);
                handshakeWrapper.WriteObject(connectionPipeName);
                handshakeWrapper.WaitForPipeDrain();
                handshakeWrapper.Close();

                // Wait for the client to connect to the data pipe
                dataPipe = CreatePipe(connectionPipeName);
                dataPipe.WaitForConnection();

                // Add the client's connection to the list of connections
                connection = ConnectionFactory.CreateConnection<TRead, TWrite>(dataPipe, _serializerRead, _serializerWrite);
                connection.ReceiveMessage += ClientOnReceiveMessage;
                connection.Disconnected += ClientOnDisconnected;
                connection.Error += ConnectionOnError;
                connection.Open();

                lock (_connections) { _connections.Add(connection); }

                ClientOnConnected(connection);
            }
            // Catch the IOException that is raised if the pipe is broken or disconnected.
            catch (Exception e)
            {
                Console.Error.WriteLine("Named pipe is broken or disconnected: {0}", e);

                Cleanup(handshakePipe);
                Cleanup(dataPipe);

                ClientOnDisconnected(connection);
            }
        }

        private NamedPipeServerStream CreateAndConnectPipe()
        {
            return _security == null
                ? PipeServerFactory.CreateAndConnectPipe(_pipeName)
                : PipeServerFactory.CreateAndConnectPipe(_pipeName, _bufferSize, _security);
        }

        private NamedPipeServerStream CreatePipe(string connectionPipeName)
        {
            return _security == null
                ? PipeServerFactory.CreatePipe(connectionPipeName)
                : PipeServerFactory.CreatePipe(connectionPipeName, _bufferSize, _security);
        }

        private void ClientOnConnected(NamedPipeConnection<TRead, TWrite> connection) => ClientConnected?.Invoke(connection);

        private void ClientOnReceiveMessage(NamedPipeConnection<TRead, TWrite> connection, TRead message) => ClientMessage?.Invoke(connection, message);

        private void ClientOnDisconnected(NamedPipeConnection<TRead, TWrite> connection)
        {
            if (connection == null)
                return;

            lock (_connections)
            {
                _connections.Remove(connection);
            }

            ClientDisconnected?.Invoke(connection);
        }

        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        private void ConnectionOnError(NamedPipeConnection<TRead, TWrite> connection, Exception exception)
        {
            OnError(exception);
        }

        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        /// <param name="exception"></param>
        private void OnError(Exception exception) => Error?.Invoke(exception);

        private string GetNextConnectionPipeName() => string.Format("{0}_{1}", _pipeName, ++_nextPipeId);

        private static void Cleanup(NamedPipeServerStream pipe)
        {
            if (pipe == null) return;
            using (var x = pipe) x.Close();
        }

        #endregion
    }
}
