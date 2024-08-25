using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Cave.Console;

#if !(NET20 || NET35)

using System.Runtime.ExceptionServices;

#endif

namespace Cave.Compression.Tests.TestSupport
{
    internal static class PerformanceTesting
    {
        const double ByteToMB = 1000000;
        const int PacifierOffset = 0x100000;

        public static void TestReadWrite(TestDataSize size, Func<Stream, Stream> input, Func<Stream, Stream> output, Action<Stream> outputClose = null)
            => TestReadWrite((int)size, input, output);

        public static void TestWrite(TestDataSize size, Func<Stream, Stream> output, Action<Stream> outputClose = null)
            => TestWrite((int)size, output, outputClose);

        public static void TestReadWrite(int size, Func<Stream, Stream> input, Func<Stream, Stream> output, Action<Stream> outputClose = null)
        {
#if !(NET20 || NET35 || NET40)
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var window = new WindowedStream(size, cts.Token);
#else
            var window = new WindowedStream(size);
#endif

            var readerState = new PerfWorkerState()
            {
                bytesLeft = size,
#if !(NET20 || NET35 || NET40)
                token = cts.Token,
#endif
                baseStream = window,
                streamCtr = input,
            };

            var writerState = new PerfWorkerState()
            {
                bytesLeft = size,
#if !(NET20 || NET35 || NET40)
                token = cts.Token,
#endif
                baseStream = window,
                streamCtr = output,
                streamCls = outputClose
            };

            var reader = new Thread(stateObject =>
            {
                var state = (PerfWorkerState)stateObject;
                try
                {
                    // Run output stream constructor
                    state.InitStream();

                    // Main read loop
                    ReadTargetBytes(ref state);

#if !(NET20 || NET35)
                    if (!state.token.IsCancellationRequested)
#endif
                    {
                        Assert.IsFalse(state.baseStream.CanRead, "Base Stream should be closed");

                        // This shouldnt read any data but should read the footer
                        var buffer = new byte[1];
                        var readBytes = state.stream.Read(buffer, 0, 1);
                        Assert.LessOrEqual(readBytes, 0, "Stream should be empty");
                    }

                    // Dispose of the input stream
                    state.stream.Close();
                }
                catch (Exception x)
                {
                    state.exception = x;
                }
            });

            var writer = new Thread(stateObject =>
            {
                var state = (PerfWorkerState)stateObject;
                try
                {
                    // Run input stream constructor
                    state.InitStream();

                    // Main write loop
                    WriteTargetBytes(ref state);

                    state.DeinitStream();

                    // Dispose of the input stream
                    state.stream.Close();
                }
                catch (Exception x)
                {
                    state.exception = x;
                }
            });

            var sw = Stopwatch.StartNew();

            writer.Name = "Writer";
            writer.Start(writerState);

            // Give the writer thread a couple of seconds to write headers
            Thread.Sleep(TimeSpan.FromSeconds(3));

            reader.Name = "Reader";
            reader.Start(readerState);

            bool writerJoined = false, readerJoined = false;
            const int timeout = 100;

            while (!writerJoined && !readerJoined)
            {
                writerJoined = writer.Join(timeout);
                if (writerJoined && writerState.exception != null)
                {
#if !(NET20 || NET35 || NET40)
                    ExceptionDispatchInfo.Capture(writerState.exception).Throw();
#endif
                }

                readerJoined = reader.Join(timeout);
                if (readerJoined && readerState.exception != null)
                {
#if !(NET20 || NET35 || NET40)
                    ExceptionDispatchInfo.Capture(readerState.exception).Throw();
#endif
                }

#if !(NET20 || NET35 || NET40)
                if (cts.IsCancellationRequested)
                {
                    break;
                }
#endif
            }

            //Assert.IsTrue(writerJoined, "Timed out waiting for reader thread to join");
            //Assert.IsTrue(readerJoined, "Timed out waiting for writer thread to join");

#if !(NET20 || NET35 || NET40)
            Assert.IsFalse(cts.IsCancellationRequested, "Threads were cancelled before completing execution");
#endif

            var elapsed = sw.Elapsed;
            var testSize = size / ByteToMB;
            SystemConsole.WriteLine($"Time {elapsed:mm\\:ss\\.fff} throughput {testSize / elapsed.TotalSeconds:f2} MB/s (using test size: {testSize:f2} MB)");
        }

        public static void TestWrite(int size, Func<Stream, Stream> output, Action<Stream> outputClose = null)
        {
#if !(NET20 || NET35 || NET40)
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
#endif
            var sw = Stopwatch.StartNew();
            var writerState = new PerfWorkerState()
            {
                bytesLeft = size,
#if !(NET20 || NET35 || NET40)
                token = cts.Token,
#endif
                baseStream = new NullStream(),
                streamCtr = output,
            };

            writerState.InitStream();
            WriteTargetBytes(ref writerState);

            writerState.DeinitStream();

            writerState.stream.Close();

            var elapsed = sw.Elapsed;
            var testSize = size / ByteToMB;
            SystemConsole.WriteLine($"Time {elapsed:mm\\:ss\\.fff} throughput {testSize / elapsed.TotalSeconds:f2} MB/s (using test size: {testSize:f2} MB)");
        }

        internal static void WriteTargetBytes(ref PerfWorkerState state)
        {
            const int bufferSize = 8192;
            var buffer = new byte[bufferSize];
            var bytesToWrite = bufferSize;

            while (state.bytesLeft > 0
#if !(NET20 || NET35)
                && !state.token.IsCancellationRequested
#endif
                )
            {
                if (state.bytesLeft < bufferSize)
                {
                    bytesToWrite = bufferSize;
                }

                state.stream.Write(buffer, 0, bytesToWrite);
                state.bytesLeft -= bytesToWrite;
            }
        }

        internal static void ReadTargetBytes(ref PerfWorkerState state)
        {
            const int bufferSize = 8192;
            var buffer = new byte[bufferSize];
            int bytesRead, bytesToRead = bufferSize;

            var pacifierLevel = state.bytesLeft - PacifierOffset;

            while (state.bytesLeft > 0
#if !(NET20 || NET35)
                && !state.token.IsCancellationRequested
#endif
                )
            {
                if (state.bytesLeft < bufferSize)
                {
                    bytesToRead = bufferSize;
                }

                bytesRead = state.stream.Read(buffer, 0, bytesToRead);
                state.bytesLeft -= bytesRead;

                if (state.bytesLeft <= pacifierLevel)
                {
                    Debug.WriteLine($"Reader {state.bytesLeft} bytes remaining");
                    pacifierLevel = state.bytesLeft - PacifierOffset;
                }

                if (bytesRead == 0)
                {
                    break;
                }
            }
        }
    }

    internal class PerfWorkerState
    {
        public Stream stream;
        public Stream baseStream;
        public int bytesLeft;
        public Exception exception;
        public Func<Stream, Stream> streamCtr;
        public Action<Stream> streamCls;
#if !(NET20 || NET35)
        public CancellationToken token;
#endif

        public void InitStream()
        {
            stream = streamCtr(baseStream);
        }

        public void DeinitStream()
        {
            streamCls?.Invoke(stream);
        }
    }

    public enum TestDataSize : int
    {
        Large = 0x10000000,
        Medium = 0x5000000,
        Small = 0x1400000,
    }
}
