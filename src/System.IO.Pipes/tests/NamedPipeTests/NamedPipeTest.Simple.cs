﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace System.IO.Pipes.Tests
{
    /// <summary>
    /// The Simple NamedPipe tests cover potentially every-day scenarios that are shared
    /// by all NamedPipes whether they be Server/Client or In/Out/Inout.
    /// </summary>
    public abstract class NamedPipeTest_Simple : NamedPipeTestBase
    {
        /// <summary>
        /// Yields every combination of testing options for the OneWayReadWrites test
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<object[]> OneWayReadWritesMemberData()
        {
            var options = new[] { PipeOptions.None, PipeOptions.Asynchronous };
            var bools = new[] { false, true };
            foreach (PipeOptions serverOption in options)
                foreach (PipeOptions clientOption in options)
                    foreach (bool asyncServerOps in bools)
                        foreach (bool asyncClientOps in bools)
                            yield return new object[] { serverOption, clientOption, asyncServerOps, asyncClientOps };
        }

        [Theory]
        [MemberData("OneWayReadWritesMemberData")]
        public async Task OneWayReadWrites(PipeOptions serverOptions, PipeOptions clientOptions, bool asyncServerOps, bool asyncClientOps)
        {
            using (NamedPipePair pair = CreateNamedPipePair(serverOptions, clientOptions))
            {
                NamedPipeClientStream client = pair.clientStream;
                NamedPipeServerStream server = pair.serverStream;
                byte[] received = new byte[] { 0 };
                Task clientTask = Task.Run(async () =>
                {
                    if (asyncClientOps)
                    {
                        await client.ConnectAsync();
                        if (pair.writeToServer)
                        {
                            received = await ReadBytesAsync(client, sendBytes.Length);
                        }
                        else
                        {
                            await WriteBytesAsync(client, sendBytes);
                        }
                    }
                    else
                    {
                        client.Connect();
                        if (pair.writeToServer)
                        {
                            received = ReadBytes(client, sendBytes.Length);
                        }
                        else
                        {
                            WriteBytes(client, sendBytes);
                        }
                    }
                });
                if (asyncServerOps)
                {
                    await server.WaitForConnectionAsync();
                    if (pair.writeToServer)
                    {
                        await WriteBytesAsync(server, sendBytes);
                    }
                    else
                    {
                        received = await ReadBytesAsync(server, sendBytes.Length);
                    }
                }
                else
                {
                    server.WaitForConnection();
                    if (pair.writeToServer)
                    {
                        WriteBytes(server, sendBytes);
                    }
                    else
                    {
                        received = ReadBytes(server, sendBytes.Length);
                    }
                }

                await clientTask;
                Assert.Equal(sendBytes, received);

                server.Disconnect();
                Assert.False(server.IsConnected);
            }
        }

        [Fact]
        public async Task ClonedServer_ActsAsOriginalServer()
        {
            byte[] msg1 = new byte[] { 5, 7, 9, 10 };
            byte[] received1 = new byte[] { 0, 0, 0, 0 };

            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream serverBase = pair.serverStream;
                NamedPipeClientStream client = pair.clientStream;
                pair.Connect();

                if (pair.writeToServer)
                {
                    Task<int> clientTask = client.ReadAsync(received1, 0, received1.Length);
                    using (NamedPipeServerStream server = new NamedPipeServerStream(PipeDirection.Out, false, true, serverBase.SafePipeHandle))
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            Assert.Equal(1, client.NumberOfServerInstances);
                        }
                        server.Write(msg1, 0, msg1.Length);
                        int receivedLength = await clientTask;
                        Assert.Equal(msg1.Length, receivedLength);
                        Assert.Equal(msg1, received1);
                    }
                }
                else
                {
                    Task clientTask = client.WriteAsync(msg1, 0, msg1.Length);
                    using (NamedPipeServerStream server = new NamedPipeServerStream(PipeDirection.In, false, true, serverBase.SafePipeHandle))
                    {
                        int receivedLength = server.Read(received1, 0, msg1.Length);
                        Assert.Equal(msg1.Length, receivedLength);
                        Assert.Equal(msg1, received1);
                        await clientTask;
                    }
                }
            }
        }

        [Fact]
        public async Task ClonedClient_ActsAsOriginalClient()
        {
            byte[] msg1 = new byte[] { 5, 7, 9, 10 };
            byte[] received1 = new byte[] { 0, 0, 0, 0 };

            using (NamedPipePair pair = CreateNamedPipePair())
            {
                pair.Connect();
                NamedPipeServerStream server = pair.serverStream;
                if (pair.writeToServer)
                {
                    using (NamedPipeClientStream client = new NamedPipeClientStream(PipeDirection.In, false, true, pair.clientStream.SafePipeHandle))
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            Assert.Equal(1, client.NumberOfServerInstances);
                        }
                        Task<int> clientTask = client.ReadAsync(received1, 0, received1.Length);
                        server.Write(msg1, 0, msg1.Length);
                        int receivedLength = await clientTask;
                        Assert.Equal(msg1.Length, receivedLength);
                        Assert.Equal(msg1, received1);
                    }
                }
                else
                {
                    using (NamedPipeClientStream client = new NamedPipeClientStream(PipeDirection.Out, false, true, pair.clientStream.SafePipeHandle))
                    {
                        Task clientTask = client.WriteAsync(msg1, 0, msg1.Length);
                        int receivedLength = server.Read(received1, 0, msg1.Length);
                        Assert.Equal(msg1.Length, receivedLength);
                        Assert.Equal(msg1, received1);
                        await clientTask;
                    }
                }
            }
        }

        [Fact]
        public void ConnectOnAlreadyConnectedClient_Throws_InvalidOperationException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                NamedPipeClientStream client = pair.clientStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                pair.Connect();

                Assert.True(client.IsConnected);
                Assert.True(server.IsConnected);

                Assert.Throws<InvalidOperationException>(() => client.Connect());
            }
        }

        [Fact]
        public void WaitForConnectionOnAlreadyConnectedServer_Throws_InvalidOperationException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                NamedPipeClientStream client = pair.clientStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                pair.Connect();

                Assert.True(client.IsConnected);
                Assert.True(server.IsConnected);

                Assert.Throws<InvalidOperationException>(() => server.WaitForConnection());
            }
        }

        [Fact]
        public async Task CancelTokenOn_ServerWaitForConnectionAsync_Throws_OperationCanceledException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                var ctx = new CancellationTokenSource();

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) // [ActiveIssue(812, PlatformID.AnyUnix)] - cancellation token after the operation has been initiated
                {
                    Task serverWaitTimeout = server.WaitForConnectionAsync(ctx.Token);
                    ctx.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverWaitTimeout);
                }

                ctx.Cancel();
                Assert.True(server.WaitForConnectionAsync(ctx.Token).IsCanceled);
            }
        }

        [Fact]
        [PlatformSpecific(PlatformID.Windows)]
        public async Task CancelTokenOff_ServerWaitForConnectionAsyncWithOuterCancellation_Throws_OperationCanceledException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                Task waitForConnectionTask = server.WaitForConnectionAsync(CancellationToken.None);

                Assert.True(Interop.CancelIoEx(server.SafePipeHandle), "Outer cancellation failed");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitForConnectionTask);
                Assert.True(waitForConnectionTask.IsCanceled);
            }
        }

        [Fact]
        [PlatformSpecific(PlatformID.Windows)]
        public async Task CancelTokenOn_ServerWaitForConnectionAsyncWithOuterCancellation_Throws_IOException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                var cts = new CancellationTokenSource();
                NamedPipeServerStream server = pair.serverStream;
                Task waitForConnectionTask = server.WaitForConnectionAsync(cts.Token);

                Assert.True(Interop.CancelIoEx(server.SafePipeHandle), "Outer cancellation failed");
                await Assert.ThrowsAsync<IOException>(() => waitForConnectionTask);
            }
        }

        [Fact]
        public async Task OperationsOnDisconnectedServer()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                pair.Connect();

                Assert.Throws<InvalidOperationException>(() => server.IsMessageComplete);
                Assert.Throws<InvalidOperationException>(() => server.WaitForConnection());
                await Assert.ThrowsAsync<InvalidOperationException>(() => server.WaitForConnectionAsync()); // fails because allowed connections is set to 1

                server.Disconnect();

                Assert.Throws<InvalidOperationException>(() => server.Disconnect());    // double disconnect

                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                if (pair.writeToServer)
                {
                    Assert.Throws<InvalidOperationException>(() => server.Write(buffer, 0, buffer.Length));
                    Assert.Throws<InvalidOperationException>(() => server.WriteByte(5));
                    Assert.Throws<InvalidOperationException>(() => { server.WriteAsync(buffer, 0, buffer.Length); });
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => server.Read(buffer, 0, buffer.Length));
                    Assert.Throws<InvalidOperationException>(() => server.ReadByte());
                    Assert.Throws<InvalidOperationException>(() => { server.ReadAsync(buffer, 0, buffer.Length); });
                }

                Assert.Throws<InvalidOperationException>(() => server.Flush());
                Assert.Throws<InvalidOperationException>(() => server.IsMessageComplete);
                Assert.Throws<InvalidOperationException>(() => server.GetImpersonationUserName());
            }
        }

        [Fact]
        public virtual async Task OperationsOnDisconnectedClient()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                NamedPipeClientStream client = pair.clientStream;
                pair.Connect();

                Assert.Throws<InvalidOperationException>(() => client.IsMessageComplete);
                Assert.Throws<InvalidOperationException>(() => client.Connect());
                await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());

                server.Disconnect();

                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                if (!pair.writeToServer)
                {
                    // Pipe is broken
                    Assert.Throws<IOException>(() => client.Write(buffer, 0, buffer.Length));
                    Assert.Throws<IOException>(() => client.WriteByte(5));
                    Assert.Throws<IOException>(() => { client.WriteAsync(buffer, 0, buffer.Length); });
                    Assert.Throws<IOException>(() => client.Flush());
                    Assert.Throws<IOException>(() => client.NumberOfServerInstances);
                }
                else
                {
                    // Nothing for the client to read, but no exception throwing
                    Assert.Equal(0, client.Read(buffer, 0, buffer.Length));
                    Assert.Equal(-1, client.ReadByte());

                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Assert.Throws<PlatformNotSupportedException>(() => client.NumberOfServerInstances);
                    }
                }

                Assert.Throws<InvalidOperationException>(() => client.IsMessageComplete);
            }
        }

        [Fact]
        [PlatformSpecific(PlatformID.Windows)]
        public async Task Windows_OperationsOnNamedServerWithDisposedClient()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                pair.Connect();
                pair.clientStream.Dispose();

                Assert.Throws<IOException>(() => server.WaitForConnection());
                await Assert.ThrowsAsync<IOException>(() => server.WaitForConnectionAsync());
                Assert.Throws<IOException>(() => server.GetImpersonationUserName());
            }
        }

        [Fact]
        [PlatformSpecific(PlatformID.AnyUnix)]
        public async Task Unix_OperationsOnNamedServerWithDisposedClient()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                pair.Connect();
                pair.clientStream.Dispose();

                // On Unix, the server still thinks that it is connected after client Disposal.
                Assert.Throws<InvalidOperationException>(() => server.WaitForConnection());
                await Assert.ThrowsAsync<InvalidOperationException>(() => server.WaitForConnectionAsync());
                Assert.Throws<PlatformNotSupportedException>(() => server.GetImpersonationUserName());
            }
        }

        [Fact]
        public void OperationsOnUnconnectedServer()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;

                // doesn't throw exceptions
                PipeTransmissionMode transmitMode = server.TransmissionMode;
                Assert.Throws<ArgumentOutOfRangeException>(() => server.ReadMode = (PipeTransmissionMode)999);

                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                if (pair.writeToServer)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        Assert.Equal(0, server.OutBufferSize);
                    else
                        Assert.Throws<PlatformNotSupportedException>(() => server.OutBufferSize);

                    Assert.Throws<InvalidOperationException>(() => server.Write(buffer, 0, buffer.Length));
                    Assert.Throws<InvalidOperationException>(() => server.WriteByte(5));
                    Assert.Throws<InvalidOperationException>(() => { server.WriteAsync(buffer, 0, buffer.Length); });
                }
                else
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        Assert.Equal(0, server.InBufferSize);
                    else
                        Assert.Throws<PlatformNotSupportedException>(() => server.InBufferSize);

                    PipeTransmissionMode readMode = server.ReadMode;
                    Assert.Throws<InvalidOperationException>(() => server.Read(buffer, 0, buffer.Length));
                    Assert.Throws<InvalidOperationException>(() => server.ReadByte());
                    Assert.Throws<InvalidOperationException>(() => { server.ReadAsync(buffer, 0, buffer.Length); });
                }

                Assert.Throws<InvalidOperationException>(() => server.Disconnect());    // disconnect when not connected 
                Assert.Throws<InvalidOperationException>(() => server.IsMessageComplete);
            }
        }

        [Fact]
        public void OperationsOnUnconnectedClient()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeClientStream client = pair.clientStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                if (client.CanRead)
                {
                    Assert.Throws<InvalidOperationException>(() => client.Read(buffer, 0, buffer.Length));
                    Assert.Throws<InvalidOperationException>(() => client.ReadByte());
                    Assert.Throws<InvalidOperationException>(() => { client.ReadAsync(buffer, 0, buffer.Length); });
                    Assert.Throws<InvalidOperationException>(() => client.ReadMode);
                    Assert.Throws<InvalidOperationException>(() => client.ReadMode = PipeTransmissionMode.Byte);
                }
                if (client.CanWrite)
                {
                    Assert.Throws<InvalidOperationException>(() => client.Write(buffer, 0, buffer.Length));
                    Assert.Throws<InvalidOperationException>(() => client.WriteByte(5));
                    Assert.Throws<InvalidOperationException>(() => { client.WriteAsync(buffer, 0, buffer.Length); });
                }

                Assert.Throws<InvalidOperationException>(() => client.NumberOfServerInstances);
                Assert.Throws<InvalidOperationException>(() => client.TransmissionMode);
                Assert.Throws<InvalidOperationException>(() => client.InBufferSize);
                Assert.Throws<InvalidOperationException>(() => client.OutBufferSize);
                Assert.Throws<InvalidOperationException>(() => client.SafePipeHandle);
            }
        }

        [Fact]
        public async Task DisposedServerPipe_Throws_ObjectDisposedException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream pipe = pair.serverStream;
                pipe.Dispose();
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                Assert.Throws<ObjectDisposedException>(() => pipe.Disconnect());
                Assert.Throws<ObjectDisposedException>(() => pipe.GetImpersonationUserName());
                Assert.Throws<ObjectDisposedException>(() => pipe.WaitForConnection());
                await Assert.ThrowsAsync<ObjectDisposedException>(() => pipe.WaitForConnectionAsync());
            }
        }

        [Fact]
        public async Task DisposedClientPipe_Throws_ObjectDisposedException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                pair.Connect();
                NamedPipeClientStream pipe = pair.clientStream;
                pipe.Dispose();
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                Assert.Throws<ObjectDisposedException>(() => pipe.Connect());
                await Assert.ThrowsAsync<ObjectDisposedException>(() => pipe.ConnectAsync());
                Assert.Throws<ObjectDisposedException>(() => pipe.NumberOfServerInstances);
            }
        }

        [Fact]
        [ActiveIssue(812, PlatformID.AnyUnix)] // the cancellation token is ignored after the operation is initiated, due to base Stream's implementation        
        public async Task Server_ReadWriteCancelledToken_Throws_OperationCanceledException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                pair.Connect();

                if (server.CanRead)
                {
                    var ctx1 = new CancellationTokenSource();
                    Task serverReadToken = server.ReadAsync(buffer, 0, buffer.Length, ctx1.Token);
                    ctx1.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverReadToken);
                    Assert.True(server.ReadAsync(buffer, 0, buffer.Length, ctx1.Token).IsCanceled);
                }
                if (server.CanWrite)
                {
                    var ctx1 = new CancellationTokenSource();
                    Task serverWriteToken = server.WriteAsync(buffer, 0, buffer.Length, ctx1.Token);
                    ctx1.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverWriteToken);
                    Assert.True(server.WriteAsync(buffer, 0, buffer.Length, ctx1.Token).IsCanceled);
                }
            }
        }

        [Fact]
        [PlatformSpecific(PlatformID.Windows)]
        public async Task CancelTokenOff_Server_ReadWriteCancelledToken_Throws_OperationCanceledException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                pair.Connect();

                if (server.CanRead)
                {
                    Task serverReadToken = server.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);

                    Assert.True(Interop.CancelIoEx(server.SafePipeHandle), "Outer cancellation failed");
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverReadToken);
                    Assert.True(serverReadToken.IsCanceled);
                }
                if (server.CanWrite)
                {
                    Task serverWriteToken = server.WriteAsync(buffer, 0, buffer.Length, CancellationToken.None);

                    Assert.True(Interop.CancelIoEx(server.SafePipeHandle), "Outer cancellation failed");
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverWriteToken);
                    Assert.True(serverWriteToken.IsCanceled);
                }
            }
        }

        [Fact]
        [PlatformSpecific(PlatformID.Windows)]
        public async Task CancelTokenOn_Server_ReadWriteCancelledToken_Throws_IOException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeServerStream server = pair.serverStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                pair.Connect();

                if (server.CanRead)
                {
                    var cts = new CancellationTokenSource();
                    Task serverReadToken = server.ReadAsync(buffer, 0, buffer.Length, cts.Token);

                    Assert.True(Interop.CancelIoEx(server.SafePipeHandle), "Outer cancellation failed");
                    await Assert.ThrowsAsync<IOException>(() => serverReadToken);
                }
                if (server.CanWrite)
                {
                    var cts = new CancellationTokenSource();
                    Task serverWriteToken = server.WriteAsync(buffer, 0, buffer.Length, cts.Token);

                    Assert.True(Interop.CancelIoEx(server.SafePipeHandle), "Outer cancellation failed");
                    await Assert.ThrowsAsync<IOException>(() => serverWriteToken);
                }
            }
        }

        [Fact]
        [ActiveIssue(812, PlatformID.AnyUnix)] // the cancellation token is ignored after the operation is initiated, due to base Stream's implementation        
        public async Task Client_ReadWriteCancelledToken_Throws_OperationCanceledException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeClientStream client = pair.clientStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                pair.Connect();
                if (client.CanRead)
                {
                    var ctx1 = new CancellationTokenSource();
                    Task serverReadToken = client.ReadAsync(buffer, 0, buffer.Length, ctx1.Token);
                    ctx1.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverReadToken);
                    Assert.True(client.ReadAsync(buffer, 0, buffer.Length, ctx1.Token).IsCanceled);
                }
                if (client.CanWrite)
                {
                    var ctx1 = new CancellationTokenSource();
                    Task serverWriteToken = client.WriteAsync(buffer, 0, buffer.Length, ctx1.Token);
                    ctx1.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverWriteToken);
                    Assert.True(client.WriteAsync(buffer, 0, buffer.Length, ctx1.Token).IsCanceled);
                }
            }
        }

        [Fact]
        [PlatformSpecific(PlatformID.Windows)]
        public async Task CancelTokenOff_Client_ReadWriteCancelledToken_Throws_OperationCanceledException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeClientStream client = pair.clientStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                pair.Connect();
                if (client.CanRead)
                {
                    Task clientReadToken = client.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);

                    Assert.True(Interop.CancelIoEx(client.SafePipeHandle), "Outer cancellation failed");
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clientReadToken);
                    Assert.True(clientReadToken.IsCanceled);
                }
                if (client.CanWrite)
                {
                    Task clientWriteToken = client.WriteAsync(buffer, 0, buffer.Length, CancellationToken.None);

                    Assert.True(Interop.CancelIoEx(client.SafePipeHandle), "Outer cancellation failed");
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clientWriteToken);
                    Assert.True(clientWriteToken.IsCanceled);
                }
            }
        }

        [Fact]
        [PlatformSpecific(PlatformID.Windows)]
        public async Task CancelTokenOn_Client_ReadWriteCancelledToken_Throws_IOException()
        {
            using (NamedPipePair pair = CreateNamedPipePair())
            {
                NamedPipeClientStream client = pair.clientStream;
                byte[] buffer = new byte[] { 0, 0, 0, 0 };

                pair.Connect();
                if (client.CanRead)
                {
                    var cts = new CancellationTokenSource();
                    Task clientReadToken = client.ReadAsync(buffer, 0, buffer.Length, cts.Token);

                    Assert.True(Interop.CancelIoEx(client.SafePipeHandle), "Outer cancellation failed");
                    await Assert.ThrowsAsync<IOException>(() => clientReadToken);
                }
                if (client.CanWrite)
                {
                    var cts = new CancellationTokenSource();
                    Task clientWriteToken = client.WriteAsync(buffer, 0, buffer.Length, cts.Token);

                    Assert.True(Interop.CancelIoEx(client.SafePipeHandle), "Outer cancellation failed");
                    await Assert.ThrowsAsync<IOException>(() => clientWriteToken);
                }
            }
        }
    }

    public class NamedPipeTest_Simple_ServerInOutRead_ClientInOutWrite : NamedPipeTest_Simple
    {
        protected override NamedPipePair CreateNamedPipePair(PipeOptions serverOptions, PipeOptions clientOptions)
        {
            NamedPipePair ret = new NamedPipePair();
            string pipeName = GetUniquePipeName();
            ret.serverStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, serverOptions);
            ret.clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, clientOptions);
            ret.writeToServer = false;
            return ret;
        }

        [Fact]
        public override async Task OperationsOnDisconnectedClient()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                await base.OperationsOnDisconnectedClient();
            else
            {
                using (NamedPipePair pair = CreateNamedPipePair())
                {
                    NamedPipeServerStream server = pair.serverStream;
                    NamedPipeClientStream client = pair.clientStream;
                    pair.Connect();

                    Assert.Throws<InvalidOperationException>(() => client.IsMessageComplete);
                    Assert.Throws<InvalidOperationException>(() => client.Connect());
                    await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());

                    server.Disconnect();

                    byte[] buffer = new byte[] { 0, 0, 0, 0 };

                    // The pipe is broken, but Unix InOut will allow the writes regardless
                    client.Write(buffer, 0, buffer.Length);
                    client.WriteByte(5);
                    await client.WriteAsync(buffer, 0, buffer.Length);
                    client.Flush();
                    Assert.Throws<InvalidOperationException>(() => client.IsMessageComplete);
                    Assert.Throws<PlatformNotSupportedException>(() => client.NumberOfServerInstances);
                }
            }
        }
    }

    public class NamedPipeTest_Simple_ServerInOutWrite_ClientInOutRead : NamedPipeTest_Simple
    {
        protected override NamedPipePair CreateNamedPipePair(PipeOptions serverOptions, PipeOptions clientOptions)
        {
            NamedPipePair ret = new NamedPipePair();
            string pipeName = GetUniquePipeName();
            ret.serverStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, serverOptions);
            ret.clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, clientOptions);
            ret.writeToServer = true;
            return ret;
        }

        [Fact]
        public override async Task OperationsOnDisconnectedClient()
        {
            // Any reads to the client will wait indefinitely on Unix regardless of the disconnected server
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                await base.OperationsOnDisconnectedClient();
        }
    }

    public class NamedPipeTest_Simple_ServerInOut_ClientIn : NamedPipeTest_Simple
    {
        protected override NamedPipePair CreateNamedPipePair(PipeOptions serverOptions, PipeOptions clientOptions)
        {
            NamedPipePair ret = new NamedPipePair();
            string pipeName = GetUniquePipeName();
            ret.serverStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, serverOptions);
            ret.clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.In, clientOptions);
            ret.writeToServer = true;
            return ret;
        }
    }

    public class NamedPipeTest_Simple_ServerInOut_ClientOut : NamedPipeTest_Simple
    {
        protected override NamedPipePair CreateNamedPipePair(PipeOptions serverOptions, PipeOptions clientOptions)
        {
            NamedPipePair ret = new NamedPipePair();
            string pipeName = GetUniquePipeName();
            ret.serverStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, serverOptions);
            ret.clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, clientOptions);
            ret.writeToServer = false;
            return ret;
        }
    }

    public class NamedPipeTest_Simple_ServerOut_ClientIn : NamedPipeTest_Simple
    {
        protected override NamedPipePair CreateNamedPipePair(PipeOptions serverOptions, PipeOptions clientOptions)
        {
            NamedPipePair ret = new NamedPipePair();
            string pipeName = GetUniquePipeName();
            ret.serverStream = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, serverOptions);
            ret.clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.In, clientOptions);
            ret.writeToServer = true;
            return ret;
        }
    }

    public class NamedPipeTest_Simple_ServerIn_ClientOut : NamedPipeTest_Simple
    {
        protected override NamedPipePair CreateNamedPipePair(PipeOptions serverOptions, PipeOptions clientOptions)
        {
            NamedPipePair ret = new NamedPipePair();
            string pipeName = GetUniquePipeName();
            ret.serverStream = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, serverOptions);
            ret.clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, clientOptions);
            ret.writeToServer = false;
            return ret;
        }
    }
      
}
