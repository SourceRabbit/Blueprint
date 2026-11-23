// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Email;
using Blueprint.Email.Senders;
using Blueprint.Email.Senders.SMTP;
using MailKit.Security;
using MimeKit;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Blueprint.Tests.Email
{
    /// <summary>Checks both send paths against a loopback SMTP server.</summary>
    internal sealed class SMTPEmailSenderTest : BlueprintTest
    {
        /// <inheritdoc/>
        public override void RunTest()
        {
            ResetAssertions();
            Assert(new SMTPSettings("localhost").SocketOptions == SecureSocketOptions.StartTls, "Submission must require STARTTLS by default.");
            Assert(new SMTPSettings("localhost", 465).SocketOptions == SecureSocketOptions.SslOnConnect, "Port 465 must use implicit TLS by default.");
            VerifySubmission(false);
            VerifySubmission(true);
            Console.WriteLine($"SMTP email checks passed ({getAssertionCount()} assertions).");
        }

        /// <summary>Submits a message and checks the envelope and decoded MIME data received over TCP.</summary>
        /// <param name="useAsync">Whether to exercise asynchronous submission with authentication.</param>
        private void VerifySubmission(bool useAsync)
        {
            using (TcpListener listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                SMTPSettings settings = new SMTPSettings("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port)
                {
                    SocketOptions = SecureSocketOptions.None,
                    Credentials = useAsync ? new NetworkCredential("sender", "test") : null
                };
                BlueprintEmailSender sender = new SMTPEmailSender(settings);
                BlueprintEmail email = new BlueprintEmail("Blueprint <from@example.test>")
                {
                    Subject = "Δοκιμή",
                    Body = useAsync ? "<p>Καλημέρα</p>" : "Καλημέρα",
                    IsBodyHtml = useAsync
                };
                email.To.Add("to@example.test");
                email.CC.Add("cc@example.test");
                email.Bcc.Add("bcc@example.test");
                byte[] content = new byte[] { 0, 1, 127, 255 };
                email.Attachments.Add(new BlueprintEmailAttachment("data.bin", content));

                Task<string> capture = Task.Run(() => ReceiveMessage(listener, useAsync));
                if (useAsync)
                {
                    sender.SendAsync(email).GetAwaiter().GetResult();
                }
                else
                {
                    sender.Send(email);
                }

                using (MemoryStream wire = new MemoryStream(Encoding.UTF8.GetBytes(capture.GetAwaiter().GetResult())))
                {
                    using (MimeMessage message = MimeMessage.Load(wire))
                    {
                        using (MemoryStream decoded = new MemoryStream())
                        {
                            Assert(message.From.Mailboxes.Single().Name == "Blueprint", "The sender's display name must be preserved.");
                            Assert(message.To.Mailboxes.Single().Address == "to@example.test" && message.Cc.Mailboxes.Single().Address == "cc@example.test", "To and CC must keep their roles.");
                            Assert(!message.Headers.Contains(HeaderId.Bcc), "The blind recipient must be absent from the message headers.");
                            Assert(message.Subject == email.Subject, "The subject must round-trip.");
                            Assert((useAsync ? message.HtmlBody : message.TextBody)?.TrimEnd('\r', '\n') == email.Body, "The body and its format must round-trip.");
                            MimePart attachment = (MimePart)message.Attachments.Single();
                            Assert(attachment.FileName == "data.bin" && attachment.ContentType.MimeType == "application/octet-stream", "The attachment metadata must round-trip.");
                            attachment.Content!.DecodeTo(decoded);
                            Assert(decoded.ToArray().SequenceEqual(content), "The attachment bytes must round-trip.");
                        }
                    }
                }
            }
        }

        /// <summary>Accepts one local SMTP transaction and captures the message after checking its envelope.</summary>
        /// <param name="listener">The listening loopback socket.</param>
        /// <param name="authenticate">Whether to expect SMTP AUTH with the test credentials.</param>
        /// <returns>The MIME data sent by the client.</returns>
        private string ReceiveMessage(TcpListener listener, bool authenticate)
        {
            using (TcpClient connection = listener.AcceptTcpClient())
            {
                using (NetworkStream stream = connection.GetStream())
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        using (StreamWriter writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" })
                        {
                            stream.ReadTimeout = 10000;
                            writer.WriteLine("220 localhost ready");
                            Assert(reader.ReadLine()!.StartsWith("EHLO "), "Expected EHLO.");
                            writer.WriteLine("250-localhost");
                            writer.WriteLine("250 AUTH PLAIN");
                            if (authenticate)
                            {
                                string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0sender\0test"));
                                Assert(reader.ReadLine() == "AUTH PLAIN " + credentials, "Expected the configured credentials.");
                                writer.WriteLine("235 authenticated");
                            }
                            Assert(reader.ReadLine() == "MAIL FROM:<from@example.test>", "Expected the envelope sender.");
                            writer.WriteLine("250 ok");
                            HashSet<string> recipients = new HashSet<string>();
                            string? command = reader.ReadLine();
                            while (command != null && command.StartsWith("RCPT TO:"))
                            {
                                recipients.Add(command);
                                writer.WriteLine("250 ok");
                                command = reader.ReadLine();
                            }
                            Assert(recipients.SetEquals(new string[] { "RCPT TO:<to@example.test>", "RCPT TO:<cc@example.test>", "RCPT TO:<bcc@example.test>" }), "Expected all envelope recipients.");
                            Assert(command == "DATA", "Expected message data.");
                            writer.WriteLine("354 send data");
                            StringBuilder data = new StringBuilder();
                            string? line;
                            while ((line = reader.ReadLine()) != ".")
                            {
                                if (line == null)
                                {
                                    throw new EndOfStreamException("The client disconnected before completing DATA.");
                                }
                                data.Append(line.StartsWith("..") ? line.Substring(1) : line).Append("\r\n");
                            }
                            writer.WriteLine("250 accepted");
                            Assert(reader.ReadLine() == "QUIT", "Expected a graceful disconnect.");
                            writer.WriteLine("221 bye");
                            return data.ToString();
                        }
                    }
                }
            }
        }
    }
}
