// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Net.Mail;

namespace Blueprint.Email
{
    /// <summary>An outgoing email message and its attachments.</summary>
    public sealed class BlueprintEmail
    {
        /// <summary>Creates a message with the specified sender address.</summary>
        /// <param name="from">The sender's email address, optionally including a display name.</param>
        public BlueprintEmail(string from)
        {
            From = new MailAddress(from);
        }

        /// <summary>The sender's email address.</summary>
        public MailAddress From { get; }

        /// <summary>The primary recipients.</summary>
        public MailAddressCollection To { get; } = new();

        /// <summary>The carbon copy recipients.</summary>
        public MailAddressCollection CC { get; } = new();

        /// <summary>The blind carbon copy recipients.</summary>
        public MailAddressCollection Bcc { get; } = new();

        /// <summary>The subject line.</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>The message body.</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>Indicates whether the body contains HTML. The default is plain text.</summary>
        public bool IsBodyHtml { get; set; }

        /// <summary>The files included with the message.</summary>
        public List<BlueprintEmailAttachment> Attachments { get; } = new();
    }
}
