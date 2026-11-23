// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using MailKit;
using MailKit.Net.Smtp;
using MimeKit;

namespace Blueprint.Email.Senders.SMTP
{
    /// <summary>Sends Blueprint email messages over an SMTP connection opened for each send.</summary>
    public sealed class SMTPEmailSender : BlueprintEmailSender
    {
        private readonly SMTPSettings fSettings;

        /// <summary>Configures the sender for an SMTP server.</summary>
        /// <param name="settings">The server's connection and authentication settings.</param>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public SMTPEmailSender(SMTPSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            fSettings = settings;
        }

        /// <inheritdoc/>
        public override void Send(BlueprintEmail email)
        {
            using (MimeMessage message = new MimeMessage())
            {
                using (SmtpClient client = new SmtpClient())
                {
                    PopulateMessage(message, email);
                    client.Connect(fSettings.Host, fSettings.Port, fSettings.SocketOptions);
                    if (fSettings.Credentials != null)
                    {
                        client.Authenticate(fSettings.Credentials);
                    }
                    client.Send(new SendRequest(message));
                    client.Disconnect(true);
                }
            }
        }

        /// <inheritdoc/>
        public override async Task SendAsync(BlueprintEmail email)
        {
            using (MimeMessage message = new MimeMessage())
            {
                using (SmtpClient client = new SmtpClient())
                {
                    PopulateMessage(message, email);
                    await client.ConnectAsync(fSettings.Host, fSettings.Port, fSettings.SocketOptions).ConfigureAwait(false);
                    if (fSettings.Credentials != null)
                    {
                        await client.AuthenticateAsync(fSettings.Credentials).ConfigureAwait(false);
                    }
                    await client.SendAsync(new SendRequest(message)).ConfigureAwait(false);
                    await client.DisconnectAsync(true).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Copies the sender, recipients, body, and attachments into the outgoing MIME message.</summary>
        /// <param name="message">The MIME message to populate.</param>
        /// <param name="email">The Blueprint message to send.</param>
        /// <exception cref="ArgumentNullException">The email is null.</exception>
        /// <exception cref="ArgumentException">The email has no recipients.</exception>
        private static void PopulateMessage(MimeMessage message, BlueprintEmail email)
        {
            ArgumentNullException.ThrowIfNull(email);
            if (email.To.Count == 0 && email.CC.Count == 0 && email.Bcc.Count == 0)
            {
                throw new ArgumentException("The email must have at least one recipient.", nameof(email));
            }

            message.From.Add((MailboxAddress)email.From);
            message.To.AddRange((InternetAddressList)email.To);
            message.Cc.AddRange((InternetAddressList)email.CC);
            message.Bcc.AddRange((InternetAddressList)email.Bcc);
            message.Subject = email.Subject;

            BodyBuilder body = new BodyBuilder();
            if (email.IsBodyHtml)
            {
                body.HtmlBody = email.Body;
            }
            else
            {
                body.TextBody = email.Body;
            }
            foreach (BlueprintEmailAttachment attachment in email.Attachments)
            {
                body.Attachments.Add(attachment.FileName, attachment.Content.ToArray(), ContentType.Parse(attachment.ContentType));
            }
            message.Body = body.ToMessageBody();
        }
    }
}
