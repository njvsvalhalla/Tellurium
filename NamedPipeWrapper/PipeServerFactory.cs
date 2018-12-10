namespace NamedPipeWrapper
{
    using System.IO.Pipes;

    internal static class PipeServerFactory
    {
        public static NamedPipeServerStream CreateAndConnectPipe(string pipeName)
        {
            var pipe = CreatePipe(pipeName);
            pipe.WaitForConnection();

            return pipe;
        }

        public static NamedPipeServerStream CreatePipe(string pipeName) 
            => new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.WriteThrough);

        public static NamedPipeServerStream CreateAndConnectPipe(string pipeName, int bufferSize, PipeSecurity security)
        {
            var pipe = CreatePipe(pipeName, bufferSize, security);
            pipe.WaitForConnection();
            return pipe;
        }

        public static NamedPipeServerStream CreatePipe(string pipeName, int bufferSize, PipeSecurity security)
        {
            var serverStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough, bufferSize, bufferSize);
            serverStream.SetAccessControl(security);
            return serverStream;
        }
    }
}
