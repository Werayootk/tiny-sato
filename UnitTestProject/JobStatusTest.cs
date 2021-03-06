﻿namespace UnitTestProject
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using TinySato;

    [TestClass]
    public class JobStatusTest
    {
        const byte NULL = 0x00, STX = 0x02, ETX = 0x03, ENQ = 0x05, ESC = 0x1b, FS = 0x1c;
        const byte ASCII_SPACE = 0x20, ASCII_ZERO = 0x30, ASCII_ONE = 0x31,
            ASCII_A = 0x41, ASCII_H = 0x48, ASCII_Z = 0x5a;

        // STATUS4 standard http://www.sato.co.jp/webmanual/printer/cl4nx-j_cl6nx-j/main/main_GUID-D94C3DAD-1A55-4706-A86D-71EF71C6F3E3.html#
        static readonly byte[] HealthOKBody = new byte[]
        {
            NULL, NULL, NULL, FS,
            ENQ,
            STX,

            // JobStatus.ID
            ASCII_SPACE, ASCII_SPACE,

            // JobStatus.Health
            Convert.ToByte('A'),

            // JobStatus.LabelRemaining
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,

            // JobStatus.Name
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,

            ETX,
        };

        static readonly byte[] HealthOnlinePrintingBufferNearFullBody = new byte[]
        {
            NULL, NULL, NULL, FS,
            ENQ,
            STX,

            // JobStatus.ID
            ASCII_SPACE, ASCII_SPACE,

            // JobStatus.Health
            Convert.ToByte('C'),

            // JobStatus.LabelRemaining
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,

            // JobStatus.Name
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,

            ETX,
        };

        static readonly byte[] HealthOfflineBody = new byte[]
        {
            NULL, NULL, NULL, FS,
            ENQ,
            STX,

            // JobStatus.ID
            ASCII_SPACE, ASCII_SPACE,

            // JobStatus.Health
            Convert.ToByte('0'),

            // JobStatus.LabelRemaining
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,

            // JobStatus.Name
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,

            ETX,
        };

        static readonly byte[] HealthErrorPaperBody = new byte[]
        {
            NULL, NULL, NULL, FS,
            ENQ,
            STX,

            // JobStatus.ID
            ASCII_SPACE, ASCII_SPACE,

            // JobStatus.Health
            Convert.ToByte('c'),

            // JobStatus.LabelRemaining
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,

            // JobStatus.Name
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,

            ETX,
        };

        static readonly byte[] HealthErrorHeadBody = new byte[]
        {
            NULL, NULL, NULL, FS,
            ENQ,
            STX,

            // JobStatus.ID
            ASCII_SPACE, ASCII_SPACE,

            // JobStatus.Health
            Convert.ToByte('g'),

            // JobStatus.LabelRemaining
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,
            ASCII_ZERO, ASCII_ZERO, ASCII_ZERO,

            // JobStatus.Name
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,
            ASCII_SPACE, ASCII_SPACE, ASCII_SPACE, ASCII_SPACE,

            ETX,
        };

        static readonly IPEndPoint printEP = new IPEndPoint(IPAddress.Loopback, 9100);
        static readonly TcpListener listener = new TcpListener(printEP) { ExclusiveAddressUse = true };

        [TestInitialize]
        public void Listen()
        {
            listener.Server.NoDelay = true;
            listener.Start(1);
        }

        [TestCleanup]
        public void Stop()
        {
            listener.Stop();
        }

        static async Task<byte[]> ResponseForPrint(IEnumerable<byte[]> health_responses)
        {
            var buffers = new List<byte[]>();

            using (var client = await listener.AcceptTcpClientAsync())
            using (var stream = client.GetStream())
            {
                var i = 0;
                while (true)
                {
                    var dummy = new byte[client.ReceiveBufferSize];
                    var actual_buffer_length = await stream.ReadAsync(dummy, 0, dummy.Length);
                    if (actual_buffer_length == 0) break;

                    var buffer = dummy.Take(actual_buffer_length);
                    if (buffer.SequenceEqual(new byte[] { ENQ }))
                    {
                        var health = health_responses.ElementAt(i);
                        await stream.WriteAsync(health, 0, health.Length);
                        ++i;
                    }

                    buffers.Add(buffer.ToArray());
                }
            }

            return buffers.SelectMany(buffer => buffer).ToArray();
        }

        [TestMethod]
        public async Task OnlineBufferNearFull()
        {
            var task = ResponseForPrint(new List<byte[]> { HealthOnlinePrintingBufferNearFullBody, HealthOKBody });
            var expected = new List<byte>
            {
                ENQ, ENQ,
                STX,
                ESC, ASCII_A,
                ESC, ASCII_Z,
                ETX,
             };

            using (var printer = new Printer(printEP))
            {
                var sent = printer.Send();
                Assert.AreEqual(expected.Count, sent + 2 /* ENQ count */);
            }

            using (task)
            {
                var actual = await task;
                CollectionAssert.AreEqual(expected, actual);
            }
        }

        [TestMethod]
        [ExpectedException(typeof(TinySatoIOException))]
        public void ConnectTimeout()
        {
            var responses = Enumerable.Repeat(HealthOfflineBody, 1000);
            _ = ResponseForPrint(responses);

            using (var printer = new Printer(printEP)) { }
        }

        [TestMethod]
        [ExpectedException(typeof(TinySatoPrinterUnitException))]
        public void HealthErrorHead()
        {
            _ = ResponseForPrint(new List<byte[]> { HealthErrorHeadBody });

            using (var printer = new Printer(printEP)) { }
        }

        [TestMethod]
        public async Task Offline()
        {
            var task = ResponseForPrint(new List<byte[]> { HealthOfflineBody, HealthOKBody });
            var expected = new List<byte>
            {
                ENQ, ENQ,
                STX,
                ESC, ASCII_A,
                ESC, ASCII_Z,
                ETX,
            };

            using (var printer = new Printer(printEP))
            {
                var sent = printer.Send();
                Assert.AreEqual(expected.Count, sent + 2 /* ENQ count */);
            }

            using (task)
            {
                var actual = await task;
                CollectionAssert.AreEqual(expected, actual);
            }
        }

        [TestMethod]
        public async Task PaperError()
        {
            var task = ResponseForPrint(new List<byte[]> { HealthErrorPaperBody, HealthOKBody });
            var expected = new List<byte>
            {
                ENQ, ENQ,
                STX,
                ESC, ASCII_A,
                ESC, ASCII_Z,
                ETX,
            };

            using (var printer = new Printer(printEP))
            {
                var sent = printer.Send();
                Assert.AreEqual(expected.Count, sent + 2 /* + ENQ count */);
            }

            using (task)
            {
                var actual = await task;
                CollectionAssert.AreEqual(expected, actual);
            }
        }

        [TestMethod]
        public async Task WaitBusyAtMultiLabel()
        {
            var task = ResponseForPrint(new List<byte[]> { HealthOKBody, HealthOnlinePrintingBufferNearFullBody, HealthOKBody, HealthOKBody });
            var expected = new List<byte>
            {
                ENQ,
                STX,
                // AddStreamAsync
                ESC, ASCII_A,
                ESC, ASCII_Z,
                ENQ,
                ENQ,

                // SendAsync
                ESC, ASCII_A,
                ESC, ASCII_H, ASCII_ZERO, ASCII_ZERO, ASCII_ZERO, ASCII_ONE,
                ESC, ASCII_Z,
                ENQ,
                ESC, ASCII_A,
                ESC, ASCII_Z,
                ETX,
            };


            using (var printer = new Printer(printEP))
            using (var source = new CancellationTokenSource())
            {
                var sent1 = await printer.AddStreamAsync(source.Token);
                printer.MoveToX(1); // example command
                var sent2 = await printer.SendAsync(source.Token);
                Assert.AreEqual(expected.Count, sent1 + sent2 + 4 /* ENQ count */);
            }

            using (task)
            {
                var actual = await task;
                CollectionAssert.AreEqual(expected, actual);
            }
        }
    }
}
