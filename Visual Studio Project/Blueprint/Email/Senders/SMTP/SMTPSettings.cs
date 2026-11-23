// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using MailKit.Security;
using System.Net;

namespace Blueprint.Email.Senders.SMTP
{
    /// <summary>Connection and authentication settings for an SMTP server.</summary>
    public sealed class SMTPSettings
    {
        /// <summary>Configures encrypted SMTP submission to the specified server.</summary>
        /// <param name="host">The server name or IP address.</param>
        /// <param name="port">The TCP port. The default is 587.</param>
        /// <exception cref="ArgumentException">The host is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The port is outside the range 1 to 65535.</exception>
        public SMTPSettings(string host, int port = 587)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(host);
            ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
            Host = host;
            Port = port;
            SocketOptions = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        }

        /// <summary>The SMTP server name or IP address.</summary>
        public string Host { get; }

        /// <summary>The SMTP server's TCP port.</summary>
        public int Port { get; }

        /// <summary>Defaults to implicit TLS on port 465 and required STARTTLS on other ports.</summary>
        public SecureSocketOptions SocketOptions { get; init; }

        /// <summary>The credentials used to authenticate, or null for anonymous submission.</summary>
        public NetworkCredential? Credentials { get; init; }
    }
}
